-- The category rules of the users for auto-tag. See docs/decisions/0031-auto-tag.md.
-- A rule gives the categories that the owner chose for an Oracle card. The next suggestions of auto-tag use the rule first.
-- An empty list is a rule too: the owner wants no category for the card.
create table content.category_rules (
    owner_id uuid not null,
    oracle_id uuid not null,
    categories text[] not null
        check (categories <@ array['ramp', 'card_draw', 'targeted_interaction', 'board_wipe', 'synergy', 'land', 'win_condition', 'protection']),
    updated_at timestamptz not null,
    primary key (owner_id, oracle_id)
);
