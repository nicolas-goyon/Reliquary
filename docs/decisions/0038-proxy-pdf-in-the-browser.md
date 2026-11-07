# 0038. The web app makes the PDF of the proxies

- Status: Accepted
- Date: 2026-10-28
- Deciders: @nicolas-goyon

## Context

Issue [#155](https://github.com/nicolas-goyon/reliquary/issues/155) asks for a PDF file of proxies.
The PDF has 9 card images on each page, at the real card size of 63 × 88 mm, in A4 or Letter.
A later issue makes the purchase list, and the purchase list must use the same PDF function.

These facts limit the options:

- The card images are on the Scryfall servers (`cards.scryfall.io`). Reliquary keeps the addresses, not the images.
- The Scryfall image servers send `Access-Control-Allow-Origin: *`, so a browser can read the images.
- A deck of 100 cards needs about 100 images of 100 to 300 KB each.
- The main bundle of the web app must not grow for a feature that few users use each day.

We looked at two options:

1. The API makes the PDF. The API then gets each image from Scryfall. This adds traffic, memory use and a PDF library to the API, and the user waits with no progress.
2. The web app makes the PDF. The browser gets the images from Scryfall, and the API has no new work. The web app can show the progress of each image.

## Decision

- The web app makes the PDF (option 2), with the library [jsPDF](https://github.com/parallax/jsPDF) 4.2.1.
  `package.json` gives the exact version.
- The web app loads jsPDF with a dynamic `import()` when the user makes a PDF. Vite puts the library in a separate file.
- The function `makeProxyPdf` in `web/src/proxies/pdf.ts` takes a list of `{ imageUri, quantity, name }`.
  The deck page makes this list from the deck lines. The purchase list can make the same list.
- The web app uses the `large` image of Scryfall (672 × 936 pixels), and puts the JPEG data in the PDF with no new compression.
  The PDF contains each image once, also for 4 copies of a card.
- A card with no image, or with an image that does not load, gets a frame with its name.

## Consequences

- The API and the worker have no new dependency and no new load.
- The PDF needs a network connection to the Scryfall servers. Reliquary does not keep a copy of the images.
- The service worker keeps the jsPDF files with the other files of the application shell. This adds about 240 KB (gzip) to the first visit.
- The standard PDF fonts contain only Latin characters. A card name in another script gives wrong characters in the frame of a missing image.
