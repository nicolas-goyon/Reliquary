# 0011. Plugin network access through a declared list of domains

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

Plugins connect Reliquary to other tools, for example Moxfield, marketplaces or a scan application.
Some of these tools run on other servers.
Other tools run on the computer of the user, for example a scan application or a local tool.
Free network access would let a plugin send the data of the user to any server.
Some sites also refuse calls from a browser (CORS).

## Decision

- A plugin can call other servers and `localhost`.
- The plugin manifest declares the domains of the plugin, and the plugin can reach only these domains.
- The user accepts the domains when they install the plugin.
  Each new domain needs a new approval from the user.
- The automatic analysis of the plugin catalog rejects a plugin that calls domains that its plugin manifest does not declare.
- For the sites that refuse calls from a browser, the core offers a network relay:
  - The relay accepts only the domains of the plugin manifest.
  - The relay has quotas.
  - The relay does not log the content of the requests.

## Consequences

- A plugin can work with a local tool, for example a scan application, through `localhost`.
- A plugin can use a site that refuses calls from a browser, through the relay.
- The user knows each domain that a plugin can reach.
- The [extensions](../architecture/extensions.md) file gives all the rules for plugins.
