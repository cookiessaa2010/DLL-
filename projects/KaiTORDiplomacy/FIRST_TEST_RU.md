# KaiTOR Diplomacy v0.1.0 — первый live-test

Цель первого прогона: доказать, что отдельный мод загружается поверх TOR 1.3.15, не заменяет модели TOR и корректно сохраняет собственное дипломатическое состояние.

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

После установки выполнить полный preflight:

```powershell
.\Test-KaiTORDiplomacyReadiness.ps1 `
  -BannerlordRoot "C:\PATH\TO\Mount & Blade II Bannerlord"
```

Если TOR Workshop лежит в другой Steam-библиотеке, добавить:

```text
-WorkshopRoot "D:\steam\steamapps\workshop\content\261550"
```

Финальная ожидаемая строка:

```text
KaiTOR Diplomacy readiness: PASS
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
kaitor_diplomacy.ledger
```

`status` должен показать PASS, `kingdoms` — реальные StringId активных королевств текущей TOR-кампании. На чистом save `ledger` может быть пустым.

## Тест пакта о ненападении

Выбрать два невоюющих королевства, которые TOR разрешает как дипломатически совместимые:

```text
kaitor_diplomacy.nap <kingdomA> <kingdomB> 30
kaitor_diplomacy.inspect <kingdomA> <kingdomB>
kaitor_diplomacy.status
```

Ожидается NAP на 30 дней, trust `0`, breaches `0`, cooldown `0`.

Затем:

1. сохранить игру;
2. выйти в главное меню/из игры;
3. загрузить тот же save;
4. снова выполнить `kaitor_diplomacy.inspect <kingdomA> <kingdomB>`.

Пакт должен сохраниться, а оставшийся срок не должен сброситься на 30 дней.

## Естественное завершение NAP

Дождаться завершения срока договора без войны и без ручного разрыва. После следующего campaign daily tick:

```text
kaitor_diplomacy.inspect <kingdomA> <kingdomB>
kaitor_diplomacy.ledger
```

Ожидается:

- NAP отсутствует;
- trust увеличился ровно на `+5`;
- breaches не изменились;
- cooldown отсутствует.

## Ручной досрочный разрыв

Создать новый допустимый NAP и выполнить:

```text
kaitor_diplomacy.break_nap <kingdomA> <kingdomB>
kaitor_diplomacy.inspect <kingdomA> <kingdomB>
```

Ожидается:

- NAP удалён;
- trust уменьшается на `10`;
- новый NAP заблокирован на `10` campaign days;
- немедленная попытка `kaitor_diplomacy.nap ...` получает `NAP refused`.

## TOR lore-gate

Попробовать создать NAP для пары, которую `TORKingdomDecisionPermissionModel` запрещает (например, Chaos или несовместимая по TOR религия пара, если такая доступна в текущей кампании).

Ожидается `NAP refused` с причиной TOR. Мод не должен обходить это ограничение.

## Война во время NAP

Если война между сторонами начинается штатной логикой TOR или тестовым способом:

- NAP удаляется;
- breach count увеличивается на `1`;
- trust уменьшается на `30`;
- новый NAP блокируется на `30` campaign days;
- выводится сообщение о нарушении;
- KaiTOR Diplomacy не отменяет и не переопределяет саму войну;
- TOR продолжает вести alliance-war/Chaos/peace логику самостоятельно.

После войны:

```text
kaitor_diplomacy.inspect <kingdomA> <kingdomB>
kaitor_diplomacy.ledger
```

Пакта уже быть не должно, а breach/trust/cooldown должны сохраниться после save/load.

## Проверка read-only diagnostics

Несколько раз подряд выполнить:

```text
kaitor_diplomacy.status
kaitor_diplomacy.inspect <kingdomA> <kingdomB>
kaitor_diplomacy.ledger
```

Значения trust, breaches, срок NAP и cooldown не должны меняться только из-за просмотра состояния.

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
- `kaitor_diplomacy.inspect <kingdomA> <kingdomB>`;
- какие два kingdom StringId использовались;
- новый save или существующий;
- стадия: startup / create NAP / save / load / expiry / manual break / war / TOR alliance / TOR trade.
