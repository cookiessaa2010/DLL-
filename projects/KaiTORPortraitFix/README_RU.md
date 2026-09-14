# KaiTOR Portrait Fix 0.3.0

Фикс чёрного силуэта героя на экране **Сохранённые кампании / Load Game** для Bannerlord `1.3.15.110062` + TOR.

## Что нашли

Диагностика 0.2.0 показала, что `SaveLoadHeroTableauTextureProvider` действительно создаётся, но получает `HeroVisualCode = empty`. То есть проблема возникает **до рендера**: Bannerlord не передаёт в preview код внешности героя.

В `SavedGameVM` есть штатная логика: при `IsModuleDiscrepancyDetected` игра задаёт `MainHeroVisualCode = string.Empty`. Поэтому даже исправный сейв может показывать чёрный placeholder/силуэт, если движок видит расхождение модулей или версий.

## Что делает 0.3.0

- только для НЕ повреждённых сохранений восстанавливает `MainHeroVisualCode` из metadata сейва;
- не меняет файл сохранения;
- не отключает предупреждения/проверки совместимости при загрузке;
- не меняет список модулей сейва;
- сохраняет диагностику SaveLoad pipeline;
- сохраняет узкий gender-fix из 0.1.0.

Если сейв помечен как corrupted, код превью **не восстанавливается**.

## Лог

`%LOCALAPPDATA%\KaiTORPortraitFix\KaiTORPortraitFix.log`

Ключевые строки:

- `SAVE_VM|corrupted=...; discrepancy=...; vmCodeEmpty=...; metadataCodeEmpty=...`
- `SAVE_VM_FIX|applied=true; reason=restore-preview-code`
- затем ожидаем `PIPE_HEROCODE|... empty=false`
- `PIPE_DESERIALIZE_IN/OUT`
- `PREVIEW_REFRESH`

## Установка

Распаковать в корень Bannerlord. Должен существовать:

`Modules\KaiTOR_PortraitFix\bin\Win64_Shipping_Client\KaiTORPortraitFix.dll`

После распаковки выполнить `Unblock-File` для DLL и включить мод в Launcher после Harmony/Native/Sandbox.
