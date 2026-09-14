# KaiTOR Diplomacy

Separate diplomacy compatibility module for The Old Realms 1.3.15 and Mount & Blade II: Bannerlord 1.3.15.110062.

## Design rule

KaiTOR Diplomacy is an additive layer. It does **not** replace TOR diplomacy, alliance, trade-agreement, marriage, war or peace models.

At campaign launch the module verifies that TOR owns all five critical model slots:

- `TOR_Core.Models.TORDiplomacyModel`
- `TOR_Core.Models.TORAllianceModel`
- `TOR_Core.Models.TORTradeAgreementModel`
- `TOR_Core.Models.TORMarriageModel`
- `TOR_Core.CampaignMechanics.Diplomacy.TORKingdomDecisionPermissionModel`

If any slot differs, KaiTOR Diplomacy fails closed and does not activate treaty logic.

## v0.1.0 MVP

Implemented:

- independent `KaiTOR_Diplomacy` Bannerlord module;
- exact dependency chain for Native/Sandbox/TOR 1.3.15;
- fail-closed TOR runtime compatibility gate;
- save/load persistence for KaiTOR treaty state;
- non-aggression pacts with configurable duration (1-365 campaign days);
- deterministic kingdom-pair keys based on StringId;
- diplomatic trust in the range `-100..100`;
- breach history per kingdom pair;
- +5 trust when a NAP completes naturally;
- -10 trust and 10-day NAP cooldown for voluntary early cancellation;
- -30 trust and 30-day NAP cooldown when war breaks an active NAP;
- expired-pact and cooldown cleanup on campaign ticks;
- treaty creation refuses kingdoms already at war;
- treaty creation passes through TOR's `IsStartAllianceDecisionAllowedBetweenKingdoms`, preserving Chaos/religion compatibility restrictions;
- diagnostics are read-only; mutation paths fail closed when TOR compatibility does not pass;
- no automatic peace, war, alliance, trade-agreement or marriage actions;
- console smoke-test commands;
- build/install script, static safety contracts and TOR readiness preflight.

Not enabled yet:

- marriage/dynasty features (TOR currently disables vanilla marriage through `TORMarriageModel`);
- treaty UI;
- AI treaty proposals;
- defensive guarantees beyond TOR's existing alliance system;
- integration with KaiTOR Online/Coop.

## Build

From PowerShell:

```powershell
.\Build-KaiTORDiplomacy.ps1 -BannerlordRoot "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
```

Build and install into `Modules\KaiTOR_Diplomacy`:

```powershell
.\Build-KaiTORDiplomacy.ps1 -BannerlordRoot "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord" -Install
```

The build script rejects a Bannerlord runtime that is not `1.3.15.110062`.

## Static safety test

```powershell
.\tests\Test-KaiTORDiplomacyContracts.ps1
```

This checks the TOR dependency chain, required TOR model gates, Bannerlord 1.3.15 treaty time precision and that the module does not contain Harmony/model replacement or direct forced war/peace/marriage actions.

## Installed-runtime preflight

After building/installing:

```powershell
.\Test-KaiTORDiplomacyReadiness.ps1 `
  -BannerlordRoot "C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
```

If TOR Workshop is in another Steam library, also pass `-WorkshopRoot` pointing at that library's `steamapps\workshop\content\261550` directory.

Expected final line:

```text
KaiTOR Diplomacy readiness: PASS
```

## In-game smoke test

Enable the developer console, start a TOR campaign with `KaiTOR_Diplomacy` loaded after `TOR_Core`, then run:

```text
kaitor_diplomacy.status
kaitor_diplomacy.kingdoms
kaitor_diplomacy.nap <kingdomA> <kingdomB> <days>
kaitor_diplomacy.inspect <kingdomA> <kingdomB>
kaitor_diplomacy.ledger
kaitor_diplomacy.break_nap <kingdomA> <kingdomB>
```

Acceptance for v0.1.0:

1. `kaitor_diplomacy.status` reports compatibility PASS.
2. A permitted NAP survives save/load with the correct remaining duration.
3. A forbidden TOR pairing is refused.
4. A NAP expires naturally and pair trust increases by 5 exactly once.
5. Voluntary cancellation removes the NAP, lowers trust by 10 and prevents re-signing for 10 days.
6. Declaring war removes the NAP, increments breach state, lowers trust by 30 and prevents re-signing for 30 days without overriding TOR's war logic.
7. `inspect` and `ledger` report state without mutating it.
8. TOR alliances, trade agreements and Chaos peace restrictions continue to behave exactly as TOR defines them.

See `FIRST_TEST_RU.md` for the Russian live-test checklist and `DESIGN_NOTES.md` for the TOR ownership matrix.
