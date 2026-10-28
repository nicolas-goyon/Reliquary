-- The collection shares. A guest must find the shares of other owners, so the table is in the users database.
-- Milestone M2 creates the table and does not use it. See docs/decisions/0034-collection-model.md.
create table users.collection_shares (
    id uuid primary key,
    owner_id uuid not null,
    guest_id uuid not null,
    -- The shared locations, or null for all the locations of the owner.
    location_ids uuid[],
    rights text not null check (rights in ('read', 'difference', 'edit')),
    created_at timestamptz not null
);

create index collection_shares_owner_idx on users.collection_shares (owner_id);
create index collection_shares_guest_idx on users.collection_shares (guest_id);
