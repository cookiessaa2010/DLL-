# TOR diplomacy research

Audit target: The Old Realms `v1.3.15` on Mount & Blade II: Bannerlord `1.3.15.110062`.

## TOR-owned diplomacy

### `TOR_Core.Models.TORDiplomacyModel`

TOR replaces the vanilla diplomacy model. War and peace are not generic Bannerlord decisions anymore: TOR scoring accounts for relative strength, war count, war duration/weariness, relations, culture/religion compatibility, territorial claims, common threats, alliance-war context and lore rivalry/affinity.

Important consequences for KaiTOR:

- do not add another `DiplomacyModel`;
- do not replace war/peace scoring;
- do not normalize TOR's lore weights into vanilla behavior;
- Chaos peace restrictions remain TOR-owned.

### `TOR_Core.Models.TORAllianceModel`

TOR owns alliance willingness and scoring. Alliances are meaningful military treaties, not cosmetic relationships.

### `TOR_Core.CampaignMechanics.Diplomacy.TORAllianceWarBehavior`

TOR tracks alliance/defensive wars separately from offensive wars. A defender's ally must honor the alliance or break it; AI resolves this using TOR scoring, while the player can receive the enforced kingdom decision. Alliance wars do not count against the same offensive-war limit.

KaiTOR must not create a parallel defensive-pact implementation that competes with this behavior.

### `TOR_Core.Models.TORTradeAgreementModel`

TOR has its own trade-agreement scoring plus `TORTradeAgreementAIBehavior`. A second trade treaty system would duplicate active TOR mechanics, so v0.1 does not add one.

### `TOR_Core.CampaignMechanics.Diplomacy.TORKingdomDecisionPermissionModel`

Important observed rules:

- Chaos cannot form alliances;
- hostile dominant religions can block alliances;
- Chaos is not allowed to use normal peace decisions.

KaiTOR v0.1 conservatively reuses `IsStartAllianceDecisionAllowedBetweenKingdoms` as a lore compatibility gate before creating a KaiTOR NAP. This is intentionally stricter than a generic historical non-aggression pact, but it guarantees the first version cannot create obviously forbidden TOR diplomatic pairings.

### `TOR_Core.Models.TORMarriageModel`

All four standard marriage permission methods currently return `false`. Vanilla marriage is therefore intentionally unavailable in TOR.

KaiTOR must not simply swap in `DefaultMarriageModel`: doing so could create unsupported cross-race spouses, family links, heirs, clan transfers and body/race inheritance combinations. Marriage is deferred to a separate opt-in subsystem after a race/culture/religion audit.

## Bannerlord 1.3.15 API findings

Verified against `BannerlordCode/bannerlord-1.3.15`:

- `GameModels.DiplomacyModel` is a public active model slot.
- `GameModels.AllianceModel` is a public active model slot.
- `GameModels.TradeAgreementModel` is a public active model slot.
- `GameModels.KingdomDecisionPermissionModel` is a public active model slot.
- `GameModels.MarriageModel` is a public active model slot.
- `KingdomDecisionPermissionModel.IsStartAllianceDecisionAllowedBetweenKingdoms(...)` exists with the signature used by KaiTOR.
- `FactionManager.IsAtWarAgainstFaction(IFaction, IFaction)` exists.
- `CampaignEvents.WarDeclared` uses `(IFaction, IFaction, DeclareWarAction.DeclareWarDetail)`.
- `CampaignEvents.MakePeace` uses `(IFaction, IFaction, MakePeaceAction.MakePeaceDetail)`.
- `CampaignTime.Now.ToDays` is `double`, not `float`.
- `IDataStore.SyncData<T>` is the standard CampaignBehavior persistence path.

The `ToDays` finding caught an early v0.1 implementation error before live testing; NAP and cooldown expiry storage now uses `Dictionary<string, double>`.

## Native `NoAttackBarterable` is not a kingdom NAP

Bannerlord contains `NoAttackBarterable`, but its `Apply()` only extends `NotAttackableByPlayerUntilTime` on the other faction when the original party is `MobileParty.MainParty.Party`. It is a player-party attack restriction, not a bilateral kingdom treaty and not an AI war-declaration constraint.

Therefore a kingdom-pair KaiTOR NAP remains a distinct mechanic.

## Save-system finding

TOR's save type definitions already include primitive keyed containers including `Dictionary<string, double>`, `Dictionary<string, float>` and `Dictionary<string, string>`, and TOR data uses `Dictionary<string, int>` in its extended hero state. KaiTOR deliberately stores only primitive pair-keyed treaty state and avoids custom saveable treaty classes in v0.1.

## v0.1 conclusion

Safe additive surface:

- kingdom-pair NAP state;
- trust and breach history;
- treaty cooldowns;
- console diagnostics;
- later UI/AI built on the same state.

Unsafe or duplicate surface for v0.1:

- replacing diplomacy/alliance/trade/marriage models;
- forcing war or peace;
- recreating TOR defensive alliances;
- enabling vanilla marriage;
- Harmony patches against TOR diplomacy before standalone compatibility is proven.
