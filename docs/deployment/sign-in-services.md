# Sign-in services

A user signs in to Reliquary with an account of Google, GitHub or Discord.
Reliquary keeps no password and sends no email.
This page tells how to connect an instance to a sign-in service.
Terms follow the [glossary](../glossary.md).
[ADR 0054](../decisions/0054-sign-in-with-oauth-providers-only.md) gives the reasons for the design.

## Select a way to sign in

| Instance | Way to sign in | Time |
| --- | --- | --- |
| [Local instance](local-development.md) | The development sign-in. It is on by default. | None |
| [Pre-production instance](pre-production.md) that stays on your computer | The [development sign-in](#development-sign-in) | 1 minute |
| Instance that other persons can reach | One sign-in service or more | 5 to 10 minutes for each service |

One service is sufficient. GitHub and Discord are the quickest to set up.
You can add a service later. An account keeps its data when its user signs in with a second service.
Reliquary uses the email address to find the account, so the two services must give the same checked address.

## Before you start

You need two values:

- The public URL of the instance. This is the value of `RELIQUARY_PUBLIC_URL`, for example `https://reliquary.example.org` or `http://localhost:8090`.
- The redirect URI of the service. It is the public URL, then `/api/v1/auth/external/<service>/callback`.

| Service | Redirect URI for `https://reliquary.example.org` |
| --- | --- |
| Google | `https://reliquary.example.org/api/v1/auth/external/google/callback` |
| GitHub | `https://reliquary.example.org/api/v1/auth/external/github/callback` |
| Discord | `https://reliquary.example.org/api/v1/auth/external/discord/callback` |

The redirect URI must be the same at the service and in the instance, letter for letter.
Do not add a slash at the end. Use `http` only with `localhost`.

Each service gives a client ID and a client secret. The client ID is public. Keep the client secret secret.

## Google

Google accepts a redirect URI with `http` only for `localhost`. A different host must use HTTPS and a domain name, not an IP address.

1. Open the [Google Auth Platform](https://console.cloud.google.com/auth/clients). Select a project, or make one.
2. If Google asks for it, configure the application:
   1. Give the name of the application and your email address.
   2. Select the audience **External**.
   3. Accept the policy, and create the configuration.
3. Select **Clients**, then **Create client**.
4. Select the application type **Web application**, and give a name.
5. In **Authorized redirect URIs**, add the redirect URI of Google.
6. Select **Create**.
7. Copy the client ID and the client secret. Google shows the client secret only at this time.
8. Select **Audience**, then **Publish app**.

Until you publish the application, only the test users of the page **Audience** can sign in.
Reliquary reads only the name and the email address, so Google asks for no verification of the application.

## GitHub

1. Open [OAuth Apps](https://github.com/settings/developers) in the developer settings of GitHub.
2. Select **New OAuth App**.
3. Give a name. In **Homepage URL**, give the public URL of the instance.
4. In **Authorization callback URL**, give the redirect URI of GitHub.
5. Select **Register application**.
6. Copy the client ID.
7. Select **Generate a new client secret**, and copy the secret. GitHub shows it only at this time.

A GitHub OAuth App has only one callback URL. Make one OAuth App for each instance.
A user must have a checked primary email address in the GitHub account.

## Discord

1. Open the [Discord Developer Portal](https://discord.com/developers/applications).
2. Select **New Application**, give a name, and create the application.
3. Select **OAuth2** in the menu.
4. In **Redirects**, select **Add Redirect**, and give the redirect URI of Discord.
5. Select **Save Changes**.
6. Copy the client ID.
7. Select **Reset Secret**, and copy the client secret. Discord shows it only at this time.

The application needs no bot. A user must have a checked email address in the Discord account.

## Put the values in the instance

/// tab | Docker Compose

1. Open the `.env` file of the instance.
2. Set the two values of each service that you use:

   | Service | Settings |
   | --- | --- |
   | Google | `RELIQUARY_GOOGLE_CLIENT_ID` and `RELIQUARY_GOOGLE_CLIENT_SECRET` |
   | GitHub | `RELIQUARY_GITHUB_CLIENT_ID` and `RELIQUARY_GITHUB_CLIENT_SECRET` |
   | Discord | `RELIQUARY_DISCORD_CLIENT_ID` and `RELIQUARY_DISCORD_CLIENT_SECRET` |

3. Set `RELIQUARY_ADMINISTRATOR_EMAIL` to the email address of your account at the service.
4. Make sure that `RELIQUARY_PUBLIC_URL` is the address that the users open.
5. Apply the change:

   ```shell
   ./reliquary-update
   ```

///

/// tab | Kubernetes

1. Add the client secret to the Secret of the instance. Use the key of the service:

   | Service | Key of the Secret | Value of the chart |
   | --- | --- | --- |
   | Google | `Auth__Providers__Google__ClientSecret` | `config.google.clientId` |
   | GitHub | `Auth__Providers__GitHub__ClientSecret` | `config.github.clientId` |
   | Discord | `Auth__Providers__Discord__ClientSecret` | `config.discord.clientId` |

2. Put the client ID in the value of the chart.
3. Put the email address of your account at the service in `config.administratorEmails`.
4. Run `helm upgrade` with the values file.

[Production instance on Kubernetes](production-kubernetes.md) gives the full commands.

///

## Do a check of the sign-in

1. Open the sign-in page of the instance. It shows one button for each service that has its two values.
2. Select a button, and accept the request of the service.
3. Make sure that Reliquary opens the web app with your account.
4. Open `/admin/moderation`. Only the account of the administrator email address can open this page.

This command lists the services that the instance has:

```shell
curl https://reliquary.example.org/api/v1/auth/providers
```

[Troubleshooting](troubleshooting.md#sign-in) gives the usual problems.

## Development sign-in

The development sign-in opens an account from its email address, with no service and no password.
Use it only when the instance stays on your computer (`RELIQUARY_BIND_ADDRESS=127.0.0.1`).

1. Set `RELIQUARY_DEVELOPMENT_SIGN_IN=true` in the `.env` file.
2. Set `RELIQUARY_ADMINISTRATOR_EMAIL` to an email address of your choice.
3. Run `./reliquary-update`.
4. Open the sign-in page. In the part **Development sign-in**, type the email address, and open the account.

Reliquary makes the account at the first sign-in.

**Warning:** With this setting, each person who can open the instance can open each account.
Do not use it with `RELIQUARY_BIND_ADDRESS=0.0.0.0` or behind a reverse proxy.

## Change or remove a service

- To replace a client secret, make a new secret at the service, put it in the instance, and apply the change.
- To remove a service, make its two values empty, and apply the change.
  An account that used only this service cannot sign in until its user signs in with a different service that gives the same email address.
- To change the public URL, change the redirect URI at each service at the same time.
