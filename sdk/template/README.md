# Reliquary plugin template

This folder is a template for a [Reliquary](https://github.com/nicolas-goyon/reliquary) plugin.
The plugin "Deck size" adds one button to the action bar of a deck and one tab to the deck analysis. The two count the cards of the deck.

The guide [Write a plugin](https://nicolas-goyon.github.io/reliquary/plugins/) gives the full procedure.

## Files

| File | Content |
| --- | --- |
| `public/manifest.json` | The plugin manifest: the ID, the version, the scopes, the slots and the panels |
| `src/main.ts` | The entry point. It calls `definePlugin`. |
| `src/plugin.ts` | The code of the plugin: `activate` and the panels |
| `vite.config.ts` | The development server and the build of `dist/index.js` |

## Use the template

1. Copy this folder to a new Git repository.
2. In `package.json`, replace `"file:.."` with a version of the package `reliquary-plugin-sdk`.
   While the package is not on npm, clone the Reliquary repository and use the path of its folder `sdk`.
3. Change the `id`, the `name` and the `description` in `public/manifest.json`.
4. Install the packages, and keep the file `package-lock.json` in the repository:

   ```sh
   npm install
   ```

5. Start the development server:

   ```sh
   npm run dev
   ```

6. On a development instance of Reliquary, open **Account**, then the panel **Development plugins**, and add `http://localhost:5173/manifest.json`.
7. Build the plugin:

   ```sh
   npm run build
   ```

   The folder `dist` then contains `index.js` and `manifest.json`.

The test `sdk/src/template.test.ts` of the Reliquary repository runs this template against a fake core in each pull request.
