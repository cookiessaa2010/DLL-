# KaiTOR Diplomacy v0.2.0 — совместимость сохранений

## Цель

KaiTOR Diplomacy должен расширять TOR 1.3.15, не создавая собственную копию мира TOR и не превращая сохранение в набор объектов, которые можно прочитать только конкретной сборкой KaiTOR.

## Что хранит KaiTOR

KaiTOR сохраняет только собственное дипломатическое состояние через стабильные `SyncData`-ключи:

- `kaitor_diplomacy_nap_expiry_days_v2` — срок NAP;
- `kaitor_diplomacy_breach_counts` — нарушения договоров;
- `kaitor_diplomacy_trust` — доверие;
- `kaitor_diplomacy_nap_cooldown_expiry_days` — дипломатический cooldown;
- `kaitor_diplomacy_save_schema` — версия схемы KaiTOR.

Значения состоят только из `Dictionary<string, int/double>` и `int`. KaiTOR не вводит `SaveableTypeDefiner`, `SaveableField` или `SaveableProperty` для объектов кампании.

## Что остаётся собственностью TOR/Bannerlord

KaiTOR не сериализует собственные копии `Settlement`, `Hero`, `Clan`, `Kingdom`, `CultureObject`, сервисных NPC или рыночных данных.

При полной смене культуры KaiTOR меняет штатное `Settlement.Culture`. TOR `AssimilationCampaignBehavior` перед сохранением сам записывает фактическую культуру поселений в свой `_settlementCulturePairs`, поэтому культура сохраняется штатным механизмом TOR.

Карты spell trainer / enchanter / bounty master и другие TOR-сервисы меняются через уже существующие TOR behavior и их коллекции. KaiTOR не создаёт параллельные таблицы этих NPC в своём save state.

## Версионирование

Текущая схема KaiTOR: `1`.

Сейвы, созданные до появления `kaitor_diplomacy_save_schema`, считаются schema `0`. Их миграция в schema `1` является metadata-only: существующие четыре словаря NAP/trust/breach/cooldown не меняют тип и ключи.

Если save имеет номер схемы выше поддерживаемого текущей DLL, KaiTOR отключает свой runtime и не пытается понизить или переписать данные. Это fail-closed защита от загрузки нового save старой версией мода.

## Нормализация при загрузке

После загрузки KaiTOR:

- удаляет malformed treaty keys;
- удаляет пары, ссылающиеся на отсутствующие в кампании kingdom StringId;
- удаляет NaN/Infinity/отрицательные значения времени;
- удаляет отрицательные breach counts;
- ограничивает trust диапазоном `-100..100`.

Это применяется только к KaiTOR-словарям. TOR-owned campaign state не переписывается.

## Обязательный live-test перед релизом

1. Загрузить существующий TOR save, созданный без KaiTOR. Убедиться, что кампания открывается и `kaitor_diplomacy.save_status` показывает `PASS`.
2. Создать NAP, сохранить игру, полностью выйти, загрузить save и проверить NAP/trust/breach/cooldown.
3. Конвертировать город другой культуры. Проверить город, связанные деревни, нотаблей, рекрутов, таверну, spell trainer, enchanter/alchemist, Empire Bounty Master или Greenskin Kwartamasta где применимо, рынок и караваны.
4. Сохранить сразу после конверсии, полностью выйти из игры, загрузить этот save. Повторно проверить культуру поселения и сервисы.
5. Сделать второй save после загрузки, затем загрузить второй save ещё раз. Это проверяет не только load, но и load -> save -> load.
6. Провести минимум 7 campaign days и повторить save/load, чтобы TOR daily/weekly behaviors не создали дубликаты сервисных NPC.
7. Для Greenskin проверить, что Kwartamasta ровно один на соответствующий fief. Для Empire town — не более одного Bounty Master.
8. Проверить Nuln/Altdorf/Lithanel/Karak/fixed priest/shrine контент: landmark NPC не должен клонироваться в обычный перекультуренный город.

## Граница гарантии

Целевая матрица релиза: Bannerlord `1.3.15.110062` + TOR `v1.3.15` + KaiTOR Diplomacy `v0.2.0`.

Совместимость save после обновления самого TOR на другую версию не объявляется автоматически: сначала должен пройти compatibility gate и отдельный save/load regression test. Удаление KaiTOR из кампании после того, как в save появились его SyncData-записи, также не считается поддержанным сценарием без отдельного теста.
