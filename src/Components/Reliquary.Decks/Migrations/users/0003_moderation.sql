-- The moderation of the public content: the reports, the hidden items and the moderation log.
-- The data is global, so it is in the users database. See docs/decisions/0035-moderation-of-public-content.md.
-- The column item_owner_id gives the owner of the item, so the deletion of an account finds all its rows.

-- The reports of the users about a public deck or an author profile.
create table users.moderation_reports (
    id uuid primary key,
    reporter_id uuid not null,
    item_type text not null check (item_type in ('deck', 'author')),
    item_id uuid not null,
    item_owner_id uuid not null,
    -- The name of the deck or of the author at the time of the report.
    item_name text not null default '',
    reason text not null check (reason in ('spam', 'offensive', 'copyright', 'other')),
    comment text not null default '',
    status text not null default 'open' check (status in ('open', 'resolved', 'dismissed')),
    created_at timestamptz not null,
    closed_at timestamptz,
    -- The administrator who closed the report. It is null after the deletion of the account of the administrator.
    closed_by uuid
);

-- One open report for each user and item.
create unique index moderation_reports_open_idx on users.moderation_reports (reporter_id, item_type, item_id) where status = 'open';
create index moderation_reports_status_idx on users.moderation_reports (status, created_at);
create index moderation_reports_reporter_idx on users.moderation_reports (reporter_id, created_at);
create index moderation_reports_item_idx on users.moderation_reports (item_type, item_id);
create index moderation_reports_owner_idx on users.moderation_reports (item_owner_id);

-- The decks and the author profiles that an administrator hid.
create table users.moderation_hidden (
    item_type text not null check (item_type in ('deck', 'author')),
    item_id uuid not null,
    item_owner_id uuid not null,
    item_name text not null default '',
    hidden_at timestamptz not null,
    -- The administrator who hid the item. It is null after the deletion of the account of the administrator.
    hidden_by uuid,
    reason text not null default '',
    primary key (item_type, item_id)
);

create index moderation_hidden_owner_idx on users.moderation_hidden (item_owner_id);

-- The actions of the administrators. The deletion of an account removes the IDs of the account from the rows, and keeps the rows.
create table users.moderation_log (
    id uuid primary key,
    created_at timestamptz not null,
    moderator_id uuid,
    action text not null check (action in ('hide', 'show', 'resolve', 'dismiss')),
    item_type text not null check (item_type in ('deck', 'author')),
    item_id uuid,
    item_owner_id uuid,
    item_name text not null default '',
    report_id uuid,
    reason text not null default ''
);

create index moderation_log_created_idx on users.moderation_log (created_at desc, id desc);
create index moderation_log_moderator_idx on users.moderation_log (moderator_id);
create index moderation_log_owner_idx on users.moderation_log (item_owner_id);
