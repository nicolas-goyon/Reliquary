-- The service accounts and the delegations of the modules. See docs/decisions/0037-service-accounts-and-delegation-tokens.md.

-- The service accounts. An administrator creates them. The table stores the prefix and a hash of the secret, never the secret.
create table users.service_accounts (
    id uuid primary key,
    name text not null unique,
    description text not null,
    prefix text not null unique,
    secret_hash bytea not null,
    created_at timestamptz not null,
    last_used_at timestamptz,
    revoked_at timestamptz
);

-- The delegations: a user lets a service account act for them, with some scopes, until a time.
create table users.delegations (
    id uuid primary key,
    account_id uuid not null references users.accounts (id) on delete cascade,
    service_account_id uuid not null references users.service_accounts (id) on delete cascade,
    scopes text[] not null,
    created_at timestamptz not null,
    expires_at timestamptz not null,
    last_used_at timestamptz,
    revoked_at timestamptz
);

create index delegations_account_idx on users.delegations (account_id);
create index delegations_service_account_idx on users.delegations (service_account_id);

-- The short delegation tokens that a service account gets for a delegation. The table stores a hash of each token, never the token.
-- A token stops working when its delegation expires or when the user revokes the delegation.
create table users.delegation_tokens (
    id uuid primary key,
    delegation_id uuid not null references users.delegations (id) on delete cascade,
    prefix text not null unique,
    secret_hash bytea not null,
    scopes text[] not null,
    created_at timestamptz not null,
    expires_at timestamptz not null
);

create index delegation_tokens_delegation_idx on users.delegation_tokens (delegation_id);
