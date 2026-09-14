# KaiTOR Portrait Fix 0.1.0

Тестовый узконаправленный фикс чёрного силуэта персонажа на экране **Сохранённые кампании / Load Game** для Bannerlord `1.3.15.110062`.

## Что именно патчим

Экран Save/Load использует `TaleWorlds.MountAndBlade.View.Tableaus.BasicCharacterTableau`, а не тот `CharacterTableau`, которым игра пользуется в кампании и инвентаре. Поэтому персонаж может нормально отображаться в игре, но быть чёрным силуэтом только в списке сохранений.

В известных затронутых версиях `BasicCharacterTableau.RefreshCharacterTableau` передаёт временный age-derived boolean в skin-generation там, где должен использовать реальное поле `_isFemale`. Патч заменяет только этот один IL-аргумент на `this._isFemale`.

Паттерн основан на публичном upstream-исправлении CharacterCreation для `BasicCharacterTableau.RefreshCharacterTableau`; здесь он вынесен в отдельный минимальный мод для Bannerlord 1.3.15/TOR.

## Безопасность / границы

- патч применяется при `OnSubModuleLoad`, то есть до первого рендера cold main menu;
- затрагивается только `BasicCharacterTableau.RefreshCharacterTableau`;
- никакие сейвы не редактируются;
- бой, карта, инвентарь, персонаж в кампании и TOR-логика не изменяются;
- если ожидаемый IL-паттерн не найден, мод **ничего не меняет** и пишет это в лог;
- KaiCleave и KaiTOR Stability не требуются для работы этого фикса.

## Лог

`%LOCALAPPDATA%\KaiTORPortraitFix\KaiTORPortraitFix.log`

Ищем:

- `PATCH_PATTERN|applied=true; fix=save-preview-gender`
- `PATCH_APPLY|success=true`
- `PREVIEW_REFRESH` при открытии списка сохранений.

Если силуэт останется, этот лог нужен для следующего прохода: тогда будем диагностировать материал/освещение уже на самом `BasicCharacterTableau`, не трогая сейв.

## Установка

Распаковать архив в корень `Mount & Blade II Bannerlord` так, чтобы существовал:

`Modules\KaiTOR_PortraitFix\bin\Win64_Shipping_Client\KaiTORPortraitFix.dll`

После распаковки выполнить `Unblock-File` для DLL, затем включить `KaiTOR Portrait Fix` в лаунчере после `Native`/Harmony. TOR может идти после/до него — прямой зависимости от TOR нет.
