# Translation guide

The interface of Reliquary is in French and in English.
This guide gives the rules for the interface texts.
Terms follow the [glossary](../glossary.md), which also gives the French term for each concept.

## Principles

- English is the source language. Write the English text first.
- Each key exists in English and in French. A test fails when a key is missing in one language.
- The English text follows the [writing rules](../contributing/writing-style.md). The check also reads the English interface texts.
- The French text uses natural French. STE is a standard for English only.
- Card names never go into the translation files. Card names come from the card data, which contains the localized names.

## Files

The texts are in JSON files, one file for each language and namespace:

```text
web/src/locales/en/common.json
web/src/locales/fr/common.json
```

The `common` namespace contains the texts of the application shell.
Create a new namespace for each large feature, for example `decks` or `collection`.
Add each new namespace to `resources` in `web/src/i18n/index.ts`, for each language.

## Keys

- Group the keys by screen or by feature: `home.title`, `apiStatus.available`.
- Write each part of a key in camelCase.
- Give each key a name that tells its role, not its text: `deck.exportButton`, not `deck.export`.
- Do not use one key in two places with different meanings. Create two keys.

TypeScript knows the English keys.
A call to `t()` with an unknown key is a type error.

## Variables and plurals

- Write variables with double braces: `"Version {{version}}."`
- Keep the same variables in each language. A test checks this.
- For plurals, use the [plural suffixes of i18next](https://www.i18next.com/translation-function/plurals), for example `_one` and `_other`.
- Give the same plural keys in the two languages, so that the key test passes.

## French typography

- Put a non-breaking space (U+00A0) before `:`, `;`, `!` and `?`.
- Use the French quotation marks with non-breaking spaces: « texte ».
- Use the typographic apostrophe: `l’API`, not `l'API`.
- Address the user with "vous".
- Use the French terms of the glossary, for example « deck monté » and « emplacement ».

## API errors

The API returns a stable error code for each error.
The web app translates the code into the language of the user.
The English message in the API response is for developers. It follows the strict mode of the writing rules.

## Add a language

1. Copy the folder `web/src/locales/en` to a folder with the new language code, for example `web/src/locales/de`.
2. Translate each text.
3. Add the language code to `supportedLanguages` in `web/src/i18n/index.ts`.
4. Add the resources of the new language to `resources` in the same file.
5. Add the name of the language to the `language` keys of each language.
6. Add a column for the new language to the [glossary](../glossary.md).
7. Run `just test`.
