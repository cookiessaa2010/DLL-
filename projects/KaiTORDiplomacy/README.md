# KaiTOR Diplomacy

Compatibility/diplomacy module for The Old Realms 1.3.15 and Mount & Blade II: Bannerlord 1.3.15.110062.

## v0.4.5 live-test scope

The module remains additive around TOR's diplomacy stack. It keeps TOR ownership of war, peace, alliance, trade and kingdom-decision rules, while selectively restoring Bannerlord's standard marriage flow through a wrapper over the exact `TOR_Core.Models.TORMarriageModel`.

Implemented in the live branch:

- non-aggression pacts with trust/breach/cooldown persistence;
- native Russian in-game diplomacy menu;
- TOR `town_outside` entry for `Дипломатия`;
- TOR `town_outside` entry for `Сменить народность поселения`;
- full settlement nationality/culture conversion through the existing TOR service bridge;
- AI ruler recruitment of existing noble clans through native `JoinKingdomAsClanBarterable`;
- standard Bannerlord marriage/courtship/arranged-marriage flow via `KaiPlayerMarriageModel`;
- standard AI marriage offers to the player's clan via Bannerlord `MarriageOfferCampaignBehavior`;
- staged cadet-house creation from an existing unmarried/childless adult lord of the ruling clan;
- cadet creation is queued on weekly tick and committed only after safe player settlement entry;
- `kaitor_diplomacy.marriage_status` and `kaitor_diplomacy.dynasty_status` diagnostics.

## Safety boundaries

Still intentionally disabled in LoadSafe:

- pregnancy wrapper;
- natural aging/death wrapper;
- custom marriage warning dialogue injection;
- Greenskin spore hero generation;
- Vampire Blood Kiss automation;
- Dawi women generation and Dawi pregnancy.

The Dawi female-asset gate remains hard `SAFE-OFF`.

Cadet-house limits:

- only if kingdom noble-clan deficit is at least 2;
- founder must already exist and be an adult lord of the ruling clan;
- founder must be unmarried, childless, not prisoner, not governor and have no owned party;
- ruling clan must keep at least one fortification;
- ruler needs at least 50,000 gold;
- one pending cadet request globally;
- request waits at least one campaign day;
- mutation occurs only on player fortification entry;
- 42-day global cooldown;
- 84-day per-kingdom cooldown.

## Compatibility gate

The runtime verifies TOR ownership of the critical model slots:

- `TOR_Core.Models.TORDiplomacyModel`
- `TOR_Core.Models.TORAllianceModel`
- `TOR_Core.Models.TORTradeAgreementModel`
- `TOR_Core.Models.TORMarriageModel` or the approved wrapper over that exact model
- `TOR_Core.CampaignMechanics.Diplomacy.TORKingdomDecisionPermissionModel`

If the expected stack is not present, treaty logic fails closed.

## Useful in-game diagnostics

```text
kaitor_diplomacy.status
kaitor_diplomacy.marriage_status
kaitor_diplomacy.dynasty_status
kaitor_diplomacy.settlement
kaitor_diplomacy.culture_support
kaitor_diplomacy.ledger
```

See `FIRST_TEST_RU.md` for the current live-test procedure.
