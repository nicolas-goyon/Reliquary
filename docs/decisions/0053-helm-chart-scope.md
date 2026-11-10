# 0053. Scope of the Helm chart

- Status: Accepted
- Date: 2026-11-10
- Deciders: @nicolas-goyon

## Context

Epic [#64](https://github.com/nicolas-goyon/reliquary/issues/64) asks for a Helm chart, API replicas and workers that scale separately.
It also asks for the move of users to another database while the instance runs.
The images already work on Kubernetes ([ADR 0042](0042-deployed-instances-and-updates.md), [Kubernetes requirements](../deployment/kubernetes.md)).

Facts that limit the options:

- The refresh cookie has the attribute `SameSite=Strict`, so the web app and the API must be on the same site.
  The Nginx server of the web image already sends `/api` to the API.
- All API replicas share Redis, and the hub of online games needs no sticky sessions.
- The project of ingress-nginx stopped in March 2026.
- Bitnami stopped its free versioned images in August 2025.

## Decision

- The chart is in the main repository, in `deploy/helm/reliquary`.
  The release workflow publishes it as an OCI chart to GHCR, with the version of the application.
- The chart contains:
  - Deployments for `api` (2 replicas), `worker` (1 replica) and `web` (2 replicas)
  - A Service for each Deployment
  - A Job `migrate` with the CLI image, as a Helm hook `pre-install` and `pre-upgrade`, with `backoffLimit: 0`
  - An optional `HTTPRoute` of the Gateway API, or an optional `Ingress`, to the `web` Service only
  - A Secret for the signing key, the connection strings, the SMTP password and the vault keys, or the name of an existing Secret
- The worker keeps 1 replica until a test proves the outbox publisher with 2 replicas.
- PostgreSQL and Redis are external by default.
  The chart has an optional Redis StatefulSet with 1 replica for small clusters.
  The documentation gives CloudNativePG for PostgreSQL.
- The chart uses no subcharts and no annotations of one controller.
- The CI runs `helm lint`, `helm template` and `kubeconform` in a container.
  A scheduled job runs a smoke test on a kind cluster.
- The move of users to another database while the instance runs is not in M4.
  It moves to its own epic in the milestone Later.

Rejected alternatives:

- Bitnami subcharts for PostgreSQL and Redis: no free versioned images.
- An Ingress with ingress-nginx annotations: the project stopped.
- A separate repository for the chart: the chart follows the images and the settings of each release.

## Consequences

- `helm rollback` does not revert the migrations. The rule of ADR 0042 applies: restore the backup.
- The operator gives PostgreSQL and Redis. Redis needs a persistent volume for the keys of the cookies.
- The backup before the migration needs a persistent volume. It is a value of the chart, off by default.

## Sources

- [Kubernetes: Ingress NGINX retirement](https://kubernetes.io/blog/2025/11/11/ingress-nginx-retirement/)
- [Broadcom: Bitnami catalog changes](https://community.broadcom.com/tanzu/blogs/beltran-rueda-borrego/2025/08/18/how-to-prepare-for-the-bitnami-changes-coming-soon)
- [Gateway API](https://gateway-api.sigs.k8s.io/)
