# KaiTOR Diplomacy v0.2.0 — live-test TOR 1.3.15

Цель прогона: доказать, что KaiTOR загружается поверх TOR 1.3.15, сохраняет собственное состояние без хрупких save-типов, не ломает TOR diplomacy/assimilation и безопасно обслуживает culture conversion + player-family marriage.

## Требования

- Mount & Blade II: Bannerlord `1.3.15.110062`.
- The Old Realms `v1.3.15`.
- Порядок модулей: `Native -> SandBoxCore -> Sandbox -> TOR_Armory -> TOR_Environment -> TOR_Core -> KaiTOR_Diplomacy`.
- На этот тест не подключать KaiTOR Online/Coop.
- Сделать резервную копию тестового save перед первым запуском новой DLL.

## Preflight

```powershell
.\tests\Test-KaiTORDiplomacyContracts.ps1
.\Test-KaiTORDiplomacyReadiness.ps1 -BannerlordRoot "C:\PATH\TO\Mount & Blade II Bannerlord"
```

Ожидается `PASS` в обоих тестах.

## Проверка запуска и save schema

После загрузки кампании:

```text
kaitor_diplomacy.status
kaitor_diplomacy.save_status
kaitor_diplomacy.culture_support
```

Ожидается:

- TOR compatibility gate PASS;
- save schema поддерживается текущей DLL;
- culture support matrix не содержит BLOCKED для тестируемой культуры.

### Existing-save upgrade

1. загрузить существующий TOR/KaiTOR save;
2. ничего не менять и сразу сохранить в новый слот;
3. выйти в главное меню;
4. загрузить новый слот;
5. повторить `save_status`;
6. проверить TOR diplomacy, поселения, героев и инвентари.

Старый save без KaiTOR schema должен мягко перейти с schema 0 на текущую schema 1. KaiTOR не должен создавать custom SaveableTypeDefiner/Hero/Settlement graph.

## NAP / trust

Выбрать два невоюющих совместимых по TOR королевства:

```text
kaitor_diplomacy.nap <kingdomA> <kingdomB> 30
kaitor_diplomacy.inspect <kingdomA> <kingdomB>
kaitor_diplomacy.ledger
```

Проверить save/load. Срок NAP, trust, breaches и cooldown должны сохраниться.

Естественное окончание: trust `+5`.
Ручной разрыв: trust `-10`, cooldown `10` дней.
Война при активном NAP: breach `+1`, trust `-30`, cooldown `30` дней; сама война остаётся под TOR.

## Full settlement culture conversion

Требуется:

- town/castle игрока;
- clan tier >= 3;
- >= 100,000 denars;
- поселение другой культуры;
- не siege;
- не TOR special `castle_BK1`.

До конверсии записать:

```text
kaitor_diplomacy.settlement
kaitor_diplomacy.culture_support
```

Также проверить вручную:

- culture города/замка и bound villages;
- notables + volunteers;
- tavern mercenary;
- wanderer;
- caravan troops;
- spell trainer;
- enchanter/alchemist;
- Empire Bounty Master, если целевая культура Empire;
- Greenskin Kwartamasta, если целевая культура Greenskin;
- ассортимент магазина.

После оплаты 100,000 проверить:

- settlement + villages получили clan culture;
- старые локальные notables заменены культурно корректными;
- recruits/tavern/wanderer/caravans соответствуют новой культуре;
- spell/enchant cultural services обновились;
- Empire town получает корректного Bounty Master;
- Greenskin-owned fortification получает ровно одного Kwartamasta;
- будущая workshop/shop production использует новую культуру;
- старый market stock может временно оставаться и естественно уходить.

Сохранить, выйти и загрузить. Culture должна остаться новой: её persistence принадлежит TOR `AssimilationCampaignBehavior`, а не отдельной таблице KaiTOR.

### Landmark regression

Проверить, что conversion НЕ клонирует:

- Nuln Master Engineer в другие города;
- Altdorf Prestige Noble в другие города;
- Dawi Karak guildmasters в обычный Dawi town;
- Lithanel envoys в обычный Eonir town;
- settlement-id priests/shrines в произвольные города.

Для настоящего Karak под Dawi guildmasters должны работать штатно. Для Lithanel под Eonir — envoys штатно.

## Player-family marriage safety

Полная матрица описана в `FAMILY_COMPATIBILITY_RU.md`.

KaiTOR разрешает player-clan social marriage между:

`Empire / Bretonnia / Sylvania / Mousillon / Asrai / Eonir / Dawi`

Greenskins исключены.

### Тест предупреждения о бездетном браке

Выбрать пару, для которой KaiTOR разрешает свадьбу, но `TorFamilySafety` запрещает vanilla pregnancy, например Dawi ↔ Human или Dawi ↔ Elf.

Дойти до финальной стадии брачных договорённостей.

До открытия обычного marriage barter должно появиться предупреждение KaiTOR о том, что:

- брак разрешён;
- биологических детей у этой пары не будет;
- KaiTOR не будет генерировать offspring для этой пары.

Проверить обе кнопки:

1. `I understand. Continue with the marriage arrangements.` — после неё должен открыться обычный Bannerlord marriage barter и свадьба может завершиться штатно;
2. `Not now. I want to reconsider this marriage.` — диалог должен закрыться без свадьбы и без изменения spouse/state.

После успешной свадьбы не должно быть повторного дублирующего warning для обычного courtship path. Для alternate arranged/barter path допускается резервное информационное сообщение на `BeforeHeroesMarried`.

Сделать save/load сразу после свадьбы: warning behavior не должен добавлять сериализуемые данные.

### Тест Dawi -> Human/Elf

1. player или член player clan культуры Dawi;
2. выбрать допустимую свободную человеческую/эльфийскую героиню;
3. убедиться, что marriage suitability больше не отбрасывается только из-за другого Bannerlord Race id;
4. оформить брак штатным player-facing механизмом;
5. прожить минимум несколько недель campaign time;
6. сохранить/загрузить несколько раз.

Ожидается:

- супруги остаются корректными;
- pregnancy не создаётся для Dawi cross-race пары;
- не появляется broken child Hero;
- save продолжает загружаться.

### Тест Human -> Human и Elf -> Elf

Для пары с одинаковым безопасным `CharacterObject.Race` оригинальный active PregnancyModel должен продолжить работать без изменения своих вероятностей.

Проверить отдельно:

- Empire-compatible same-race pair;
- Bretonnian same-race pair;
- Asrai/Eonir elf pair, если оба реально `race=elf`.

### Тест Vampire

Женщина-вампир может быть marriage partner, но TOR vampire/undead hero не должен получать vanilla pregnancy. Прожить несколько недель + save/load.

### Тест Greenskin

Orc/Greenskin не должен становиться допустимым marriage/family partner. Технические `townswoman_greenskins` не считать женскими Orc templates.

## TOR regressions

После всех операций проверить:

- TOR war/peace;
- Chaos restrictions;
- alliances + ally call;
- trade agreements;
- religion/faith systems;
- spell trainers/enchanters;
- bounty master;
- Dawi/Eonir landmark services;
- Greenskin Teef/Kwartamasta;
- save/load минимум 3 цикла;
- загрузку старого save после замены только KaiTOR DLL.

## Что прислать при ошибке

- точный скрин/текст exception;
- `rgl_log_*.txt` / crash report;
- `kaitor_diplomacy.status`;
- `kaitor_diplomacy.save_status`;
- `kaitor_diplomacy.culture_support`;
- `kaitor_diplomacy.settlement`, если ошибка в settlement;
- культуры/race пары, если ошибка family;
- новый или существующий save;
- действие непосредственно перед ошибкой: startup / save / load / NAP / culture conversion / marriage / pregnancy / TOR service.
