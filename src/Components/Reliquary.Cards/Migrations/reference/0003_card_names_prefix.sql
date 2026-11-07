-- A pattern index for the autocomplete of card names: "sol r%" uses a range of the index.
-- The trigram index stays for the search of a part of a name.
create index card_names_prefix_idx on reference.card_names (name_normalized text_pattern_ops);
