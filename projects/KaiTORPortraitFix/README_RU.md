# KaiTOR Portrait Fix 0.6.3 — Save Preview Only RC4

Тестовая сборка для **Mount & Blade II: Bannerlord 1.3.15.110062 + The Old Realms 1.3.15**.

## Цель RC4

Исправить пустой/чёрный портрет героя в меню Save/Load, не вмешиваясь в 3D-модели персонажей.

Live-тест показал, что без KaiTOR Portrait Fix модели TOR отображаются нормально. Поэтому из RC4 полностью удалены все pose/action исправления.

## Что осталось

Только Harmony postfix на конструкторы `SavedGameVM`:

- если `MainHeroVisualCode` уже заполнен — мод ничего не делает;
- если сейв повреждён — мод ничего не делает;
- если visual code отсутствует, мод читает уже сохранённый `GetCharacterVisualCode()` из metadata и возвращает его только в VM превью;
- save-файл не изменяется.

## Что полностью удалено

- `CharacterTableau` и `BasicCharacterTableau` patches;
- любые записи в `ActionIndexCache`;
- `act_inventory_idle` / `act_inventory_idle_start` manipulation;
- `SetAgentActionChannel`;
- skeleton/pose runtime calls;
- Character Creation hooks;
- FaceGen / BodyProperties / race / gender / age changes;
- старые диагностические pose-патчи.

## Версия

- Launcher: `v1.3.15.63`
- Assembly/File: `0.6.3.0`
- Scope: Save preview only

## Live-test

1. Запустить новую кампанию и пройти Character Creation.
2. Проверить Banner Editor и Clan Name.
3. В кампании проверить Character / Inventory / Party / Clan.
4. Создать сейв.
5. Открыть Save/Load и проверить портрет.
6. Перезагрузить сейв.

Лог:

`%LOCALAPPDATA%\KaiTORPortraitFix\KaiTORPortraitFix.log`

Ожидаемые события:

- `SESSION_START|KaiTOR Portrait Fix 0.6.3-savepreview-only`
- `PATCH_APPLY|success=true`
- `SAVE_VM_FIX`
