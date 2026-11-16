-- The archetype of a public deck in the read model, for the search by archetype. See docs/decisions/0029-public-deck-read-model.md.
-- The rows of the existing public decks get the archetype at the next change of the deck.
alter table users.public_decks add column archetype text;

create index public_decks_archetype_idx on users.public_decks (archetype, updated_at desc) where archetype is not null;
