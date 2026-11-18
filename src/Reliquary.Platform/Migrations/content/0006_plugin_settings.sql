-- The settings of the user for each plugin (issue #278): the position of the plugin, the hidden contributions,
-- and the time when the core deactivated a plugin that failed.
alter table content.plugin_activations
    add column settings jsonb not null default '{}';
