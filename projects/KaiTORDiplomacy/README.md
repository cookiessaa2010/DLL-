# KaiTOR Diplomacy & Dawi — v0.6.4 FULL-Test

Target:
- Mount & Blade II: Bannerlord 1.3.15.110062
- The Old Realms 1.3.15
- Module id: `KaiTOR_Diplomacy`

This module is additive around The Old Realms. TOR keeps ownership of its war/peace/alliance/trade rules and lore restrictions; KaiTOR adds non-aggression pacts, family and dynastic workflows, race-aware family simulation, settlement nationality conversion, bounded racial population systems and safe realm-house growth.

## RU — что входит

### Полноценный UI
- основной экран: `KaiTORDiplomacyHubUIMovie`;
- отдельный Gauntlet layer: `KaiTORDiplomacyLayer`;
- вкладки: Обзор / Дипломатия / Семья / Держава и население;
- список держав с доверием и состоянием NAP;
- выбор срока 30/60/90/180 дней;
- предложение NAP и предложение разрыва из собственного UI;
- переход в семейные дела;
- переход к смене народности поселения;
- Dawi live-test из UI;
- классический GameMenu-hub сохранён как fallback;
- если нативный Kingdom UI не может открыть совет через reflection, игрок получает видимое сообщение вместо «мёртвого клика».

### Изоляция от KaiTOR Co-op
Diplomacy и Co-op технически разделены:
- Co-op module id: `Coop`;
- Diplomacy module id: `KaiTOR_Diplomacy`;
- Co-op movies: `CoopConnectionUIMovie`, `CoopOptionsUIMovie`;
- Diplomacy movie: `KaiTORDiplomacyHubUIMovie`;
- Co-op namespace: `GameInterface.Services.UI`;
- Diplomacy namespace: `KaiTOR.Diplomacy.UI`;
- Diplomacy localization ids: только `kaitor_diplomacy_ui_*`;
- Diplomacy Harmony id: `kaitor.diplomacy.kingdom-ui`;
- CI валит сборку при обнаружении Co-op UI identifiers внутри Diplomacy.

### Дипломатия
- NAP с сохранением срока, доверия, нарушений и cooldown;
- 30/60/90/180 дней через UI;
- council decision для предложения/разрыва;
- блок обычного объявления войны при активном NAP через permission wrapper;
- штрафы доверия/отношений за досрочный разрыв и нарушение войной;
- AI -> Player NAP;
- AI -> AI NAP;
- диалоговый frontend с правителем;
- native kingdom-screen action остаётся дополнительной точкой входа.

### Семья и династия
- «Семейные дела» в городе/замке;
- выбор члена рода, другого дома и кандидата;
- обычные брачные переговоры через `MarriageBarterable -> Barter -> MarriageAction`;
- династический брак: 500 000 динаров через persisted escrow;
- +20 отношений, +30 доверия, Dynastic Bond 180 дней;
- предупреждение о бездетном браке до сделки;
- female+female social marriage для дома игрока;
- усыновление через native `AdoptHeroAction`;
- входящие AI-предложения брака с accept/reject.

### Race-aware family / children
- TOR native marriage model disables all marriages; KaiTOR restores marriage for lore-capable peoples.
- Marriage uses only a lore adulthood floor and has no upper age cap.
- Humans/mortal peoples: marriage 18+; biological pregnancy keeps the ordinary 18–45 human curve.
- Dawi: marriage 30+; biological pregnancy 30+ with no vanilla upper-age cutoff; same dwarf race and female-Dawi assets required.
- Elves: marriage 18+; biological pregnancy 18+ with no vanilla upper-age cutoff.
- Vampires: social marriage allowed; biological pregnancy disabled; population grows through Blood Kiss.
- Greenskins: conventional marriage/pregnancy disabled; population grows through spores.
- Ordinary undead: conventional marriage and biological pregnancy disabled.
- pregnancy wrapper preserves the active TOR/Bannerlord model for ordinary mortals;
- social marriage and biological pregnancy are separated;
- TOR child education stages 8/14/16;
- profession/specialization effects;
- safe retry for failed stage-14 TOR effect;
- profession repair pass for adults.

### Dawi women
- integrated Dawi female template + dwarf race + TPAC gate;
- `kaitor_diplomacy.dawi_status`;
- `kaitor_diplomacy.dawi_spawn_test`;
- live-test spawn reports hero name, clan and settlement;
- spawned test woman is explicitly entered into the reported settlement;
- bounded automatic Dawi women population is **ON**: shortage-based weekly pass, max 3 generated women per eligible AI clan, 336-day per-clan cooldown.

### Other systems
- Blood Kiss + troll route;
- bounded Vampire population;
- bounded Greenskin population;
- safe AI realm-house growth through native clan factory;
- settlement nationality conversion with TOR service/market/caravan/recruit refresh;
- +50 relation mercy reward for deliberate lord release;
- save compatibility and runtime diagnostics.

## EN — included systems

- Isolated Gauntlet diplomacy dashboard with Overview / Diplomacy / Family / Realm & Population tabs.
- Independent naming and resource boundaries from KaiTOR Co-op.
- Non-aggression pacts with trust, breach penalties and cooldown persistence.
- Native council decisions for NAP proposals and pact termination.
- Family affairs, arranged marriage, dynastic marriage and adoption.
- Race-aware Dawi / long-lived-elf family simulation.
- TOR child education stages and profession packages.
- Dawi female live-test tooling plus bounded automatic female-Dawi shortage filling.
- Blood Kiss, bounded Vampire/Greenskin population, realm-house growth.
- Settlement nationality conversion.
- Mercy relation reward on deliberate lord release.

## Diagnostics

```text
kaitor_diplomacy.status
kaitor_diplomacy.world_status
kaitor_diplomacy.marriages
kaitor_diplomacy.racial_status
kaitor_diplomacy.family_rules
kaitor_diplomacy.dawi_status
kaitor_diplomacy.dawi_spawn_test
kaitor_diplomacy.ui_status
kaitor_diplomacy.ui_family
kaitor_diplomacy.save_status
kaitor_diplomacy.culture_support
kaitor_diplomacy.settlement
kaitor_diplomacy.kingdoms
kaitor_diplomacy.ledger
kaitor_diplomacy.inspect <kingdomA> <kingdomB>
```

## Release status

- CODE: implementation candidate
- CI compile/safety/package: required before promotion
- LIVE TEST: still required in the real TOR campaign
- Dawi automatic population: ON, bounded by per-clan quota/cooldown; still requires live observation for balance/save integrity

See `module/TEST-v0.6.4-RU.md` for the live-test checklist.
