# Diplomacy v0.4.5 — live-test TOR 1.3.15

Цель: проверить нативные браки Bannerlord поверх TOR, доступность дипломатии/смены народности в реальном TOR-меню города и безопасное появление новых кадетских кланов без мутаций на weekly world tick.

## Требования

- Mount & Blade II: Bannerlord `1.3.15.110062`.
- The Old Realms `v1.3.15`.
- Модуль `KaiTOR_Diplomacy` загружен после `TOR_Core`.
- Перед первым запуском сделать резервную копию test-save.

## Базовая диагностика

После загрузки существующего сейва выполнить:

```text
kaitor_diplomacy.status
kaitor_diplomacy.marriage_status
kaitor_diplomacy.dynasty_status
kaitor_diplomacy.settlement
kaitor_diplomacy.culture_support
```

Ожидается:

- compatibility gate PASS;
- `marriage_status`: `KaiPlayerMarriageModel` активен поверх `TOR_Core.Models.TORMarriageModel`;
- `RomanceCampaignBehavior=LOADED`;
- `MarriageOfferCampaignBehavior=LOADED`;
- Dawi women assets = `SAFE-OFF`;
- `dynasty_status` показывает recruitment status и отдельную `Cadet-house safe queue`.

## Город и замок

В собственном городе на TOR-экране, который использует `town_outside`, должны появиться обычные пункты:

- `Дипломатия`;
- `Сменить народность поселения`.

Никаких слов `KaiTOR`, названия мода или технических меток в игровом интерфейсе быть не должно.

### Смена народности

Условия:

- поселение принадлежит клану игрока;
- clan tier >= 3;
- не идёт осада;
- есть 100 000 динаров;
- целевая народность отличается от текущей;
- TOR bridge подтверждает полный набор необходимых cultural templates.

Перед подтверждением сделать отдельный save. После смены проверить:

- город/замок и связанные деревни;
- notables;
- recruits;
- tavern mercenaries;
- caravans;
- cultural services;
- save/load.

## Брачные предложения — игрок

v0.4.5 не строит собственную ветку courtship. Он восстанавливает штатный `MarriageModel`, поэтому должны работать стандартные Bannerlord behaviors.

Проверка:

1. поговорить с главой другого подходящего клана;
2. открыть обычный раздел дипломатических/семейных предложений;
3. найти стандартное предложение брачного союза;
4. выбрать подходящего члена своего клана и кандидата другого клана;
5. должна открыться штатная механика `MarriageBarterable`/barter;
6. отказаться — разговор должен завершиться без краша;
7. принять на отдельном test-save — брак должен оформиться стандартной игрой;
8. выйти на карту и промотать минимум 3–7 дней;
9. сохранить/перезагрузить минимум два раза.

## Брачные предложения — ИИ

Штатный `MarriageOfferCampaignBehavior` должен снова иметь возможность присылать игроку предложения о браке для членов его клана.

Проверить при промотке времени:

- предложение приходит через стандартное уведомление Bannerlord;
- отказ не ломает campaign map;
- принятие создаёт обычный marriage;
- после принятия карта продолжает работать;
- повторный save/load не повреждает сейв.

Беременность, естественная смерть и Dawi women в этой версии НЕ включены. Dawi остаются SAFE-OFF по женской модели/беременности.

## Набор существующих кланов правителями

AI ruler при дефиците noble clans сначала использует только штатный `JoinKingdomAsClanBarterable`.

Ограничение: максимум один успешный такой набор за world week.

Для проверки гномов выполнить:

```text
kaitor_diplomacy.dynasty_status
```

Смотреть `settlements / clans / target / deficit`.

## Новые кадетские кланы из существующего дома

Функция НЕ удалена.

Безопасная схема v0.4.5:

1. weekly tick только выбирает кандидата и записывает primitive IDs;
2. создание клана на weekly tick запрещено;
3. требуется deficit >= 2;
4. founder — уже существующий взрослый lord правящего клана;
5. founder не женат, без детей, не пленник, не governor и не имеет собственной party;
6. правящий клан должен сохранить минимум одно укреплённое поселение;
7. ruler должен иметь >= 50 000 золота;
8. pending request ждёт минимум 1 campaign day;
9. фактическое создание выполняется только после безопасного входа игрока в город/замок;
10. одновременно существует максимум одна pending-заявка на весь мир;
11. global cooldown = 42 дня;
12. cooldown одного kingdom = 84 дня.

После появления pending-заявки:

- подождать минимум сутки;
- войти в любой город/замок;
- проверить `dynasty_status`;
- проверить новый clan в kingdom screen;
- убедиться, что founder действительно вышел из старого ruling clan и стал главой нового;
- проверить передачу одного fief;
- промотать минимум 7 дней;
- save/load минимум два раза.

## Критическая стабильность

После каждого из трёх блоков — marriage, nationality, cadet house — проверить:

- обычное передвижение по карте;
- Daily Tick;
- Weekly Tick;
- разговор с другим lord;
- barter;
- town entry/exit;
- save/load.

## Что прислать при краше

- точное действие непосредственно перед падением;
- `crash_tags.txt`;
- свежий `rgl_log_*.txt`;
- `watchdog_log_*.txt`, если есть;
- вывод:

```text
kaitor_diplomacy.status
kaitor_diplomacy.marriage_status
kaitor_diplomacy.dynasty_status
```

- если проблема в городе: `kaitor_diplomacy.settlement`.
