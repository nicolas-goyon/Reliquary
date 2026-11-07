# 0023. Extension registry and core API version

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

[ADR 0010](0010-plugins-and-modules.md) defines two levels of extensions: plugins and modules.
Item 7 of the preparation for modules asks for a registry of the installed extensions.
Each extension has an activation flag and a minimum core version.
Extensions do not run before milestone M4, but the core must know them from milestone M0.

The data model gives one installation of a plugin for each user, and one installation of a module for each instance.
A plugin from the plugin catalog is the same code for all the users, so the instance needs one record of each plugin.
Each user then decides if the plugin runs for them.

The registry is a part of the platform, not a core component: it serves all the core components and the hosts.

## Decision

- The table `users.extensions` contains one row for each installed extension, plugin or module.
  The row keeps the manifest, the permissions, the slots, the settings and the activation flag of the instance.
- The table `content.plugin_activations` contains the activation of each plugin for each user, in the shard of the user.
- Only an administrator manages the registry, with the endpoints `/api/v1/admin/extensions`.
- The core API has a version in the Semantic Versioning format, in `CoreApi.Version`. The first version is 1.0.0.
  A manifest declares the supported versions in the field `coreApi`, with the range syntax of npm.
- The API refuses to install or enable an extension that does not support the current version.
- `GET /api/v1/extensions` lists the enabled extensions for all the clients. The web interface uses it to load the contributions to the slots.

## Consequences

- The registry exists before the plugin runtime. Milestone M4 adds the installation of plugins from the catalog and the loading of their code.
- An update of the core that changes the major version of the core API stops the incompatible extensions until the administrator installs new versions.
- The deletion of an extension leaves the activations of the users in the shards. They have no effect, and a new installation of the same ID uses them again.
