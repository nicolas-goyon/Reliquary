-- The export and the deletion of the personal data of an account. See docs/decisions/0032-personal-data.md.

-- The time of the deletion request. The worker deletes the account 7 days later, when the user did not cancel the request.
alter table users.accounts add column deletion_requested_at timestamptz;

-- The exports of the data of the accounts. The worker makes the archive, and the user downloads it.
create table users.data_exports (
    id uuid primary key,
    account_id uuid not null references users.accounts (id) on delete cascade,
    status text not null check (status in ('pending', 'ready', 'failed')),
    requested_at timestamptz not null,
    completed_at timestamptz,
    -- The worker deletes the archive at this time.
    expires_at timestamptz,
    archive bytea,
    size_bytes bigint
);

create index data_exports_account_idx on users.data_exports (account_id, requested_at desc);
create index data_exports_pending_idx on users.data_exports (requested_at) where status = 'pending';
