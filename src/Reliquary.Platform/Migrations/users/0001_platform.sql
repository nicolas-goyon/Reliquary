-- The shard directory gives the shard database of each owner. See docs/architecture/database.md.
create table users.shard_directory (
    owner_id uuid primary key,
    shard text not null,
    read_only boolean not null default false,
    assigned_at timestamptz not null default now()
);

create index shard_directory_shard_idx on users.shard_directory (shard);

-- The last run of each scheduled job. The worker reads it to know when a job is due.
create table users.job_runs (
    name text primary key,
    last_started_at timestamptz,
    last_finished_at timestamptz,
    last_status text,
    last_error text
);

-- The outbox of the users database. The core components and the modules write their domain events
-- here, in the transaction of the data change. The outbox publisher sends them to the consumers.
create table users.outbox (
    id uuid primary key,
    event_type text not null,
    event_version integer not null,
    owner_id uuid,
    payload jsonb not null,
    occurred_at timestamptz not null,
    -- The publisher sends the event after this time. A failure moves it later.
    available_at timestamptz not null,
    published_at timestamptz,
    attempts integer not null default 0,
    last_error text
);

create index outbox_pending_idx on users.outbox (available_at, id) where published_at is null;
