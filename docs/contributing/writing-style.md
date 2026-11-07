# Writing style

All English text in Reliquary follows ASD-STE100 Simplified Technical English (STE).
STE is a controlled language from the aerospace industry.
It removes words with more than one meaning and sentences with more than one possible structure.

Many readers of this project do not read English as their first language.
Translation tools and AI agents also read the text.
STE makes the text clear for all of these readers.

## Scope

Apply these rules to:

- documentation, including README files and decision records
- code comments and API descriptions
- commit messages, pull request text and issue text
- API error messages and log messages
- the English interface text

French interface text uses natural French, because STE is a standard for English.
The [translation guide](../development/translations.md) gives the rules for French text.

## Two modes

Use the strict mode where a wrong reading has a cost.
Use the STE-flavored mode for explanations.

| Mode | Use it for | Rules |
| --- | --- | --- |
| Strict | Procedures, error messages, log messages, API descriptions, instructions for AI agents | All the rules on this page. A sentence that gives an instruction has 20 words or fewer. |
| STE-flavored | Specifications, explanations, README files, pull request text, changelogs | All the structural rules. Use the word rules as a direction, and keep some range in the vocabulary. |

## Structural rules

Apply these rules in both modes.
The examples that break a rule are in code format, so that the automatic check ignores them.

| Rule | Write | Do not write |
| --- | --- | --- |
| Use the active voice. Name the actor. | The importer reads the Scryfall bulk file. | `The Scryfall bulk file is read.` |
| Do not use phrasal verbs. A two-word verb has meanings that its parts do not tell. | Start the instance. Remove the card from the deck. | `Spin up the instance. Take out the card.` |
| Give one instruction in each sentence. | Open the deck. Select the cards. | `Open the deck and select the cards, then apply the tag.` |
| Keep sentences short. | 20 words or fewer for an instruction. 25 words or fewer for a description. | A sentence with many clauses. |
| Do not use semicolons. | Write two sentences. | `The deck is legal; the list is complete.` |
| Use three words or fewer in a noun cluster. | the search parser for card queries | `the Scryfall syntax card search query parser` |
| Do not remove words to make a sentence shorter. | Files that the backup does not contain are lost. | `Files not backed up lost.` |
| Keep the modality of the source. | The import may have failed. | `The import failed.` (when the source only says "may have failed") |
| Use simple tenses. | The job completed. | `The job has completed.` |
| Write one topic in each paragraph, with 6 sentences or fewer. | Two short paragraphs. | One paragraph with three topics. |
| Use a list for 3 or more steps or conditions. | A numbered list of steps. | A sequence inside one long sentence. |

Simple tenses have one exception.
When a compound tense gives information that a simple tense cannot give, keep the compound tense.
For example, "may have failed" is a hedge, and `has completed` can tell that the result is available now.

## Word rules

- Use one word for one meaning.
  The [glossary](../glossary.md) gives the project terms and the verbs to use.
- Add a new term to the glossary in the same pull request as the text that uses it.
- Use a verb for an action, not a noun.
  Write "Analyze the deck", not `Perform an analysis of the deck`.
- Use the plain, common word.
- Do not use marketing adjectives such as `seamless`, `robust` or `blazing-fast`.
  Give a measurement: "Card search answers in less than 150 ms."
- Do not stack hedges such as `it may potentially help to improve`.
  State the claim, or delete it.

## Markdown format

- Write one sentence on each line.
  A Git diff then shows only the sentence that changed.
- Start each file with one level-1 heading.
- Write headings in sentence case: "Deck building", not "Deck Building".
- Use relative links between documents of the repository.
- Put code, commands, file names and identifiers in code format.

## English interface text

- Use a verb for a button: "Save", "Add card", "Export deck".
- Use a noun for a title: "Collection", "Deck analysis".
- Use the glossary term for each concept.
- Write error messages in the strict mode.
  Tell what happened, then tell what the user can do.
  Example: "The deck has 101 cards. Remove 1 card to make the deck legal."

## Check your text

The `just lint-docs` command checks all Markdown files and the English interface text.
Without `just`, run `python3 tools/ste-lint/lint-docs.py`.
The same check runs in CI for each pull request.

The check finds two types of problems:

- Hard violations make the check fail:
  - semicolons
  - sentences with more than 25 words
  - phrasal verbs from the list of the check
  - nominalizations and marketing adjectives
  - different words for one action
  - list items that end with "and" or "or"
- Advisory findings do not make the check fail: passive voice and compound tenses.
  Read each advisory finding, and change the sentence when the rule applies.

The check uses patterns, not a full grammar.
It does not find all phrasal verbs, so `take off the panel` passes.
A clean result does not prove that the text follows STE.
The review of each pull request also checks the writing.

When the check gives a false result, change the sentence.
For example, the word "fixed" can trigger the rule for different words for one action.

The [tools/ste-lint](../../tools/ste-lint/README.md) folder tells where the check comes from.

## The ASD-STE100 standard

This project applies the rules of ASD-STE100 Issue 9.
The official STE dictionary has limits on reproduction, so this repository does not contain it.
The project applies the principle of the dictionary instead: use the plainest common word, and use it the same way every time.
To get the standard, use the request form on the [official ASD-STE100 site](https://www.asd-ste100.org/).
