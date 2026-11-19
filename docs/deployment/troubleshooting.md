# Troubleshooting

This page gives the usual problems of a deployed instance, and their solutions.
Terms follow the [glossary](../glossary.md).

## Read the logs

In the folder of the instance:

```shell
docker compose --project-name reliquary-preprod logs --tail 200 api worker web
```

Replace `reliquary-preprod` with the value of `RELIQUARY_PROJECT` in `.env`.
The same command works in PowerShell.
On Windows, Docker Desktop also shows the logs: open **Containers**, then select a container.

## Docker does not respond

When a command gives `Cannot connect to the Docker daemon` or `docker: command not found`:

/// tab | Linux

1. Start Docker:

   ```shell
   sudo systemctl start docker
   ```

2. When the message is `permission denied`, add your user to the group `docker`, then sign out and sign in again:

   ```shell
   sudo usermod --append --groups docker "$USER"
   ```

///

/// tab | Windows

1. Start Docker Desktop, and wait until it shows **Engine running**.
2. In the Ubuntu terminal of WSL 2 only: in Docker Desktop, open **Settings → Resources → WSL integration**, and enable **Ubuntu**.
3. Close the terminal, and open it again.
4. When the problem stays, run `wsl --shutdown` in PowerShell, then start Docker Desktop again.

///

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
| In Git Bash, `cat: 'C:/Program Files/Git/app/deploy/...': No such file or directory` | Git Bash changes the paths of the container to Windows paths. | Use `reliquary-update.ps1` in PowerShell. See [Git Bash](requirements.md#git-bash). |
| In PowerShell, `reliquary-update.ps1 cannot be loaded because running scripts is disabled on this system` | Windows blocks the scripts by default. | Run `Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned`, then try again. |
| In PowerShell, `reliquary-update.ps1 is not digitally signed. You cannot run this script on the current system.` | A browser downloaded the script, and Windows marks the files from the Internet. The policy `RemoteSigned` then asks for a signature. | Run `Unblock-File .\reliquary-update.ps1`. The script then runs. |
| In PowerShell, `docker: The term 'docker' is not recognized` | Docker Desktop is not installed, or PowerShell started before the installation. | Install Docker Desktop, then open a new PowerShell window. |
| In WSL 2, `Permission denied` on the folder `backups` | The instance is in the Windows file system (`/mnt/c`). | Install the instance in the Linux file system, for example in `~/reliquary-preprod`, and move the backups there. Or use `reliquary-update.ps1` in PowerShell. |
| In WSL 2, `bash: ./reliquary-update: /bin/sh^M: bad interpreter` | A Windows program saved the script with Windows line endings. | Get the script again from the CLI image. Edit `.env` and the script in WSL or in an editor that keeps the line endings. |
| An online game shows "The connection stopped. The page connects again." again and again. | The reverse proxy does not let WebSockets through on `/api/v1/playtest/hub`, or its timeout is too short. | Configure the reverse proxy. See [WebSockets for the online games](production.md#websockets-for-the-online-games). |

## Sign-in

[Sign-in services](sign-in-services.md) tells how to set up each service.

| Problem | Cause | Solution |
| --- | --- | --- |
| The sign-in page shows "This instance has no sign-in service." | No service has its client ID and its client secret. | Put the two values of one service in the instance, then apply the change. |
| The sign-in page shows no button for one service. | One of the two values of this service is empty, or the containers did not start again. | Do a check of the two values, then run `./reliquary-update`. |
| The service shows `redirect_uri_mismatch`, "Invalid Redirect URI" or "The redirect_uri is not associated with this application". | The redirect URI at the service is not the redirect URI of the instance. | Register `RELIQUARY_PUBLIC_URL/api/v1/auth/external/<service>/callback` at the service, with no slash at the end. |
| The sign-in fails behind the reverse proxy. | The public URL is not correct, or the reverse proxy does not send the header `X-Forwarded-Proto`. | Set `RELIQUARY_PUBLIC_URL` to the HTTPS address, and configure the reverse proxy. |
| Google shows "Access blocked" or `access_denied`. | The application is not published, and the user is not a test user. | In the Google Auth Platform, select **Audience**, then **Publish app**. |
| Reliquary shows that the email address is not checked or not available. | The account at the service has no checked email address. | Check the email address at the service, then sign in again. |
| The administrator cannot open `/admin/moderation`. | The administrator email address is not the address of the account at the service. | Set `RELIQUARY_ADMINISTRATOR_EMAIL` to this address, apply the change, then sign in again. |

## Start again from a backup

When the instance does not start and the recovery does not help:

1. Run `./reliquary-update --status` to see the version and the restore points.
2. Run `./reliquary-update --rollback` to go back to the last restore point.
3. If the problem stays, open an issue on [GitHub](https://github.com/nicolas-goyon/reliquary/issues) with the logs.
