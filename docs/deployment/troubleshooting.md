# Troubleshooting

This page gives the usual problems of a deployed instance, and their solutions.
Terms follow the [glossary](../glossary.md).

## Read the logs

In the folder of the instance:

```shell
docker compose --project-name reliquary-preprod logs --tail 200 api worker web
```

Replace `reliquary-preprod` with the value of `RELIQUARY_PROJECT` in `.env`.

## Problems and solutions

| Message or symptom | Cause | Solution |
| --- | --- | --- |
| `Cannot pull ghcr.io/...: denied` | The version does not exist, or the image is private. | Check the version on the [packages page](https://github.com/nicolas-goyon?tab=packages&repo_name=reliquary). |
| `has no Compose file. Use a version that is more recent.` | The image is older than the script `reliquary-update`. | Use a more recent version. |
| `The Compose file of X does not work with .env` | The new version needs a setting that `.env` does not contain. | Read the release notes, add the setting to `.env`, then run the update again. |
| `An other run of reliquary-update uses the instance.` | A different run is active, or a run stopped with no clean exit. | When no other run exists, delete the folder `.state/lock`. |
| `An earlier update did not end.` | The recovery of a failed update also failed. | Correct the cause (for example, start Docker), then run `./reliquary-update --recover`. |
| `Bind for 127.0.0.1:8090 failed: port is already allocated` | A different program uses the port. | Change `RELIQUARY_APP_PORT` (and `RELIQUARY_PUBLIC_URL`) in `.env`. |
| The card search gives few results after the installation. | The worker imports the cards after its first start. The import takes some minutes. | Wait. To start the import again, run `./reliquary-update --import-cards`. |
| All the users must sign in again after an update. | The Redis volume or the signing key changed. | Do not change `RELIQUARY_SIGNING_KEY`, and do not delete the volume `<project>_redis-data`. |
| The Google sign-in fails behind the reverse proxy. | The redirect URI or the public URL is not correct. | Set `RELIQUARY_PUBLIC_URL` to the HTTPS address, and register `https://your-domain/api/v1/auth/external/google/callback` at Google. |

## Start again from a backup

When the instance does not start and the recovery does not help:

1. Run `./reliquary-update --status` to see the version and the restore points.
2. Run `./reliquary-update --rollback` to go back to the last restore point.
3. If the problem stays, open an issue on [GitHub](https://github.com/nicolas-goyon/reliquary/issues) with the logs.
