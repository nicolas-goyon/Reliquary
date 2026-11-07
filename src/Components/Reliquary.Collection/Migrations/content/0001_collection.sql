-- The collection of each owner, in the shard of the owner. See docs/decisions/0034-collection-model.md.

-- The locations of the cards. A location has two levels at most: a section has a parent location, and a section has no section.
-- A section has the kind of its parent location. Only the Decks component creates a location of the kind 'deck'.
create table content.locations (
    id uuid primary key,
    owner_id uuid not null,
    parent_id uuid references content.locations (id) on delete cascade,
    name text not null,
    kind text not null check (kind in ('rack', 'binder', 'box', 'deck', 'cube')),
    locked boolean not null default false,
    pickable boolean not null default true,
    -- The deck of an assembled deck location.
    deck_id uuid,
    position integer not null default 0,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create index locations_owner_idx on content.locations (owner_id, position, id);
create index locations_parent_idx on content.locations (parent_id);
create unique index locations_deck_idx on content.locations (owner_id, deck_id) where deck_id is not null;

-- The cards of each location. Two entries of one location never have the same card, printing, language and advanced fields.
-- The columns card_name and set_code copy the Oracle name and the set of the printing, for the sort, the search and the filters.
create table content.location_entries (
    id uuid primary key,
    owner_id uuid not null,
    location_id uuid not null references content.locations (id) on delete cascade,
    oracle_id uuid not null,
    printing_id uuid,
    card_name text not null,
    set_code text,
    language text not null,
    finish text check (finish in ('nonfoil', 'foil', 'etched')),
    condition text check (condition in ('mint', 'near_mint', 'excellent', 'good', 'light_played', 'played', 'poor')),
    purchase_price numeric(12, 2) check (purchase_price >= 0),
    purchase_currency text check (purchase_currency ~ '^[A-Z]{3}$'),
    acquired_on date,
    note text,
    quantity integer not null check (quantity between 1 and 100000),
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create unique index location_entries_key_idx on content.location_entries (
    location_id, oracle_id, coalesce(printing_id, '00000000-0000-0000-0000-000000000000'::uuid), language,
    coalesce(finish, ''), coalesce(condition, ''), coalesce(purchase_price, -1), coalesce(purchase_currency, ''),
    coalesce(acquired_on, '0001-01-01'::date), coalesce(note, '')
);
create index location_entries_owner_oracle_idx on content.location_entries (owner_id, oracle_id);
create index location_entries_owner_name_idx on content.location_entries (owner_id, card_name, oracle_id);
create index location_entries_location_name_idx on content.location_entries (location_id, card_name, id);

-- The advanced fields that each owner enabled: finish, condition, purchasePrice, acquiredOn and note.
create table content.collection_settings (
    owner_id uuid primary key,
    advanced_fields text[] not null default '{}'
        check (advanced_fields <@ array['finish', 'condition', 'purchasePrice', 'acquiredOn', 'note']),
    updated_at timestamptz not null
);

-- The movements of the cards. Each change of the entries writes movements in the same transaction.
-- An origin or a destination is a location, 'purchase', 'sale', 'gift', 'trade' or 'nowhere'.
-- The location columns have no foreign key: the history keeps the movements of a deleted location.
create table content.movements (
    id uuid primary key,
    owner_id uuid not null,
    -- The movements of one group action have the same batch ID.
    batch_id uuid not null,
    kind text not null check (kind in ('add', 'remove', 'move')),
    origin_kind text not null check (origin_kind in ('location', 'purchase', 'sale', 'gift', 'trade', 'nowhere')),
    origin_location_id uuid,
    destination_kind text not null check (destination_kind in ('location', 'purchase', 'sale', 'gift', 'trade', 'nowhere')),
    destination_location_id uuid,
    oracle_id uuid not null,
    printing_id uuid,
    card_name text not null,
    language text not null,
    finish text,
    condition text,
    purchase_price numeric(12, 2),
    purchase_currency text,
    acquired_on date,
    note text,
    quantity integer not null check (quantity > 0),
    -- The inventory that wrote the movement, or null.
    inventory_id uuid,
    created_at timestamptz not null,
    check ((origin_kind = 'location') = (origin_location_id is not null)),
    check ((destination_kind = 'location') = (destination_location_id is not null))
);

create index movements_owner_idx on content.movements (owner_id, created_at desc, id desc);
create index movements_batch_idx on content.movements (batch_id);
create index movements_origin_idx on content.movements (origin_location_id) where origin_location_id is not null;
create index movements_destination_idx on content.movements (destination_location_id) where destination_location_id is not null;

-- A movement never changes. Only the deletion of the data of the owner deletes a movement.
create function content.refuse_movement_change() returns trigger language plpgsql as $$
begin
    raise exception 'A movement never changes.';
end;
$$;

create trigger movements_immutable before update on content.movements
    for each row execute function content.refuse_movement_change();

-- The inventories of the locations. An open inventory waits for the answers of the owner.
-- An applied inventory keeps the content of the location at the date of the inventory.
create table content.inventories (
    id uuid primary key,
    owner_id uuid not null,
    location_id uuid not null references content.locations (id) on delete cascade,
    status text not null check (status in ('open', 'applied')),
    created_at timestamptz not null,
    applied_at timestamptz
);

create index inventories_location_idx on content.inventories (location_id, created_at desc);
create index inventories_owner_idx on content.inventories (owner_id);

-- The counted content of an inventory. It does not change after the owner applies the inventory.
create table content.inventory_entries (
    id uuid primary key,
    inventory_id uuid not null references content.inventories (id) on delete cascade,
    owner_id uuid not null,
    oracle_id uuid not null,
    printing_id uuid,
    card_name text not null,
    set_code text,
    language text not null,
    finish text,
    condition text,
    purchase_price numeric(12, 2),
    purchase_currency text,
    acquired_on date,
    note text,
    quantity integer not null check (quantity between 1 and 100000)
);

create index inventory_entries_inventory_idx on content.inventory_entries (inventory_id, card_name);

-- The storage rules: a card search in the syntax of the card search suggests a location for a new card.
-- The rule with the lowest priority number applies first.
create table content.storage_rules (
    id uuid primary key,
    owner_id uuid not null,
    location_id uuid not null references content.locations (id) on delete cascade,
    query text not null,
    priority integer not null,
    created_at timestamptz not null,
    updated_at timestamptz not null
);

create index storage_rules_owner_idx on content.storage_rules (owner_id, priority, id);

-- The tokens of each assembled deck, and whether the owner has them.
create table content.deck_tokens (
    location_id uuid not null references content.locations (id) on delete cascade,
    owner_id uuid not null,
    oracle_id uuid not null,
    owned boolean not null default false,
    updated_at timestamptz not null,
    primary key (location_id, oracle_id)
);

create index deck_tokens_owner_idx on content.deck_tokens (owner_id);
