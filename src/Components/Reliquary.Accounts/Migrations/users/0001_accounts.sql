-- The accounts, the sessions and the API keys of the Accounts component. See docs/architecture/accounts.md.

create table users.accounts (
    id uuid primary key,
    email text not null,
    email_normalized text not null unique,
    email_checked_at timestamptz,
    password_hash text,
    display_name text not null,
    language text not null check (language in ('en', 'fr')),
    is_administrator boolean not null default false,
    -- A new value makes all the refresh tokens of the account invalid, for example after a password reset.
    security_stamp uuid not null,
    created_at timestamptz not null,
    updated_at timestamptz not null,
    last_sign_in_at timestamptz
);

-- The accounts of the OAuth providers that are linked to an account.
create table users.external_logins (
    provider text not null,
    provider_key text not null,
    account_id uuid not null references users.accounts (id) on delete cascade,
    email text,
    created_at timestamptz not null,
    primary key (provider, provider_key)
);

create index external_logins_account_idx on users.external_logins (account_id);

-- The refresh tokens of the web sessions. The table stores a hash of each token, never the token.
-- A family contains a first token and all the tokens that replaced it.
create table users.refresh_tokens (
    id uuid primary key,
    account_id uuid not null references users.accounts (id) on delete cascade,
    family_id uuid not null,
    token_hash bytea not null unique,
    security_stamp uuid not null,
    created_at timestamptz not null,
    expires_at timestamptz not null,
    used_at timestamptz,
    revoked_at timestamptz,
    replaced_by uuid
);

create index refresh_tokens_account_idx on users.refresh_tokens (account_id);
create index refresh_tokens_family_idx on users.refresh_tokens (family_id);

-- The single-use tokens of the emails: the check of an email address and the reset of a password.
create table users.account_tokens (
    id uuid primary key,
    account_id uuid not null references users.accounts (id) on delete cascade,
    purpose text not null check (purpose in ('email_check', 'password_reset')),
    token_hash bytea not null unique,
    created_at timestamptz not null,
    expires_at timestamptz not null,
    used_at timestamptz
);

create index account_tokens_account_idx on users.account_tokens (account_id, purpose);

-- The personal API keys. The table stores the prefix and a hash of the secret, never the secret.
create table users.api_keys (
    id uuid primary key,
    account_id uuid not null references users.accounts (id) on delete cascade,
    name text not null,
    prefix text not null unique,
    secret_hash bytea not null,
    created_at timestamptz not null,
    last_used_at timestamptz,
    revoked_at timestamptz
);

create index api_keys_account_idx on users.api_keys (account_id);

-- The number of API requests of each day, for each API key and each account. The worker copies the counters of Redis here.
create table users.usage_counters (
    day date not null,
    subject_kind text not null check (subject_kind in ('key', 'account')),
    subject_id uuid not null,
    request_count bigint not null,
    primary key (day, subject_kind, subject_id)
);
