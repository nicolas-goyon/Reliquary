# Web app

This guide tells how the web app works on phones and as an installed application.
It also gives the keyboard shortcuts, the themes and the accessibility checks.
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

## Keyboard shortcuts

| Keys | Action |
| --- | --- |
| `/` | Go to the card search field. On a different page, open the card search first. |
| `g`, then `h` | Open the home page |
| `g`, then `c` | Open the card search |
| `g`, then `d` | Open the deck list |
| `g`, then `e` | Open the public decks |
| `n` | On the deck list: open the dialog that makes a deck |
| `a` | On the deck page: go to the field "Add a card" |
| `?` | Show the list of the shortcuts |

For a shortcut of two keys, push the second key less than 1.5 seconds after the first key.
The **Keyboard shortcuts** button at the bottom of each page also shows the list.

The shortcuts do not work in these conditions:

- The focus is in a field: an `input`, a `textarea`, a `select` or an editable element.
- The user holds Ctrl, Alt or Meta.
  The shortcuts accept Shift, because some keyboards need Shift for `/` and `?`.
- A dialog is open.

To add a shortcut, follow these steps:

1. Add the shortcut to the list `shortcuts` in `web/src/shortcuts/shortcuts.ts`.
2. To focus or select an element of a page, give the element the attribute `data-shortcut`.
3. Add the description to `shortcuts.actions` in the `common` namespace, in English and in French.
4. Add the shortcut to the table of this guide.

## Themes

The **Theme** field in the header has three values:

| Value | Effect |
| --- | --- |
| System | The colors follow the setting of the operating system. This is the default value. |
| Light | The light colors |
| Dark | The dark colors |

The web app sets the attribute `data-theme` on the root element for the values "Light" and "Dark".
The CSS tokens in `web/src/styles.css` read this attribute.

The browser keeps the choice in its local storage, under the key `reliquary.theme`.
The account API has no field for the theme, so the choice applies only to one browser.
When the local storage is not available, the choice applies until the page closes.

## Accessibility

The web app follows the level AA of the Web Content Accessibility Guidelines (WCAG) 2.2.
Three automatic tests check the main rules:

| Test | What it checks |
| --- | --- |
| `web/src/a11y/accessibility.test.tsx` | The [axe](https://github.com/dequelabs/axe-core) rules on the main pages. The test fails for each problem with the impact "serious" or "critical". |
| `web/src/theme/contrast.test.ts` | The contrast of the color tokens in the two themes. Text needs 4.5:1 or more. The borders of controls, the focus indicator and the chart bars need 3:1 or more. |
| `web/src/theme/theme.test.tsx` | The dark tokens of the "Dark" value and of the "System" value are the same. |

The axe test covers these pages: home, sign-in, registration, card search, card, public decks, deck list, deck and account.
jsdom has no layout, so axe cannot check the contrast in the tests.
The contrast test reads the tokens from `web/src/styles.css` instead.

The web app also applies these rules:

- Each page has a level 1 heading. The title of the document is the heading, followed by "Reliquary".
- After a page change, the focus goes to the main content, so a screen reader reads the new page.
- The "Go to the content" link is the first element of each page.
- Each element that the keyboard can reach shows a visible focus indicator.
- Each field has a label. A field without a visible label has a label for screen readers.
- A modal dialog keeps the focus inside. Escape closes it, and the focus goes back to the element that opened it.

To add a page, add a test for the page to `web/src/a11y/accessibility.test.tsx`.
To add a color token, give it a value in the light block and in the two dark blocks of `web/src/styles.css`.
If the token colors text or a control, add it to `web/src/theme/contrast.test.ts`.
