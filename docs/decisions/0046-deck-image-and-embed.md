# 0046. Deck image in the browser, link previews from the read model, and an embed page

- Status: Accepted
- Date: 2026-11-09
- Deciders: @nicolas-goyon

## Context

Epic [#56](https://github.com/nicolas-goyon/reliquary/issues/56) asks for two ways to show a deck on Discord, on forums and on other sites.
The two ways are an image of the deck and an embed of the deck.
Sharing with a link is part of M1.

These facts limit the options:

- The maintainer wants few dependencies. A headless browser, ImageSharp or SkiaSharp in the API adds a large dependency, memory use and a new attack surface.
- Reliquary does not keep copies of the card images ([ADR 0012](0012-card-data-sources.md)).
  The Scryfall image servers send `Access-Control-Allow-Origin: *`, so a browser can draw the images on a canvas and read the canvas ([ADR 0038](0038-proxy-pdf-in-the-browser.md)).
- Discord, forums and chat applications make a link preview from the tags `og:title`, `og:description` and `og:image` of the HTML of the page.
  Their robots do not run JavaScript. The web app is a single-page application, so its `index.html` has no deck data.
- The web container serves `index.html` with Nginx ([ADR 0024](0024-container-images.md)). Nginx has server side includes (SSI) and `auth_request`.
- Only public decks can show on other sites. The read model of the public decks ([ADR 0029](0029-public-deck-read-model.md)) has no private, unlisted or hidden deck.
- An unlisted deck is for "people with the link". A preview or an embed of an unlisted deck can send its list to a site that the owner did not choose.
- Each page except the embed must refuse a frame of another site, to stop clickjacking.

We looked at these options for the image:

1. The API draws the image. This needs a drawing library or a headless browser, and the API must get each card image from Scryfall.
2. The web app draws the image on a canvas and gives a PNG file. The API has no new work and no new dependency.

We looked at these options for the link preview:

1. The API serves a server-rendered image at a URL for `og:image`. This needs option 1 above.
2. `og:image` is the Scryfall image of the first commander. The API gives the preview tags, and Nginx puts them in `index.html` with SSI.
3. A separate page for robots, chosen by the `User-Agent` header. This is fragile, and the robots change often.

## Decision

Deck image:

- The web app draws the image on a canvas (`web/src/share/deckImage.ts`) and gives a PNG file. It uses no new library.
- The user selects one of two styles: the commander art with the list grouped by card type, or a grid of the card images.
- The image is 1600 pixels wide. The grid shows 120 distinct cards or fewer, so the file stays smaller than the 8 MB limit of Discord.
  A deck of 60 cards gives a file of about 2 MB.
- The layout is in `web/src/share/deckImageLayout.ts`, with no drawing code, so the tests check it without a canvas.

Link preview:

- `GET /api/v1/public-decks/{id}/preview` gives the HTML tags of the preview: Open Graph, the Twitter card and the oEmbed discovery link.
  It reads the read model of the public decks. Other decks give the status 404.
- `og:image` is the `normal` Scryfall image of the first commander (488 × 680 pixels). A deck with no commander has no `og:image`.
- Nginx serves the deck page `/decks/{id}` with SSI. An internal location gets the tags from the API, with no cookie and no token.
  An error of the API gives an empty include, so the page always opens.
- The API makes the absolute links from the setting `Auth:PublicUrl`.

Embed:

- The web app has the route `/embed/decks/{id}`. It shows a read-only list with no application shell and no session.
  It asks `GET /api/v1/public-decks/{id}` first, so a deck that is not public shows a "not available" text.
- Nginx asks the same endpoint with `auth_request`. A deck that is not public gives the status 404, also for a robot.
- Only `/embed/` paths get `Content-Security-Policy: frame-ancestors *` and no `X-Frame-Options` header.
  All the other pages get `frame-ancestors 'self'` and `X-Frame-Options: SAMEORIGIN`.
- The share dialog gives the HTML code of an iframe and a copy button.
- `GET /api/v1/oembed?url=...` gives the oEmbed response (`rich`, JSON only) of the link of a public deck. Blogs and forums with oEmbed then show the embed from the link alone.

The image, the embed and oEmbed are for public decks only. An unlisted deck keeps the "Copy the link" button only.

## Consequences

- The API and the worker get no new dependency. The web app gets no new npm package.
- The image and the preview need the Scryfall servers. A card image that does not load gives a frame with the card name.
- The preview shows the commander card, not an image of the full list. A server-rendered image can come later with its own ADR, if users ask for it.
- A reverse proxy other than the web container of Reliquary must apply the same SSI, `auth_request` and frame headers. Without them, the pages work, but the previews are empty and all pages accept a frame.
- A link preview can show an old version of a deck for up to 5 minutes, because the response has `Cache-Control: public, max-age=300`. Discord also keeps previews in its own cache.
- When a public deck becomes private, its embed and its preview stop after the read model changes, about one second later.
- The embed page has no session, so an owner sees the same view as a visitor.
