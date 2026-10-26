# 0010. Two extension levels: plugins and modules

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

One goal of Reliquary is to let people add integrations and information without a change to the core.
The core contains all the features that users expect from a tool like Moxfield.
Extensions add the rest.
The extensions have two kinds of needs:

- Many small extensions show more information, automate an action or link an external account.
  Anyone must be able to write them.
- A few advanced features need code on the server, their own data, scheduled tasks, or actions while the user is not connected.

## Decision

Reliquary has two levels of extensions: plugins and modules.

| Aspect | Plugin | Module |
| --- | --- | --- |
| Where it runs | Only in the browser, in a sandbox | In the front end and on the server |
| Who can write it | Anyone | The internal team or selected partners |
| Who installs it | Each user, for their own account | The instance administrator |
| Access to data | The data of the user, through the public API, with the permissions that the user gives | Access on the server, with its own schema `mod_<id>` |
| Trust | The plugin catalog lists the plugins that the project reviewed and signed | Each module has a signature |
| Milestone | M4 | After M4 |

- The core never runs plugin code on the server.
  Only signed modules that the instance administrator installs run on the server.
- A module communicates with the core through a versioned internal API and domain events.
- The preferred form of a module is a separate service in a container.
  Authors can write a module in C# or in TypeScript.
  A module of the internal team with a close link to the core can be a .NET library instead.
- The core prepares for modules from milestone M0.
  The [extensions](../architecture/extensions.md) file lists the seven items of this work.

## Consequences

- Anyone can extend Reliquary without access to the server.
- A plugin acts only when the web interface is open.
  A feature that must act while the user is not connected needs a module.
- The project must review and sign the plugins of the plugin catalog, and revoke compromised plugin versions.
- The core must deliver domain events, an internal API, slots, delegation tokens and a register of extensions before modules exist.
