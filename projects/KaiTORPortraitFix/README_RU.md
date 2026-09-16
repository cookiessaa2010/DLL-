# KaiTOR Portrait Fix 0.6.0 Stable

Стабильный фикс UI-портретов для **Mount & Blade II: Bannerlord 1.3.15.110062 + The Old Realms 1.3.15**.

Версия 0.6.0 объединяет два отдельно подтверждённых исправления:

1. **Чёрный силуэт в “Сохранённых кампаниях”** — Bannerlord при module discrepancy очищает `SavedGameVM.MainHeroVisualCode`, хотя visual code персонажа остаётся в metadata сейва. Мод возвращает этот код только в VM превью.
2. **Лежащий / горизонтальный персонаж в UI** — `ActionIndexCache.act_inventory_idle_start` и `ActionIndexCache.act_inventory_idle` могут инициализироваться слишком рано и остаться равными `-1`, тогда как поздний live lookup уже возвращает корректные индексы. Мод восстанавливает только эти два статических action index перед использованием tableau.

## Что подтверждено тестом

На Bannerlord `1.3.15.110062` диагностическая 0.5.2 зафиксировала:

- `act_inventory_idle_start`: `before=-1`, live lookup `4014`, после repair `4014`;
- `act_inventory_idle`: `before=-1`, live lookup `4216`, после repair `4216`;
- обе записи были перечитаны и подтверждены после reflection write;
- после восстановления персонажи снова отображались вертикально в Save/Load, окне навыков и интерфейсе отряда;
- корректно отображались как человеческие, так и TOR-персонажи других рас, включая орка.

## Что делает стабильная 0.6.0

- сохраняет проверенный `SavedGameVM` preview restore;
- проверяет готовность `MBAnimation` до обращения к ремонтируемым static action values;
- восстанавливает **только** `act_inventory_idle_start` и `act_inventory_idle`, если они равны `-1`, а live lookup уже валиден;
- не перезаписывает здоровые action index;
- после reflection write перечитывает значение и считает repair успешным только при подтверждённой записи;
- содержит узкие runtime fallback для `CharacterTableau` и `BasicCharacterTableau`, если конкретный runtime откажется менять `static readonly`;
- ограничивает повторные попытки и диагностический лог;
- удаляет экспериментальные pose/gender патчи и тяжёлую reflection-диагностику предыдущих тестовых сборок.

## Чего мод НЕ делает

- не изменяет `.sav`;
- не меняет race, body properties, equipment или skeleton персонажа;
- не отключает предупреждения Bannerlord о несовпадении модулей;
- не обходит проверки загрузки сохранений;
- не меняет TOR XML/ресурсы;
- не влияет на бой или campaign logic.

## Установка

Распакуйте архив **в корень Bannerlord**.

После установки должен существовать файл:

`Modules\KaiTOR_PortraitFix\bin\Win64_Shipping_Client\KaiTORPortraitFix.dll`

Если Windows заблокировал DLL, выполните PowerShell:

```powershell
Unblock-File "D:\steam\steamapps\common\Mount & Blade II Bannerlord\Modules\KaiTOR_PortraitFix\bin\Win64_Shipping_Client\KaiTORPortraitFix.dll"
```

Путь к игре при необходимости замените на свой.

Рекомендуемый порядок:

```text
Bannerlord.Harmony
Native
SandBoxCore
BirthAndDeath
Sandbox
CustomBattle
TOR_Armory
TOR_Environment
TOR_Core
KaiCleave
KaiTOR_Stability
KaiTOR_PortraitFix
TOR_RU_Translation
```

## Лог

`%LOCALAPPDATA%\KaiTORPortraitFix\KaiTORPortraitFix.log`

Основные строки стабильной версии:

```text
SESSION_START
PATCH_APPLY
SAVE_VM_FIX
ACTION_CACHE_FIELD
ACTION_CACHE_REPAIR
ACTION_CACHE_FALLBACK
COLD_MENU_READY
```

`ACTION_CACHE_FALLBACK` обычно не нужен: в подтверждённом тесте прямое восстановление static readonly сработало и было проверено перечитыванием.
