-- The columns, functions and indexes of the card search. See docs/architecture/card-search.md.

-- The numeric value of a power, a toughness, a loyalty or a defense, for the comparisons of the search.
-- "*", "X" and "?" count as 0. "1+*" counts as 1. "½" adds 0.5. "∞" counts as 1000000.
create function reference.stat_value(stat text) returns numeric
    language sql immutable parallel safe
as $$
    select case
        when stat is null then null
        when stat ~ '^[-+]?[0-9]+(\.[0-9]+)?' then substring(stat from '^[-+]?[0-9]+(?:\.[0-9]+)?')::numeric
            + case when stat like '%½%' then 0.5 else 0 end
        when stat like '%½%' then 0.5
        when stat like '%∞%' then 1000000
        else 0
    end
$$;

-- The symbols of a mana cost that are not generic mana, with a number for each repetition.
-- For example "{2}{W}{W}{U}" gives {U#1, W#1, W#2}. The operators @> and <@ then compare the costs as multisets.
create function reference.mana_symbols(cost text) returns text[]
    language sql immutable parallel safe
as $$
    select coalesce(array_agg(symbol || '#' || repetition order by symbol, repetition), '{}')
    from (
        select symbol, row_number() over (partition by symbol) as repetition
        from (
            select upper(match[1]) as symbol
            from regexp_matches(coalesce(cost, ''), '\{([^}]+)\}', 'g') as match
        ) symbols
        where symbol !~ '^[0-9]+$'
    ) numbered
$$;

-- The generic mana of a mana cost: the sum of the number symbols. For example "{2}{W}{W}" gives 2.
create function reference.mana_generic(cost text) returns integer
    language sql immutable parallel safe
as $$
    select coalesce(sum(match[1]::integer), 0)::integer
    from regexp_matches(coalesce(cost, ''), '\{([0-9]+)\}', 'g') as match
$$;

-- The Oracle text without the reminder text between parentheses. The keyword o: searches this text.
create function reference.remove_reminder_text(text text) returns text
    language sql immutable parallel safe
as $$
    select regexp_replace(text, '\s*\([^()]*\)', '', 'g')
$$;

-- The lowercase form of each element of a text array.
create function reference.lower_array(items text[]) returns text[]
    language sql immutable parallel safe
as $$
    select coalesce(array_agg(lower(element.item) order by element.position), '{}')
    from unnest(items) with ordinality as element (item, position)
$$;

alter table reference.oracle_cards
    add column oracle_text_plain text generated always as (reference.remove_reminder_text(oracle_text)) stored,
    add column keywords_normalized text[] generated always as (reference.lower_array(keywords)) stored;

-- Statistics on the expressions of the search, so that the planner estimates the number of matching cards.
create statistics reference.oracle_cards_type_stats on (lower(type_line)) from reference.oracle_cards;
create statistics reference.oracle_cards_front_type_stats on (lower(split_part(type_line, ' // ', 1))) from reference.oracle_cards;
create statistics reference.oracle_cards_text_stats on (lower(oracle_text_plain)) from reference.oracle_cards;

create index oracle_cards_text_plain_trgm_idx on reference.oracle_cards using gin (lower(oracle_text_plain) gin_trgm_ops);
create index oracle_cards_keywords_normalized_idx on reference.oracle_cards using gin (keywords_normalized);

alter table reference.card_faces
    add column mana_symbols text[] generated always as (reference.mana_symbols(mana_cost)) stored,
    add column mana_generic integer generated always as (reference.mana_generic(mana_cost)) stored;

-- Story Spotlight printings, for is:spotlight, and the printings with a content warning of Scryfall.
alter table reference.printings
    add column story_spotlight boolean not null default false,
    add column content_warning boolean not null default false;
alter table reference.import_printings
    add column story_spotlight boolean not null default false,
    add column content_warning boolean not null default false;

-- The funny cards (is:funny), and the cards that the search hides by default, as Scryfall does:
-- the cards with a content warning, and the funny or digital cards that are legal in no format, for example the playtest cards.
-- The import computes these columns. See CardFileImporter.
alter table reference.oracle_cards
    add column is_funny boolean not null default false,
    add column search_hidden boolean not null default false;

-- The printings that the search uses when the query gives no language:
-- the English printings, and the printings whose set has no English printing with the same collector number.
-- The import computes this column. See CardFileImporter.
-- The migration does not compute it, because the update of all the printings takes more than one minute.
-- Until the next import, the search uses the printings in all the languages.
alter table reference.printings add column default_lang boolean not null default true;

create index printings_games_idx on reference.printings using gin (games);

create index printings_flavor_trgm_idx on reference.printings using gin (lower(flavor_text) gin_trgm_ops);
create index printings_default_oracle_idx on reference.printings (oracle_id) where default_lang;

analyze reference.oracle_cards, reference.card_faces, reference.printings;
