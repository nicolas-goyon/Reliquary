-- The consent of the user for each plugin: the plugin version and the lists that the user accepted.
-- A plugin runs only when these lists contain all the requests of its installed version (issue #287).
-- A row without accepted_version has no consent, so a new version with requests stays paused.
alter table content.plugin_activations
    add column accepted_version text,
    add column accepted_scopes text[] not null default '{}',
    add column accepted_network text[] not null default '{}',
    add column accepted_devices text[] not null default '{}',
    add column accepted_sites text[] not null default '{}',
    add column accepted_at timestamptz;
