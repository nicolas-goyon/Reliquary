# Production instance on Kubernetes

This page tells how to install and update a production instance with the Helm chart.
[Requirements for Kubernetes](kubernetes.md) gives the reference of the chart and of the images.
[ADR 0053](../decisions/0053-helm-chart-scope.md) gives the scope of the chart.
Terms follow the [glossary](../glossary.md).

## Requirements

- A Kubernetes cluster, version 1.27 or later, and `kubectl`.
- Helm 3.
- A Gateway API implementation with a Gateway and a certificate for your domain.
  An Ingress controller is the second choice.
- PostgreSQL 18. This page uses [CloudNativePG](https://cloudnative-pg.io/).
- Redis with a persistent volume.
- An email server (SMTP) for the account emails.
- A StorageClass for the volume of the backups.

The chart installs the API, the worker, the web app and the migration Job.
It does not install PostgreSQL. It installs Redis only when you set `redis.enabled`.

The release workflow does not publish the chart yet ([#312](https://github.com/nicolas-goyon/reliquary/issues/312)).
Until then, install the chart from a clone of the repository, at the tag of the release.

## Prepare the namespace

Make the namespace of the instance:

```shell
kubectl create namespace reliquary
```

The commands of this page use the namespace `reliquary` and the release name `reliquary`.

## Prepare PostgreSQL with CloudNativePG

1. Install the operator. Follow the [installation guide of CloudNativePG](https://cloudnative-pg.io/documentation/current/installation_upgrade/), or use its Helm chart:

   ```shell
   helm repo add cnpg https://cloudnative-pg.github.io/charts
   helm upgrade --install cnpg cnpg/cloudnative-pg --namespace cnpg-system --create-namespace
   ```

2. Make the file `reliquary-db.yaml`:

   ```yaml
   apiVersion: postgresql.cnpg.io/v1
   kind: Cluster
   metadata:
     name: reliquary-db
     namespace: reliquary
   spec:
     instances: 2
     imageName: ghcr.io/cloudnative-pg/postgresql:18
     storage:
       size: 20Gi
     bootstrap:
       initdb:
         database: reliquary
         owner: reliquary
   ```

3. Apply the file, and wait until the cluster is ready:

   ```shell
   kubectl apply --filename reliquary-db.yaml
   kubectl --namespace reliquary wait cluster/reliquary-db --for=condition=Ready --timeout=10m
   ```

4. Get the password of the user `reliquary`:

   ```shell
   kubectl --namespace reliquary get secret reliquary-db-app --output jsonpath='{.data.password}' | base64 --decode
   ```

The Service `reliquary-db-rw` sends the connections to the primary instance.
The connection string is `Host=reliquary-db-rw;Port=5432;Database=reliquary;Username=reliquary;Password=<password>`.

Keep the major version 18. The CLI image contains the client tools of PostgreSQL 18 for the backups, and their major version must be the version of the server.

The migrations make the extension `pg_trgm`. The owner of the database can make it, because `pg_trgm` is a trusted extension.

Configure the backups of CloudNativePG too, to an object store on a different site.
The [backup guide of CloudNativePG](https://cloudnative-pg.io/documentation/current/backup/) tells how.

## Prepare Redis

Redis keeps these data:

- The keys that protect the cookies. When Redis loses them, all the users must sign in again.
- The state of the online games. When Redis loses it, the online games stop.

Select one option:

| Option | Values | Use |
| --- | --- | --- |
| The Redis server of the chart | `redis.enabled: true`, `redis.persistence.size` | A small cluster. One replica, with the append-only file on a persistent volume. |
| An external Redis server | `ConnectionStrings__Redis` in the Secret | A managed Redis server, or a Redis server of your cluster with persistence |

For an external Redis server, the connection string has the format `host:6379,password=<password>`.
For a server with TLS, add `,ssl=true`.

## Make the Secret

Make a Secret with the secret settings. The chart does not make it, and Helm never shows its values.

1. Make a signing key:

   ```shell
   openssl rand -base64 32
   ```

2. Make the Secret:

   ```shell
   kubectl --namespace reliquary create secret generic reliquary-secrets \
     --from-literal=Auth__SigningKey='<signing key>' \
     --from-literal=ConnectionStrings__Users='Host=reliquary-db-rw;Port=5432;Database=reliquary;Username=reliquary;Password=<password>' \
     --from-literal=ConnectionStrings__Redis='<redis connection string>' \
     --from-literal=Email__Smtp__Password='<SMTP password>'
   ```

   With the Redis server of the chart, do not add `ConnectionStrings__Redis`. The chart gives the address of its Redis server.

3. For the Google sign-in, add the key `Auth__Providers__Google__ClientSecret`.

The containers read all the keys of the Secret as environment variables.
To add a setting later, add a key to the Secret, then restart the pods:

```shell
kubectl --namespace reliquary rollout restart deployment reliquary-api reliquary-worker
```

Keep a copy of the Secret in a safe place, out of the cluster.
A backup is not usable without the signing key and the passwords.

### Vault keys

The vault of the linked accounts encrypts the secrets of the plugins with the keys of the setting `Plugins:VaultKeys` ([ADR 0050](../decisions/0050-no-plugin-code-on-the-server.md)).
Each key has a key ID.

1. Make a key:

   ```shell
   openssl rand -base64 32
   ```

2. Add the key to the Secret with the name `Plugins__VaultKeys__<key ID>`, for example `Plugins__VaultKeys__2026-11`.

To change the key, add a new key with a new key ID.
Do not delete the old keys. The vault needs them to read the old secrets.
When you lose a key, the secrets that it encrypted are lost. The users must link their accounts again.

## Write the values

Make the file `values-production.yaml`:

```yaml
image:
  tag: "X.Y.Z"
config:
  publicUrl: https://reliquary.example.org
  administratorEmails:
    - you@example.org
  email:
    from: Reliquary <noreply@example.org>
    smtp:
      host: smtp.example.org
      port: 587
      security: StartTls
      userName: reliquary
secrets:
  existingSecret: reliquary-secrets
httpRoute:
  enabled: true
  parentRefs:
    - name: public
      namespace: gateway
      sectionName: https
  hostnames:
    - reliquary.example.org
migrate:
  backup:
    enabled: true
    existingClaim: reliquary-backups
```

Replace `X.Y.Z` with the number of the release.
Do not use the tags `latest` or `develop`. A release number never changes, so each update is a decision.

`deploy/helm/reliquary/values.yaml` gives all the values, with a comment for each value.
[Route of the site](kubernetes.md#route-of-the-site) tells how to use an Ingress, and which timeouts the WebSocket of the online games needs.

## Prepare the volume of the backups

The migration Job makes a backup before the migrations of each upgrade.
Make a PersistentVolumeClaim for the backups:

```yaml
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: reliquary-backups
  namespace: reliquary
spec:
  accessModes: ["ReadWriteOnce"]
  resources:
    requests:
      storage: 20Gi
```

Apply it with `kubectl apply --filename`.
The chart deletes no backup. Delete the old backups yourself, and copy the backups to a different site.

## Install

1. Get the chart of the release:

   ```shell
   git clone --depth 1 --branch vX.Y.Z https://github.com/nicolas-goyon/reliquary.git reliquary-X.Y.Z
   ```

2. Install the chart:

   ```shell
   helm install reliquary reliquary-X.Y.Z/deploy/helm/reliquary --namespace reliquary --values values-production.yaml --wait --timeout 15m
   ```

   Helm runs the migration Job first. Then it starts the API, the worker and the web app.

3. Check the version:

   ```shell
   kubectl --namespace reliquary port-forward service/reliquary-web 8080:8080
   curl http://localhost:8080/api/v1/status
   ```

4. Open `https://reliquary.example.org`, make the account of the administrator email address, and open the link of the email.

The worker imports the card data from Scryfall at its first start. The import takes some minutes.

## Update

1. Read the release notes on the [releases page](https://github.com/nicolas-goyon/reliquary/releases).
2. Check that the volume of the backups has space for one more backup.
3. Get the chart of the new release:

   ```shell
   git clone --depth 1 --branch vX.Y.Z https://github.com/nicolas-goyon/reliquary.git reliquary-X.Y.Z
   ```

4. Change `image.tag` in `values-production.yaml` to the new release number.
5. At a time with few users, upgrade:

   ```shell
   helm upgrade reliquary reliquary-X.Y.Z/deploy/helm/reliquary --namespace reliquary --values values-production.yaml --wait --timeout 15m
   ```

6. Check the version with `/api/v1/status`, or in the footer of the web app.

The upgrade does these steps:

1. The migration Job makes a backup, then it applies the migrations of the new release.
2. Helm replaces the pods of the API and of the web app one at a time. The site stays available.
3. Helm stops the old worker, then it starts the new worker.

The old pods continue to run during the migrations.
The [migration rules](versions-and-updates.md#migration-rules) make this possible.

When the migration Job fails, the upgrade stops and the old version continues to run.
Read the logs of the Job:

```shell
kubectl --namespace reliquary logs job/reliquary-migrate --all-containers
```

## Roll back

`helm rollback` does not revert the migrations.
The old version can fail on the database of the new version.
To go back to the old version, restore the backup of the upgrade, then roll back.

1. Find the name of the backup in the logs of the migration Job. The name has the format `<UTC date>-before-revision-<revision>`.
2. Stop the API and the worker:

   ```shell
   kubectl --namespace reliquary scale deployment reliquary-api reliquary-worker --replicas 0
   ```

3. Make the file `restore.yaml`. Replace `<old version>` with the number of the old release, and `<backup>` with the name of the backup:

   ```yaml
   apiVersion: batch/v1
   kind: Job
   metadata:
     name: reliquary-restore
     namespace: reliquary
   spec:
     backoffLimit: 0
     template:
       spec:
         restartPolicy: Never
         securityContext:
           runAsNonRoot: true
           fsGroup: 1654
         containers:
           - name: restore
             image: ghcr.io/nicolas-goyon/reliquary-cli:<old version>
             args: ["restore", "/backups/<backup>", "--replace"]
             envFrom:
               - secretRef:
                   name: reliquary-secrets
             securityContext:
               allowPrivilegeEscalation: false
               readOnlyRootFilesystem: true
             volumeMounts:
               - name: backups
                 mountPath: /backups
               - name: tmp
                 mountPath: /tmp
         volumes:
           - name: backups
             persistentVolumeClaim:
               claimName: reliquary-backups
           - name: tmp
             emptyDir: {}
   ```

4. Run the restore, and wait until it ends:

   ```shell
   kubectl apply --filename restore.yaml
   kubectl --namespace reliquary wait job/reliquary-restore --for=condition=Complete --timeout=30m
   ```

5. Roll back to the revision before the upgrade:

   ```shell
   helm rollback reliquary --namespace reliquary --wait --timeout 15m
   ```

   The rollback starts the API and the worker again with the old images.

6. Delete the Job of the restore:

   ```shell
   kubectl --namespace reliquary delete job reliquary-restore
   ```

The data that the users changed after the upgrade is lost.
To keep it, make a backup before the restore. [Backup and restore](../development/backup-and-restore.md) tells how.

## Uninstall

```shell
helm uninstall reliquary --namespace reliquary
```

The command keeps the volume of the backups, the Secret `reliquary-secrets` and the PostgreSQL cluster.
With `redis.enabled`, it also keeps the volume of Redis.
Delete them with `kubectl` when you do not need the data.

## Security

- Give access to the Secret `reliquary-secrets` and to the volume of the backups only to the administrators.
- Do not open PostgreSQL and Redis outside the cluster.
- Update to each new release that fixes a security problem. [Security policy](https://github.com/nicolas-goyon/reliquary/blob/develop/SECURITY.md) tells how to report a problem.
