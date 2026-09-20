# KaiTOR Portrait Fix 0.6.1 Regression

Регрессионная сборка для **Mount & Blade II: Bannerlord 1.3.15.110062 + The Old Realms 1.3.15**.

## Почему понадобилась 0.6.1

Live-test 20.09.2026 выявил новый дефект 0.6.0: при активном KaiTOR Portrait Fix и отключённом KaiTOR Diplomacy персонаж в TOR Character Creation мог отображаться горизонтально/растянуто, а повторный вход в создание персонажа завершился нативным access violation.

0.6.0 глобально восстанавливал два static readonly значения `ActionIndexCache`. Это исправляло лежащие UI-превью в Save/Load, Skills и Party, но глобальная запись могла влиять на TOR Character Creation.

## Что изменено

- полностью удалена запись в static поля `ActionIndexCache`;
- `CharacterTableau.GetIdleAction` получает live fallback только для конкретного вызова и только если исходный idle index невалиден;
- `BasicCharacterTableau` получает live `act_inventory_idle` только на текущий preview skeleton;
- при активном `CharacterCreationScreen` pose-fallback полностью обходится;
- восстановление чёрного Save/Load preview через `SavedGameVM.MainHeroVisualCode` сохранено без изменений;
- мод по-прежнему не меняет race, BodyProperties, equipment, skin material или save-файл.

## Обязательный regression-test

1. Новая кампания -> Character Creation.
2. Вернуться в главное меню.
3. Снова начать новую кампанию и открыть Character Creation.
4. Проверить Save/Load preview.
5. Проверить Skills и Party preview.
6. Проверить Human / Dawi / Vampire / Greenskin.

## Версия

Лаунчер: `v1.3.15.61`.

## Лог

`%LOCALAPPDATA%\KaiTORPortraitFix\KaiTORPortraitFix.log`

Новые диагностические события:

- `CHARACTER_CREATION_BYPASS`
- `LOCAL_POSE_FALLBACK`
- `SAVE_VM_FIX`
