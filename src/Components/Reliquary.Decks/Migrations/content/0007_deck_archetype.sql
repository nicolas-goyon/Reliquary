-- The archetype of a deck, for example "control". The owner chooses it from a fixed list (DeckRules.Archetypes).
-- A null value means no archetype. The API checks the values, so a new archetype needs no migration.
alter table content.decks add column archetype text;
