-- The purchase preferences of the deck lines, as JSON. See docs/architecture/purchase-list.md.
-- A null value means no preference: each copy of the card fits.
alter table content.deck_lines add column purchase_preferences jsonb;

-- A frozen version keeps the preferences of its lines. The new column changes no row, so the trigger of ADR 0033 does not run.
alter table content.deck_version_lines add column purchase_preferences jsonb;
