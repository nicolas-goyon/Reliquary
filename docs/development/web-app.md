# Web app

This guide tells how the web app works on phones and as an installed application.
The code is in the `web` folder.
Terms follow the [glossary](../glossary.md).

## Installable web app

The web app is a progressive web app (PWA).
A user can install it on a phone or a computer from the browser menu.
The installed app opens in its own window, without the browser bars.

The installation needs these files:

| File | Content |
| --- | --- |
| `web/public/manifest.webmanifest` | The web app manifest: the name, the colors, the start page and the icons |
| `web/public/icons/` | The icons: 192 px, 512 px, a maskable 512 px icon, an Apple touch icon and the SVG sources |
| `web/src/pwa/service-worker.js` | The source of the service worker |
| `web/vite.service-worker.ts` | The build step that writes the service worker to `dist/sw.js` |
| `web/src/pwa/registration.ts` | The registration of the service worker and the update flow |

The browser installs the web app only on a secure origin: HTTPS, or `localhost`.

### Icons

The script `web/scripts/generate-icons.mjs` draws the icons from a list of simple shapes.
It writes the SVG files and the PNG files, and it uses no external library.
To change the icons, change the shapes in the script, then run this command in the `web` folder:

```sh
npm run icons
```

The mark of a maskable icon must stay in a circle with a radius of 40 % of the icon size.
The platform can cut the area outside this circle.

### Service worker

The service worker keeps the application shell in a cache, so the web app opens without a network.
The application shell is `index.html`, the JavaScript and CSS files, the manifest and the icons.

The service worker applies these rules:

- It never answers a request to `/api/`. The API data always comes from the network, and the service worker never puts an API response in a cache.
- It answers only `GET` requests to the same origin.
- For a page, it asks the network first.
  When the network is not available, it gives the cached `index.html`.
- For a file of the application shell, it uses the cache first.
  The names of the build files contain a hash, so a cached file never becomes old.

Each build gives a new cache version.
When the new service worker starts, it deletes the caches of the old versions.

The development server (`npm run dev`) does not register the service worker.
To test the service worker, run `npm run build` and `npm run preview`.

### Update flow

1. The browser checks `/sw.js` when a page opens, and the web app asks for a check each hour.
2. When the file changed, the browser installs the new service worker. The new worker waits.
3. The web app shows the message "A new version of Reliquary is available." with an **Update** button.
4. The user selects **Update**. The waiting worker starts and takes control.
5. The web app loads the page again with the new version.

The web app loads the page again only after the approval of the user.
Thus the user does not lose the data of a form that is not saved.

### Server settings

The Nginx configuration of the web image (`web/nginx.conf`) applies these settings:

| Path | `Cache-Control` | Note |
| --- | --- | --- |
| `/sw.js` | `no-cache` | The browser must check the service worker at each visit. A missing file gives the status 404. |
| `/manifest.webmanifest` | `no-cache` | The content type is `application/manifest+json`. |
| `/assets/` | `public, max-age=31536000, immutable` | The file names contain a hash. |
| Other paths | `no-cache` | `index.html`, the icons and the pages of the router |

A different reverse proxy must apply the same settings.
A long cache time on `/sw.js` stops the updates.

## Small screens

The pages work on screens with a width of 320 px or more.
Below 600 px, the layout changes:

- The page does not scroll horizontally. A wide table scrolls in its own container.
- The forms and the field that adds a card use one column.
- The deck list hides the tags and the date of the last change.

On small screens and on touch screens, each button, link of the navigation, field and check box is at least 44 px high.
