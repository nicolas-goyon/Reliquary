-- The outbox of the reference database. The core components and the modules write their domain events
-- here, in the transaction of the data change. The outbox publisher sends them to the consumers.
create table reference.outbox (
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

create index outbox_pending_idx on reference.outbox (available_at, id) where published_at is null;
