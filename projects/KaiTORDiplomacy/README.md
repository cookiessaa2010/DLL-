# KaiTOR Diplomacy — v1.3.15.70 Strategic FULL-Test

Target:
- Mount & Blade II: Bannerlord 1.3.15.110062
- The Old Realms 1.3.15
- Module id: `KaiTOR_Diplomacy`

This module is additive around The Old Realms. TOR keeps ownership of war/peace/alliance/trade, ServeAsAHireling, races, religions, duels, books, post-battle systems and assimilation. KaiTOR adds non-aggression pacts, family/dynastic simulation, messenger diplomacy, war exhaustion, negotiated peace terms, political memory, Council analysis, Service Record, Realm House elevation, fief grants, conquest claims, Threat and Grievances.

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

### Strategic Diplomacy Expansion
- NAP hard guard covers direct TOR/vanilla war paths; mandatory kingdom-creation/rebellion/claim wars use a logged forced-breach path.
- Messenger: known-hero encyclopedia action, 6–72h travel, moving-target tracking, persisted queue, Later/Recall and real ConversationMission.
- War Exhaustion: 0–100 per side from native war statistics and campaign events; additive TOR peace modifier only.
- Peace Terms: status quo, reparations, return of a captured fief, peace + 90-day NAP through native actions.
- Dynastic Diplomacy 2.0: persisted ruling-house political memory modifying TOR alliance/trade/war/peace scores without replacing TOR models.
- Council: read-only strategic analysis.
- TOR Service Record: observes existing ServeAsAHireling, including TOR desertion semantics.
- Realm House Promotion: ruler can elevate an eligible companion through the existing native clan factory.
- Grant Fief: native KingdomManager gifting frontend.
- Right of Conquest: temporary claimant merit bonus inside native SettlementClaimantDecision.
- Threat: expansion reputation modifying TOR diplomatic scores; no coalition or second war AI.
- Grievances: persisted political memory only; no civil-war engine in this version.

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
kaitor_diplomacy.messenger_status
kaitor_diplomacy.nap_guard_status
kaitor_diplomacy.war_status
kaitor_diplomacy.dynasty_status
kaitor_diplomacy.service_status
kaitor_diplomacy.realm_status
kaitor_diplomacy.threat_status
kaitor_diplomacy.grievance_status
kaitor_diplomacy.live_test_start
kaitor_diplomacy.live_test_snapshot
kaitor_diplomacy.live_test_path
```

## Release status

- CODE: implementation candidate
- CI compile/safety/package: required before promotion
- LIVE TEST: still required in the real TOR campaign
- Dawi automatic population: ON, bounded by per-clan quota/cooldown; still requires live observation for balance/save integrity

See `module/TEST-v0.7.0-RU.md` for the Strategic Expansion live-test checklist.
