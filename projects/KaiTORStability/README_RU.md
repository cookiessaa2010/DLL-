# KaiTOR Stability 0.2.0 — тестовая версия

Отдельный compatibility/stability-мод для **Mount & Blade II: Bannerlord 1.3.15.110062 + The Old Realms 1.3.15**.

Цель проекта — не менять баланс TOR, а уменьшать известные горячие участки, делать долгую загрузку понятной и собирать данные о зависаниях/компиляции шейдеров.

## Что уже делает мод

### 1. KaiTOR Load Monitor

`Tools\KaiTORLoadMonitor.exe` запускает обычный TaleWorlds Launcher и остаётся отдельным отзывчивым окном, даже если окно Bannerlord временно выглядит зависшим.

Монитор показывает:

- текущую стадию запуска;
- прошедшее время;
- адаптивный ETA полного запуска по медиане последних 5 успешных запусков;
- CPU/RAM Bannerlord и состояние окна;
- реальное число незавершённых shader compilations, когда движок Bannerlord уже доступен;
- ETA компиляции шейдеров по фактической скорости уменьшения очереди;
- статус battle optimization;
- фактический путь shader cache.

Прогресс не рисует фиктивные 99%: до появления реальных событий TOR используется только ограниченная оценка по истории конкретного ПК.

### 2. Совместимость с обычным shader cache и Kai Shader Cache Redirector

Monitor автоматически различает два поддерживаемых режима:

1. стандартный Bannerlord 1.3.15.110062:

```text
C:\ProgramData\Mount and Blade II Bannerlord\Shaders
```

2. нашу ранее сделанную модификацию **Bannerlord Shader Cache Redirector**. Для неё проверяется именно сигнатура нашего патча в `TaleWorlds.Native.dll`, после чего из DLL читается реальный путь, например:

```text
D:\MNB\Shaders
```

Поэтому **KaiTOR Stability не требует возвращать shader cache на C:**. Battle optimization и shader telemetry работают одинаково в обоих вариантах. Redirector остаётся отдельным необязательным патчем — KaiTOR Stability сам native DLL не изменяет.

Если `TaleWorlds.Native.dll` не совпадает ни с оригинальным SHA-256 версии 1.3.15.110062, ни с сигнатурой нашего redirector, монитор не пытается угадывать чужую модификацию и показывает предупреждающее описание.

### 3. Реальная shader telemetry

Мод раз в 500 мс опрашивает официальный engine counter:

```text
TaleWorlds.Engine.Utilities.GetNumberOfShaderCompilationsInProgress()
```

В лог пишутся только изменения очереди и редкий heartbeat, поэтому telemetry не должна создавать заметную дополнительную нагрузку. Load Monitor читает лог инкрементально, а не перечитывает весь файл каждые полсекунды.

Если движок добавляет новую волну компиляции, ETA автоматически начинает измеряться заново, чтобы не смешивать две разные скорости.

## Что мы НЕ делаем с shader compiler

Bannerlord 1.3.15 предоставляет managed API для проверки состояния и числа текущих shader compilations, но безопасного публичного API для изменения числа native compiler threads мы не нашли. Поэтому версия 0.2.0 **не вмешивается в native scheduler компилятора** и не обещает искусственного ускорения в несколько раз.

Это сознательно: неправильное распараллеливание shader compiler потенциально опаснее, чем длинная первая загрузка.

TOR сам копирует `.rs/.rsh` из `TOR_Armory` только когда целевой файл отсутствует или выглядит устаревшим, а официальный TOR `Build Shader Cache` специально загружает большую custom-battle сцену с уникальными войсками/NPC, чтобы прогреть локальный cache заранее.

Следующий этап оптимизации — измерить на реальных ПК, где находится узкое место: CPU shader compiler, диск/cache path, RAM/pagefile или синхронная managed-инициализация TOR. После этого можно оптимизировать конкретный участок, а не отключать проверки вслепую.

## Battle Status optimization

Оригинальный `TOR_Core.BattleMechanics.StatusEffect.StatusEffectMissionLogic` просматривает всех агентов очень часто. Тестовый replacement уменьшает частоту полного поиска агентов, но уже найденные активные status-effect components продолжает обновлять каждый кадр.

По умолчанию полный повторный поиск:

```text
100 ms
```

Конфиг:

```text
Modules\KaiTOR_Stability\ModuleData\KaiTORStability.config.xml
```

Быстрый откат только battle optimization:

```xml
<BattleStatusOptimization enabled="false" fullRescanIntervalMs="100" />
```

Отключение только shader telemetry:

```xml
<ShaderTelemetry enabled="false" sampleIntervalMs="500" />
```

Если runtime API TOR не совпадает с ожидаемым контрактом, replacement не ставится и оригинальная TOR logic остаётся активной.

## Установка

Скопировать:

```text
Modules\KaiTOR_Stability
```

в папку Bannerlord `Modules` и включить **KaiTOR Stability после TOR_Core** в launcher.

Для первого теста запускать через:

```text
Tools\KaiTORLoadMonitor.exe
```

Monitor сам ищет Steam libraries. При необходимости можно указать путь явно:

```text
KaiTORLoadMonitor.exe --game-root "D:\steam\steamapps\common\Mount & Blade II Bannerlord"
```

## Лог

```text
%LOCALAPPDATA%\KaiTORStability\KaiTORStability.log
```

Ключевые события:

```text
SESSION_START
MODULE_LOAD
SHADER_START
SHADER_PROGRESS
SHADER_COMPLETE
SHADER_TELEMETRY_ERROR
OPTIMIZATION_ACTIVE
OPTIMIZATION_FALLBACK
INITIAL_SCREEN_READY
```

## Как тестировать

Сначала сделать один обычный запуск через Load Monitor. Затем один небольшой бой и один крупный бой. Для shader-cache теста отдельно запустить TOR `Build Shader Cache` и оставить Monitor открытым: после старта engine telemetry он должен показывать фактический остаток компиляций и ETA.

После краша/фриза не очищать `%LOCALAPPDATA%\KaiTORStability` до анализа лога.

## Безопасность изменений

- оригинальный `TOR_Core.dll` не заменяется;
- оригинальные TaleWorlds DLL не входят в пакет;
- KaiTOR Stability не применяет и не удаляет Shader Cache Redirector;
- сохранения не переписываются модом;
- при несовместимом TOR API battle optimization fail-open и оставляет оригинальную логику.
