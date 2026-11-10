# Requirements for Kubernetes

This page lists what the images of Reliquary need on Kubernetes.
The Helm chart in `deploy/helm/reliquary` applies these rules.
[ADR 0053](../decisions/0053-helm-chart-scope.md) gives the scope of the chart.
Terms follow the [glossary](../glossary.md).
[ADR 0042](../decisions/0042-deployed-instances-and-updates.md) gives the reasons.

## Images

Use the images of `ghcr.io/nicolas-goyon/reliquary-<host>` with a release number, for example `0.2.0`, or with a digest.
Do not use the tags `latest` or `develop` in a production deployment.

| Image | Port | Use |
| --- | --- | --- |
| `reliquary-api` | 8080 | The public API |
| `reliquary-worker` | 8080 (health and metrics) | The outbox publisher and the scheduled jobs. Run one replica. |
| `reliquary-web` | 8080 | The web app, and a reverse proxy from `/api` to the API |
| `reliquary-cli` | — | The migrations, the backups and the restores |

## Security context

All the images run as a user that is not root. Use these settings:

- `readOnlyRootFilesystem: true`
- An `emptyDir` volume on `/tmp` for each container.
- For the web container, one more `emptyDir` volume on `/etc/nginx/conf.d`.

## Settings

The API, the worker and the CLI read their settings from environment variables.
`deploy/compose.yaml` gives the full list. Put the secrets in a Kubernetes Secret:

- `ConnectionStrings__Users` and the other connection strings
- `Auth__SigningKey`
- `Email__Smtp__Password` and the client secrets of the external sign-in

The web container needs these variables:

| Variable | Value |
| --- | --- |
| `RELIQUARY_API_URL` | The full address of the API service, for example `http://reliquary-api.reliquary.svc.cluster.local:8080`. Nginx does not use the search domains, so use the full name. |
| `RELIQUARY_DNS_RESOLVER` | Optional. Without a value, the container uses the first name server of `/etc/resolv.conf`, which is the cluster DNS service. |

The API and the web app must be on the same site, because the refresh cookie has the attribute `SameSite=Strict`.
Send all the requests of the site to the web service, or send `/api` to the API service and the other paths to the web service.

## Probes

| Container | Liveness | Readiness |
| --- | --- | --- |
| API | `GET /health/live` | `GET /health/ready` |
| Worker | `GET /health/live` | `GET /health/ready` |
| Web | `GET /` | `GET /` |

## Ingress and online games

The chart has no Ingress. Give the Service `web` your own Ingress or Gateway.

- The online games use a WebSocket on the path `/api/v1/playtest/hub`. The Ingress controller must let WebSockets through on this path.
- Set the read timeout and the send timeout of this path to 1 hour. For example, with the Ingress NGINX controller, use the annotations `nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"` and `nginx.ingress.kubernetes.io/proxy-send-timeout: "3600"`.
- The API replicas need no sticky sessions. The replicas share the games and the hub messages in Redis.

The [production guide](production.md#websockets-for-the-online-games) gives the other requirements of the reverse proxy.

## Migrations

Run `reliquary migrate` with the CLI image of the new version before the new API and the new worker start.
For example, use a Job as a Helm `pre-install` and `pre-upgrade` hook.
Do the backup before the migrations, with `reliquary backup` and a persistent volume.

## Redis

Redis keeps the keys that protect the cookies. Give Redis a persistent volume, or the users must sign in again after a restart of Redis.
Redis also keeps the online games. A restart of Redis ends the games in progress.

## Helm chart

The chart in `deploy/helm/reliquary` contains these resources:

| Resource | Content |
| --- | --- |
| Deployment and Service `api` | The public API, with 2 replicas by default (`api.replicas`) and a PodDisruptionBudget |
| Deployment and Service `worker` | The worker, with 1 replica. An update stops the old pod before the new pod starts. |
| Deployment and Service `web` | The web app, with 2 replicas by default (`web.replicas`) and a PodDisruptionBudget |
| Secret | The secret settings, when `secrets.existingSecret` is empty |
| StatefulSet and Service `redis` | An optional Redis server with a persistent volume, when `redis.enabled` is `true` |

The chart has no subcharts. PostgreSQL is always external.

`values.yaml` gives all the values, with a comment for each value.
These values are necessary:

- `config.publicUrl`: the public address of the site.
- `secrets.existingSecret`: the name of a Secret with the keys `Auth__SigningKey`, `ConnectionStrings__Users` and `ConnectionStrings__Redis`.
  The containers read all the keys of the Secret as environment variables.

To check a change of the chart, run `just helm-check`.
