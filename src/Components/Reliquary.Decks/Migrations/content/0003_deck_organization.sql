-- The organization of the decks: deck tags and the color identity for the filters of the deck list.
-- See docs/specification/deck-organization.md.

-- The deck tags of the user. A deck tag is a label on a deck, not on a deck line.
alter table content.decks add column tags text[] not null default '{}';

-- The color identity of the deck, with the letters W, U, B, R and G in this order. An empty text is colorless.
-- The commanders give the color identity. A deck with no commander uses the cards of its main deck and its companions.
alter table content.decks add column color_identity text not null default '';

create index decks_tags_idx on content.decks using gin (tags);
create index deck_folders_parent_idx on content.deck_folders (parent_id);
