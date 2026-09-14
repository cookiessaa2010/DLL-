# KaiTOR Diplomacy v0.1.0 — первый live-test

Цель первого прогона: доказать, что отдельный мод загружается поверх TOR 1.3.15, не заменяет модели TOR и корректно сохраняет собственное состояние.

## Требования

- Mount & Blade II: Bannerlord `1.3.15.110062`.
- The Old Realms `v1.3.15`.
- Порядок модулей: `Native -> SandBoxCore -> Sandbox -> TOR_Armory -> TOR_Environment -> TOR_Core -> KaiTOR_Diplomacy`.
- На первый тест не подключать KaiTOR Online/Coop: дипломатия тестируется отдельно.

## Сборка и установка

```powershell
.\Build-KaiTORDiplomacy.ps1 `
  -BannerlordRoot "C:\PATH\TO\Mount & Blade II Bannerlord" `
  -Install
```

Ожидаемая строка:

```text
KaiTOR Diplomacy build: PASS
```

До запуска игры выполнить:

```powershell
.\tests\Test-KaiTORDiplomacyContracts.ps1
```

Ожидается:

```text
KaiTOR Diplomacy contract tests: PASS
```

## Проверка запуска

Запустить TOR-кампанию. После загрузки должна появиться строка:

```text
KaiTOR Diplomacy: TOR 1.3.15 compatibility gate PASS.
```

Если мод пишет `disabled`, тест остановить и сохранить точный текст причины.

## Smoke-команды

```text
kaitor_diplomacy.status
kaitor_diplomacy.kingdoms
```

`status` должен показать PASS, `kingdoms` — реальные StringId активных королевств текущей TOR-кампании.

## Тест пакта о ненападении

Выбрать два невоюющих королевства, которые TOR разрешает как дипломатически совместимые:

```text
kaitor_diplomacy.nap <kingdomA> <kingdomB> 30
kaitor_diplomacy.status
```

Ожидается NAP на 30 дней.

Затем:

1. сохранить игру;
2. выйти в главное меню/из игры;
3. загрузить тот же save;
4. снова выполнить `kaitor_diplomacy.status`.

Пакт должен сохраниться, а оставшийся срок не должен сброситься на 30 дней.

## TOR lore-gate

Попробовать создать NAP для пары, которую `TORKingdomDecisionPermissionModel` запрещает (например, Chaos или несовместимая по TOR религия пара, если такая доступна в текущей кампании).

Ожидается `NAP refused` с причиной TOR. Мод не должен обходить это ограничение.

## Война во время NAP

Если война между сторонами начинается штатной логикой TOR или тестовым способом:

- NAP удаляется;
- выводится сообщение о нарушении;
- KaiTOR Diplomacy не отменяет и не переопределяет саму войну;
- TOR продолжает вести alliance-war/Chaos/peace логику самостоятельно.

После войны:

```text
kaitor_diplomacy.status
```

Пакта уже быть не должно.

## Регрессии TOR

Обязательно проверить, что без изменений работают:

- обычное объявление войны TOR;
- мир TOR;
- запрет мира с Chaos;
- TOR alliances и вызов союзника в войну;
- TOR trade agreements;
- отсутствие vanilla marriage (до отдельного marriage-этапа KaiTOR Diplomacy).

## Что прислать при ошибке

- скрин/текст ошибки;
- `rgl_log_*.txt` / relevant crash report;
- точную команду, после которой появилась ошибка;
- `kaitor_diplomacy.status`;
- какие два kingdom StringId использовались;
- новый save или существующий;
- стадия: startup / create NAP / save / load / war / TOR alliance / TOR trade.
