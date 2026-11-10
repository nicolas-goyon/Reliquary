# End-to-end test

The end-to-end test checks the online games in real browsers, against the real API, the table hub and Nginx.
It is not on the path of the pull requests.
The workflow `.github/workflows/e2e.yml` runs it on a schedule.
The architecture of the online games is in [Online games](../architecture/online-games.md).

## What the test does

The test is `e2e/tests/online-game.spec.ts`. It uses [Playwright](https://playwright.dev/) with Chromium.

1. The test creates a public deck for 2 demo accounts through the API.
2. Four browser contexts open the web app. The host and the second player have an account. One guest gives a deck link, and the other guest gives a deck list.
3. The host creates a game with 4 seats. The other players join with the invite link.
4. The test checks that the players use the two API replicas. Nginx sends each connection to one replica, so the test reloads a page until both replicas have a connection.
5. The host starts the game.
6. During the given duration, each player clicks a random control of its own table, about once each second. Each control of the own table is a legal action of this player.
7. Each 60 to 120 seconds, one player reloads the page, or leaves the table and goes back to it.
8. The test waits until all the pages have the same sequence number, and then compares the pages.
9. The host ends the game.

The test fails when one of these conditions is true:

- A page calls `Resync` with a reason other than `view`.
- The metric `reliquary_playtest_desyncs_total` of a replica is more than 0.
- A replica sent no patch.
- A page has a hub error, a page error or a console error.
  The status 401 of `/api/v1/auth/refresh` is a known console error.
  The web app asks for an access token before a sign-in, and a guest has no account.
- A page does not show the same table as the page of the player.
  The test compares the life points, the number of cards in the hand and in the library, and the cards of the public zones.
- A page does not show the same turn and the same active player as the other pages.

The test replaces the card images with a small local image, so it does not load images from the Scryfall servers.

## The instance

The file `e2e/compose.yaml` is an overlay of `compose.yaml`.
It starts 2 replicas of the service `api` behind the web container.
The replicas share the games in Redis, so they need no sticky sessions.
Each replica gives its metrics at `/metrics` on a random port of `127.0.0.1`.

## Schedule

| Run | Duration |
| --- | --- |
| Each night from Monday to Saturday | 10 minutes |
| Each Sunday | 60 minutes |
| Manual run ("Run workflow" in the Actions tab) | The input `minutes`, 10 by default |

When a scheduled run fails, the workflow opens an issue with the label `type: bug`.
If an open issue with the same title exists, the workflow adds a comment to that issue.
The artifact `e2e-online-games` of the run contains these files:

- The trace of the last 3 minutes of each browser.
- A screenshot of each page.
- The logs of the instance.

Open a trace with `npx playwright show-trace <file>` in the folder `e2e`, or at [trace.playwright.dev](https://trace.playwright.dev/).

## Run the test on your computer

1. Install the packages and Chromium:

   ```shell
   cd e2e
   npm ci
   npx playwright install chromium
   ```

2. Create the instance with two API replicas on <http://localhost:8080>.
   The instance uses the ports of the local instance, so stop the local instance first with `just stop`.
   The recipe waits until the worker imported the demo cards.

   ```shell
   just e2e-up
   ```

3. Run the test. The argument is the duration in minutes:

   ```shell
   just e2e 5
   ```

4. Destroy the instance:

   ```shell
   just e2e-down
   ```

The test uses these environment variables:

| Variable | Meaning |
| --- | --- |
| `E2E_BASE_URL` | The address of the web container. The default is `http://localhost:8080`. |
| `E2E_MINUTES` | The duration of the random game, in minutes. The default is 10. |
| `E2E_METRICS_URLS` | The addresses of `/metrics` of the replicas, with a comma between them. With less than 2 addresses, the test does not check the replicas and the metrics. |
| `E2E_CHROME` | The path of a Chrome executable. With no value, the test uses the Chromium of Playwright. |
| `E2E_SUMMARY` | A file for the summary of the run. The workflow adds it to the summary of the job. |
| `E2E_DEBUG` | With a value, the test writes the last patch before each `Resync` to the output. |

## Dependencies

The folder `e2e` has its own `package.json` and lock file, with only `@playwright/test`.
The dependencies of the web app do not change.
Dependabot updates the folder `e2e` each week.
