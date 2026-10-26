# Deck analysis

This file describes the analysis of a deck.
The analysis is only about the game.
Other parts of Reliquary handle all the subjects about prices and purchases.
Terms follow the [glossary](../glossary.md).

## Analysis features

- **Common base for all formats**:
  - mana curve
  - distribution by type, color and category
  - mana sources against the mana symbols that the cards need, with the Karsten method
  - draw probabilities, for example "X lands on turn 3"
- **Analysis for each format**, with Commander first: each format defines its expected categories and its targets.
- **Needs of a Commander deck**: the analysis compares each category with a target.
  The categories are ramp, card draw, interaction, board wipes, lands, synergy and win conditions.
  Known Commander templates inspire each target, and the analysis adjusts the target to the curve and the strategy.
  The analysis shows the categories with too few cards and the categories with too many cards.
- Reliquary detects combos with Commander Spellbook, and shows an indicative power level or bracket.
- Plugins can add their own analysis panels.

## Milestones

The project delivers the features of this file in these [milestones](../roadmap.md).

| Milestone | Features |
| --- | --- |
| M1 (Commander deck MVP) | All the features of this file, except the analysis panels from plugins |
| M4 (Plugins) | Analysis panels from plugins |
