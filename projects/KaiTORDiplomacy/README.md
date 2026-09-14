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
- expired-pact cleanup;
- automatic pact termination and breach counter when war begins;
- treaty creation refuses kingdoms already at war;
- treaty creation passes through TOR's `IsStartAllianceDecisionAllowedBetweenKingdoms`, preserving Chaos/religion compatibility restrictions;
- no automatic peace, war, alliance, trade-agreement or marriage actions;
- console smoke-test commands.

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

This checks the TOR dependency chain, required TOR model gates and that the module does not contain Harmony/model replacement or direct forced war/peace/marriage actions.

## In-game smoke test

Enable the developer console, start a TOR campaign with `KaiTOR_Diplomacy` loaded after `TOR_Core`, then run:

```text
kaitor_diplomacy.status
kaitor_diplomacy.kingdoms
kaitor_diplomacy.nap <kingdomA> <kingdomB> <days>
kaitor_diplomacy.break_nap <kingdomA> <kingdomB>
```

Acceptance for v0.1.0:

1. `kaitor_diplomacy.status` reports compatibility PASS.
2. A permitted NAP survives save/load with the correct remaining duration.
3. A forbidden TOR pairing is refused.
4. A NAP expires naturally.
5. Declaring war removes the NAP and increments breach state without overriding TOR's war logic.
6. TOR alliances, trade agreements and Chaos peace restrictions continue to behave exactly as TOR defines them.
