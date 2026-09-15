# KaiTOR Diplomacy v0.3.0 — live-test TOR 1.3.15

Цель прогона: доказать, что KaiTOR загружается поверх TOR 1.3.15, сохраняет собственное состояние без хрупких save-типов, не ломает TOR diplomacy/assimilation и безопасно обслуживает settlement conversion, world marriages, aging, AI vassals и race-specific population lifecycle.

## Требования

- Mount & Blade II: Bannerlord `1.3.15.110062`.
- The Old Realms `v1.3.15`.
- Порядок модулей: `Native -> SandBoxCore -> Sandbox -> TOR_Armory -> TOR_Environment -> TOR_Core -> KaiTOR_Diplomacy`.
- На этот тест не подключать KaiTOR Online/Coop.
- Сделать резервную копию test-save перед первым запуском новой DLL.

## Preflight

```powershell
.\tests\Test-KaiTORDiplomacyContracts.ps1
.\Test-KaiTORDiplomacyReadiness.ps1 -BannerlordRoot "C:\PATH\TO\Mount & Blade II Bannerlord"
```

Ожидается `PASS` в обоих тестах.

## Базовая диагностика

После загрузки кампании выполнить:

```text
kaitor_diplomacy.status
kaitor_diplomacy.save_status
kaitor_diplomacy.world_status
kaitor_diplomacy.marriages
kaitor_diplomacy.racial_status
kaitor_diplomacy.culture_support
```

Ожидается:

- TOR compatibility gate PASS;
- `KaiTOR world lifecycle: ENABLED`;
- активны `KaiPlayerMarriageModel`, `KaiPregnancyModel`, `KaiHeroDeathProbabilityModel`;
- `racial_status` показывает Greenskin spore pressure, Vampire Blood Kiss cooldown и Dawi asset gate;
- без отдельного female-Dawi asset pack строка Dawi должна быть `MISSING/SAFE-OFF`, а не ошибкой загрузки.

## Existing-save upgrade и aging catch-up

1. загрузить существующий TOR/KaiTOR save;
2. записать возраст 3–5 героев разных рас;
3. сохранить в новый слот и перезагрузить;
4. промотать campaign time;
5. снова проверить возраст и `world_status`.

Проверить особенно старый TOR-save, который долго жил с `IsLifeDeathCycleDisabled=true`: возможен заметный возрастной catch-up. Если появляется лавина мгновенных old-age deaths, зафиксировать save и лог — это отдельный hardening case, а не повод отключать lifecycle обратно.

Save schema KaiTOR остаётся примитивной: dictionaries/string/double/int без custom Saveable Hero/Settlement graph.

## NAP / trust

Выбрать два невоюющих совместимых по TOR королевства:

```text
kaitor_diplomacy.nap <kingdomA> <kingdomB> 30
kaitor_diplomacy.inspect <kingdomA> <kingdomB>
kaitor_diplomacy.ledger
```

Проверить save/load. Срок NAP, trust, breaches и cooldown должны сохраниться.

- естественное окончание: trust `+5`;
- ручной разрыв: trust `-10`, cooldown `10` дней;
- война при активном NAP: breach `+1`, trust `-30`, cooldown `30` дней; сама война остаётся под TOR.

## Full settlement culture conversion

Требуется player-owned town/castle, clan tier >= 3, >= 100,000 denars, другая культура, без siege и не `castle_BK1`.

До/после проверить:

```text
kaitor_diplomacy.settlement
kaitor_diplomacy.culture_support
```

Проверки:

- settlement + bound villages получают clan culture;
- старые локальные notables заменяются корректно;
- recruits/tavern/wanderer/caravans меняют культурную экосистему;
- spell/enchant services обновляются;
- Empire Bounty Master и Greenskin Kwartamasta появляются только там, где должны;
- future workshop/shop production следует новой культуре;
- legacy market stock не обязан исчезать мгновенно;
- landmark-сервисы Nuln/Altdorf/Karak/Lithanel не клонируются в чужие города;
- save/load сохраняет новую culture через TOR `AssimilationCampaignBehavior`.

## World marriages

TOR отключает NPC marriage; KaiTOR должен восстановить его для всего мира через native `RomanceCampaignBehavior`, а не прямой `MarriageAction`.

Промотать несколько недель/месяцев и повторить:

```text
kaitor_diplomacy.marriages
```

Проверить появление новых NPC-браков между допустимыми культурами.

### Бездетные cross-race браки

Для Dawi ↔ Human/Elf или другой разрешённой social-marriage пары с несовместимым `CharacterObject.Race`:

- свадьба разрешена;
- player-facing путь показывает предупреждение до финального barter;
- pregnancy не создаётся;
- broken child Hero не появляется;
- save/load работает.

### Безопасные пары

Human↔Human и Elf↔Elf с одинаковым реальным `CharacterObject.Race` должны продолжать использовать оригинальный active PregnancyModel без изменения его вероятностей.

## Dawi

До установки отдельного `KaiTOR_DawiWomen` asset pack:

- same-race Dawi pregnancy должна оставаться выключенной;
- `racial_status` должен показывать Dawi female asset gate `MISSING/SAFE-OFF`;
- Dawi не должны получать фальшивых женщин на male mesh.

После будущего подключения полного female asset pack sentinel-template `kaitor_dawi_woman_lord` должен переводить gate в `READY`. Только после этого разрешается same-race Dawi pregnancy. Cross-race Dawi marriage остаётся социальным и бездетным.

## Greenskin spores

Greenskins не используют marriage/pregnancy для размножения. KaiTOR накапливает скрытый spore pressure от контролируемых fortifications.

Тест:

1. выбрать AI Greenskin kingdom с хотя бы одним fortification;
2. записать `kaitor_diplomacy.racial_status`;
3. промотать недели;
4. pressure должен расти;
5. при достижении порога и отсутствии cooldown должен появиться новый взрослый Greenskin AI companion из TOR templates;
6. после spawn pressure уменьшается, начинается cooldown;
7. новый Hero должен быть `Occupation.Special + AICompanion`, принадлежать реальному clan и нормально переживать save/load;
8. он может позднее стать founder нового Greenskin cadet house через `KaiDynastyAiBehavior`.

Ограничения, которые обязательно проверить:

- максимум Greenskin AI companions ограничен;
- не создаётся ребёнок/мать/pregnancy;
- при провале TOR `AICompanion` attribute bridge созданный special Hero удаляется, а не остаётся сломанным;
- player kingdom не получает автономные spore decisions.

## Vampire Blood Kiss

Vampires не используют pregnancy. Новые вампиры должны появляться редким обращением существующих смертных.

Тест:

1. выбрать AI Sylvania или Mousillon kingdom;
2. убедиться, что там есть хотя бы один существующий vampire sponsor;
3. выполнить `racial_status`;
4. найти подходящего unmarried/childless adult human lord или TOR AI companion с non-negative relation к sponsor;
5. промотать время до срабатывания;
6. выбранный Hero должен сохранить identity/clan/name, но получить vampire race;
7. old-age mortality для него должна стать 0 через `KaiHeroDeathProbabilityModel`;
8. career/religion/spells KaiTOR сам не должен переписывать;
9. после обращения действует 180-day cooldown;
10. save/load минимум 3 раза.

Не допускать автоматического Blood Kiss для Dawi, Elf, Greenskin или другого custom race до отдельного решения по лору.

## AI vassals / cadet houses

Для AI kingdom с deficit по noble clans:

1. KaiTOR сначала пытается native `JoinKingdomAsClanBarterable`;
2. если recruit не прошёл, ruler с >=30,000 gold и >=2 fortifications может выделить новый cadet house;
3. founder — unmarried/childless adult lord или TOR AI companion;
4. новый `kaitor_house_*` должен быть реальным `Clan`, войти в kingdom через `ChangeKingdomAction`, получить fief через `ChangeOwnerOfSettlementAction` и seed gold;
5. cooldown создания дома — 180 дней;
6. target noble clans ограничен максимумом 10.

Отдельно проверить Dawi и Greenskins: Dawi могут поддерживать политическую преемственность cadet houses даже до female asset pack; Greenskin spore-born AI companions должны становиться валидными кандидатами founder.

## Race-aware natural death

Проверить при длительной промотке:

- Humans: обычная Bannerlord/TOR old-age death;
- Dawi: old-age mortality начинается примерно после 180, hard max около 420;
- Asrai/Eonir: no old-age death в обычном campaign horizon;
- Greenskins: no old-age death;
- Vampires/other undead: no old-age death;
- battle/mission death остаётся рабочей для всех.

## TOR regressions

После всех операций проверить:

- TOR war/peace;
- Chaos restrictions;
- alliances + ally call;
- trade agreements;
- religion/faith;
- careers/spells/resources;
- spell trainers/enchanters;
- bounty master;
- Dawi/Eonir landmark services;
- Greenskin Teef/Kwartamasta;
- save/load минимум 3 цикла;
- загрузку старого save после замены только KaiTOR DLL.

## Что прислать при ошибке

- точный exception/скрин;
- `rgl_log_*.txt` / crash report;
- `kaitor_diplomacy.status`;
- `kaitor_diplomacy.save_status`;
- `kaitor_diplomacy.world_status`;
- `kaitor_diplomacy.marriages`;
- `kaitor_diplomacy.racial_status`;
- `kaitor_diplomacy.culture_support`;
- `kaitor_diplomacy.settlement`, если ошибка в settlement;
- культуры/race затронутых Hero;
- новый или существующий save;
- действие перед ошибкой: startup / save / load / aging / NAP / conversion / marriage / pregnancy / spore spawn / Blood Kiss / clan recruit / cadet house.
