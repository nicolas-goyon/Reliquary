-- A script with an error, for the test of a failed migration.
create table users.broken (id uuid primary key, name not_a_type);
