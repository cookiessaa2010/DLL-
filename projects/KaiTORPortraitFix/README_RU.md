# KaiTOR Portrait Fix 0.3.1

Минимальный фикс чёрного силуэта персонажа на экране **Сохранённые кампании / Load Game** для Bannerlord `1.3.15.110062` + TOR.

## Что нашли

Диагностика 0.2.0 показала: `SaveLoadHeroTableauTextureProvider` создаётся, но получает пустой `HeroVisualCode`. Значит проблема возникает **до рендера**. В штатном `SavedGameVM` Bannerlord очищает `MainHeroVisualCode`, когда считает, что у сохранения есть расхождение по модулям. Поэтому Save/Load tableau не получает описание героя и показывает пустой/чёрный preview, хотя сам персонаж после загрузки кампании исправен.

## Что делает 0.3.1

- патчит все фактически объявленные instance-конструкторы `SavedGameVM`, независимо от их точной сигнатуры;
- для **не повреждённого** сейва, если `MainHeroVisualCode` пустой, берёт character visual code из metadata сохранения и возвращает его только в VM превью;
- не изменяет файл сохранения;
- не отключает предупреждения о несовпадении модулей и не обходит проверки при загрузке;
- сохраняет диагностику Save/Load pipeline и узкий gender-fix `BasicCharacterTableau` из предыдущих тестов.

## Почему 0.3.1 вместо 0.3.0

В 0.3.0 Harmony не смог автоматически определить overload конструктора `SavedGameVM` (`Undefined target method`). 0.3.1 использует `HarmonyTargetMethods` и перечисляет все declared instance constructors через reflection, поэтому не зависит от точной сигнатуры конкретной сборки Bannerlord.

## Лог

`%LOCALAPPDATA%\KaiTORPortraitFix\KaiTORPortraitFix.log`

На успешном старте ожидаем:

- `SAVE_VM_TARGETS|constructors=<N>`
- `PATCH_APPLY|success=true`
- `COLD_MENU_READY|patchInstalled=True`

На проблемном сохранении ожидаем:

- `SAVE_VM|... discrepancy=True; vmCodeEmpty=True; metadataCodeEmpty=False ...`
- `SAVE_VM_FIX|applied=true; reason=restore-preview-code ...`
- `PIPE_HEROCODE|... empty=false ...`

## Установка

Распаковать архив в корень Bannerlord. Должен существовать:

`Modules\KaiTOR_PortraitFix\bin\Win64_Shipping_Client\KaiTORPortraitFix.dll`

После распаковки выполнить `Unblock-File` для DLL и включить мод в Launcher после Harmony/Native/Sandbox.
