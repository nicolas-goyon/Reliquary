-- A table for the tests of the shard routing.
create table content.test_items (
    id uuid primary key,
    owner_id uuid not null,
    name text not null
);
