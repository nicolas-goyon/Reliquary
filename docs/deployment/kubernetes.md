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
Do the backup before the migrations, with `reliquary backup` and a persistent volume.

The Helm chart has a Job `migrate` for this step:

- Helm runs the Job before each install and each upgrade, as a `pre-install` and `pre-upgrade` hook.
- The Job does not try again (`backoffLimit: 0`). When it fails, the upgrade stops and the old version continues to run.
- With `migrate.backup.enabled: true`, the Job runs `reliquary backup` before the migrations of an upgrade.
  The backup goes to the PersistentVolumeClaim of `migrate.backup.existingClaim`, in a folder `<UTC date>-before-revision-<revision>`.
- `helm rollback` does not revert the migrations. To go back to the old version, restore the backup.

The old pods continue to run during the migrations.
The [migration rules](versions-and-updates.md#migration-rules) make this possible: a migration only adds, and a deletion comes one release after the code change.

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
| Job `migrate` | The migrations with the CLI image, before each install and each upgrade. See [Migrations](#migrations). |
| Secret | The secret settings, when `secrets.existingSecret` is empty |
| HTTPRoute or Ingress | The optional route of the site to the `web` Service. See [Route of the site](#route-of-the-site). |
| StatefulSet and Service `redis` | An optional Redis server with a persistent volume, when `redis.enabled` is `true` |

The chart has no subcharts. PostgreSQL is always external.

`values.yaml` gives all the values, with a comment for each value.
These values are necessary:

- `config.publicUrl`: the public address of the site.
- `secrets.existingSecret`: the name of a Secret with the keys `Auth__SigningKey`, `ConnectionStrings__Users` and `ConnectionStrings__Redis`.
  The containers read all the keys of the Secret as environment variables.

To check a change of the chart, run `just helm-check`.

## Route of the site

The chart sends all the paths of the site to the `web` Service.
The web app sends `/api` to the API, so the web app and the API stay on the same site.
The chart has two options:

| Option | Values | Use |
| --- | --- | --- |
| HTTPRoute of the Gateway API | `httpRoute.enabled`, `httpRoute.parentRefs`, `httpRoute.hostnames` | The first choice. The cluster needs a Gateway API implementation and a Gateway. |
| Ingress | `ingress.enabled`, `ingress.className`, `ingress.hosts`, `ingress.tls` | A cluster with an Ingress controller and no Gateway API |

The chart adds no annotation of one controller.
Put the annotations of your controller in `httpRoute.annotations` or `ingress.annotations`.

### WebSockets

The table hub of the online games uses a WebSocket on `/api/v1/playtest/hub`.
A WebSocket stays open for the length of a game.
Check these items on your Gateway or your Ingress controller:

- The WebSocket upgrade must pass. Most Gateway API implementations and Ingress controllers let it pass with no setting.
- The idle timeout must be 60 seconds or more. The client sends a ping each 15 seconds.
- A timeout on the full duration of a request must be off, or longer than a game.
  For an HTTPRoute, set `httpRoute.timeouts.request` to `"0s"` when your Gateway supports this field.
  For an Ingress, use the annotation of your controller.
- The web container keeps a WebSocket open for 1 hour with no message, so a longer timeout has no effect.

The hub needs no sticky sessions. The API replicas share the messages of the games through Redis.
