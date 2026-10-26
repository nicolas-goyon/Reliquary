# 0002. License: AGPL-3.0 for the core

- Status: Accepted
- Date: 2026-10-26
- Deciders: @nicolas-goyon

## Context

Reliquary is open source, and the project must choose a license for the core.
The specification compares three licenses:

| Criterion | MIT | Apache 2.0 | AGPL-3.0 |
| --- | --- | --- | --- |
| Use, changes and commercial use | Yes | Yes | Yes |
| A fork can become closed source | Yes | Yes | No: the derived code stays under AGPL |
| A changed web service must publish its code | No | No | Yes, for all users of the online service |
| Protection against patent lawsuits | No | Yes (explicit patent license) | Yes |
| Obstacle to adoption by companies | None | None | Real: many companies avoid it |

## Decision

- The core uses the GNU Affero General Public License v3.0 (AGPL-3.0).
  The [LICENSE](../../LICENSE) file contains the text of the license.
- The project chose AGPL-3.0 for these reasons:
  - The project stays open.
  - Other people can continue the project if its author stops.
  - No fork of the project can become closed source, also as SaaS (software as a service).
- The project can publish the plugin SDK separately under MIT or Apache 2.0, so that plugins can have their own license.
  The choice between MIT and Apache 2.0 is still open.
- If the project plans a paid offer one day, it will need a contributor license agreement (CLA).
  The CLA keeps the possibility of a dual license.

## Consequences

- Anyone can use and change Reliquary, also for commercial use.
- The code of a fork stays under AGPL-3.0.
- An operator can run a changed version of Reliquary as an online service.
  The operator must then publish the code of this version for all the users of the service.
- AGPL-3.0 gives protection against patent lawsuits.
- Many companies avoid AGPL-3.0 code.
  This is a real obstacle to the adoption of Reliquary by companies.
- Each contribution uses the AGPL-3.0 license.
- The license of the plugin SDK needs a separate decision.
