# KaiTOR Diplomacy design notes

This module is intentionally isolated from KaiTOR Online/Coop until TOR-specific diplomacy compatibility is mapped and tested.

## Prime directive

TOR remains authoritative for every diplomatic mechanic it already implements. KaiTOR Diplomacy may add state, presentation and optional proposals around TOR, but it must not silently replace TOR game models or bypass TOR permission rules.

## Ownership matrix

| Mechanic | Authoritative owner | KaiTOR Diplomacy v0.1 role |
| --- | --- | --- |
| War declaration scoring | `TORDiplomacyModel` | Observe only; a broken NAP records consequences but never cancels TOR's war. |
| Peace scoring | `TORDiplomacyModel` | Observe only. No automatic peace. |
| Chaos peace restriction | TOR diplomacy/permission models | Preserve exactly. |
| Alliances | `TORAllianceModel` + `TORAllianceWarBehavior` | Do not duplicate. |
| Defensive alliance war obligations | `TORAllianceWarBehavior` | Do not duplicate or reclassify wars. |
| Trade agreements | `TORTradeAgreementModel` + TOR AI behavior | Do not duplicate. |
| Alliance/religion permission | `TORKingdomDecisionPermissionModel` | Reuse as a conservative compatibility gate for KaiTOR treaties. |
| Marriage | `TORMarriageModel` | Keep disabled until a dedicated race/dynasty design is proven safe. |
| Lore rivalries/affinities | TOR models/helpers | Never override. Future AI proposal scoring may read these only through TOR-owned public models or a narrow adapter. |
| Non-aggression pact | KaiTOR Diplomacy | Additive treaty state only. |
| Treaty trust/breach history | KaiTOR Diplomacy | Additive state only; does not alter TOR diplomacy yet. |

## v0.1 treaty semantics

A non-aggression pact is a diplomatic promise, not an engine-level prohibition. TOR may still declare a war if its own logic decides that it should. If war begins while a NAP is active, KaiTOR records a breach, removes the pact, reduces pairwise trust by 30 and applies a 30-day NAP cooldown.

A voluntary early cancellation reduces trust by 10 and applies a 10-day cooldown. Natural completion increases trust by 5. Trust is clamped to `[-100, 100]`.

This design is deliberate: the first standalone release proves persistence and compatibility without patching `TORDiplomacyModel` or `TORKingdomDecisionPermissionModel`. Hard war blocking can only be considered later as an explicit optional policy after standalone testing.

## Save-state contract

KaiTOR stores only primitive keyed containers in `CampaignBehavior.SyncData`:

- NAP expiry day: `Dictionary<string, double>`;
- breach count: `Dictionary<string, int>`;
- diplomatic trust: `Dictionary<string, int>`;
- post-break NAP cooldown expiry: `Dictionary<string, double>`.

The pair key is deterministic (`kingdomA|kingdomB`, StringIds sorted ordinally), so save data does not depend on object reference identity.

`CampaignTime.Now.ToDays` is `double` in Bannerlord 1.3.15; treaty/cooldown time storage therefore remains `double` and must not regress to `float`.

## Compatibility gate

At campaign launch all of these model slots must be owned by TOR:

- `TOR_Core.Models.TORDiplomacyModel`
- `TOR_Core.Models.TORAllianceModel`
- `TOR_Core.Models.TORTradeAgreementModel`
- `TOR_Core.Models.TORMarriageModel`
- `TOR_Core.CampaignMechanics.Diplomacy.TORKingdomDecisionPermissionModel`

Any mismatch disables KaiTOR treaty runtime for that campaign. Fail closed, never guess.

## Staged roadmap

Phase A: standalone compatibility + NAP/trust persistence.

Phase B: player UI and readable diplomatic ledger, still without model replacement.

Phase C: AI proposals using TOR-compatible scoring and trust history.

Phase D: additional treaties only where they do not overlap TOR (for example guarantees or access arrangements after a separate mechanical audit).

Phase E: marriage/dynasty as an independent opt-in subsystem with an explicit TOR race/culture/religion compatibility matrix.

Phase F: only after standalone acceptance, evaluate KaiTOR Online/Coop synchronization and server authority for treaty actions.
