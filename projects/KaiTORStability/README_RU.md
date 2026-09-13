# KaiTOR Stability 0.4.1 — тестовая версия

Отдельный compatibility/stability-мод для **Mount & Blade II: Bannerlord 1.3.15.110062 + The Old Realms 1.3.15**.

Цель проекта — не менять баланс TOR, а уменьшать известные горячие участки, ускорять тяжёлые shader/cache-процессы и делать долгую загрузку измеримой.

## Что уже делает мод

### 1. KaiTOR Load Monitor

`Tools\KaiTORLoadMonitor.exe` запускает обычный TaleWorlds Launcher и остаётся отдельным отзывчивым окном, даже если окно Bannerlord временно выглядит зависшим.

Монитор показывает:

- текущую стадию запуска;
- **общий прогресс в процентах**;
- **отдельный процент текущей shader-compilation wave**, когда движок уже сообщает очередь;
- полное время загрузки от запуска Monitor до готового главного экрана;
- ETA запуска по истории этого ПК;
- ETA шейдеров по фактической скорости уменьшения engine queue;
- CPU/RAM Bannerlord и состояние окна;
- статус battle optimization;
- фактический путь shader cache.

До появления реального shader counter общий процент помечается `~`, потому что это стадийная оценка, а не выдуманное точное значение. После появления очереди компиляции отдельный shader-процент считается из реального количества оставшихся задач текущей волны.

Во время **первой загрузки до главного экрана** Monitor временно переводит процесс Bannerlord из Normal/BelowNormal в **AboveNormal**. Affinity и native shader compiler threads не меняются. После готовности главного экрана или закрытия Monitor исходный priority восстанавливается.

### 2. First-load shader-source prestage

Версия 0.4.1 включает ускоритель стартовой загрузки.

TOR при `OnSubModuleLoad` синхронно вызывает `ShaderSourceManager.CopyShaderSourcesToGame()`: проверяет `.rs/.rsh` из `TOR_Armory/Shaders/Sources` и при необходимости копирует их в базовый `Bannerlord/Shaders/Sources`.

KaiTOR Load Monitor делает совместимую проверку **до запуска Bannerlord**:

1. находит `TOR_Armory` в обычной `Modules` или в Steam Workshop текущей библиотеки;
2. перечисляет `.rs/.rsh`;
3. использует то же безопасное правило обновления, что TOR: отсутствует файл, отличается размер или source новее target;
4. заранее переносит только нужные файлы;
5. читает итоговые target-файлы с `SequentialScan`, чтобы они уже находились в файловом кэше Windows перед стартом движка;
6. показывает подготовку игроку в процентах и по количеству файлов.

После этого штатный TOR-проход должен увидеть уже актуальные shader sources и не тратить время на их повторное копирование. Если prestage не может найти TOR_Armory или записать target, он просто отступает: игра продолжает запуск и TOR выполняет оригинальную процедуру.

События для анализа:

```text
PRELAUNCH_START
SHADER_SOURCE_PRESTAGE
LOAD_COMPLETE
```

`SHADER_SOURCE_PRESTAGE` содержит `total/copy/skipped/warmed/errors/ms`, поэтому можно отдельно измерить стоимость подготовки.

### 3. Совместимость с обычным shader cache и Kai Shader Cache Redirector

Monitor автоматически различает два режима:

1. стандартный Bannerlord:

```text
C:\ProgramData\Mount and Blade II Bannerlord\Shaders
```

2. нашу ранее сделанную модификацию **Bannerlord Shader Cache Redirector**, для которой из проверенной сигнатуры `TaleWorlds.Native.dll` читается фактический путь, например:

```text
D:\MNB\Shaders
```

First-load prestage работает с **shader source files** в каталоге игры, а не с compiled cache, поэтому одинаково совместим и со стандартным cache, и с Redirector. Battle optimization, telemetry и Build Shader Cache accelerator также не требуют возвращать cache на C:.

### 4. Совместимость с KaiTOR Online / KaiCoop

Bannerlord Coop сравнивает community-модули сервера и клиентов в обе стороны, включая **Id и Version**. Поэтому если `KaiTOR_Stability` включён, версия должна быть одинаковой на авторитетном campaign-process сервере и у всех подключающихся клиентов.

Рекомендуемый порядок:

```text
TOR_Armory
TOR_Environment
TOR_Core
KaiTOR_Stability
Coop
```

`Coop` остаётся последним.

Чтобы не вносить дополнительную разницу в сетевую симуляцию до отдельного live-теста, 0.4.1 автоматически включает защитный режим: если активен мод `Coop`, замена `TOR StatusEffectMissionLogic` не устанавливается. При этом остаются активны локальные функции загрузки и шейдеров: Monitor, проценты/ETA, telemetry, prestage и Shader Cache Accelerator.

Конфиг:

```xml
<CoopCompatibility disableGameplayPatches="true" />
```

Оставлять `true` до отдельной сетевой проверки battle optimization.

Kai Shader Cache Redirector не является Bannerlord community-модулем и не участвует в module handshake Coop: он меняет только локальный путь compiled shader cache. Поэтому разные ПК могут использовать стандартный путь или Redirector независимо друг от друга.

### 5. Реальная shader telemetry

Мод раз в 500 мс опрашивает engine counter:

```text
TaleWorlds.Engine.Utilities.GetNumberOfShaderCompilationsInProgress()
```

В лог пишутся изменения очереди и редкий heartbeat:

```text
SHADER_START
SHADER_PROGRESS
SHADER_COMPLETE
```

Если движок добавляет новую волну, расчёт скорости/ETA текущей волны сбрасывается, чтобы не смешивать разные очереди.

### 6. Shader Cache Accelerator — phase 1

Официальный TOR `Build Shader Cache` формирует специальный custom-battle roster. В текущем TOR каждый обычный soldier добавляется 4 раза даже при одном battle-equipment варианте.

KaiTOR Stability применяет консервативное правило:

- soldier с 0/1 battle-equipment вариантом: 1 копия вместо 4;
- soldier с 2+ вариантами: сохраняются оригинальные TOR-копии;
- heroes/non-soldiers не урезаются;
- native shader compiler и формат cache не изменяются.

В лог пишется фактическая экономия:

```text
SHADER_CACHE_ROSTER|original=...; optimized=...; saved=...; uniqueCharacters=...; collapsedSingleLoadoutTroops=...; preservedMultiLoadoutTroops=...
```

Отключение:

```xml
<ShaderCacheAcceleration enabled="false" singleLoadoutCopies="1" />
```

## Что дальше для первой загрузки

0.4.1 ускоряет безопасную часть до старта движка и делает прогресс видимым. Основное тяжёлое время первой установки всё равно может уходить на native shader compilation.

Следующие направления тестируются только после замеров 0.4.1:

- определить долю времени до `MODULE_LOAD`, внутри shader queue и после неё;
- профилировать, насколько first-load ограничен CPU, диском или native compiler;
- подготовить корректный **module-level precompiled shader cache** через официальный Bannerlord Modding Kit и проверить cold start на чистой установке;
- после подтверждения совместимости сравнить его со стандартной локальной компиляцией;
- для `Build Shader Cache` перейти к управляемым пакетам персонажей/equipment вместо одной огромной сцены, не теряя shader combinations.

Мы не раздаём случайный локальный `%ProgramData%` cache одного ПК. Для distribution нужен именно корректно созданный module shader cache под точную версию игры/TOR.

## Battle Status optimization

Оригинальный `TOR_Core.BattleMechanics.StatusEffect.StatusEffectMissionLogic` часто просматривает всех агентов. Replacement уменьшает частоту полного поиска, но уже найденные активные status-effect components продолжает обновлять каждый кадр.

По умолчанию:

```text
100 ms
```

Конфиг:

```text
Modules\KaiTOR_Stability\ModuleData\KaiTORStability.config.xml
```

Быстрый откат:

```xml
<BattleStatusOptimization enabled="false" fullRescanIntervalMs="100" />
```

Shader telemetry:

```xml
<ShaderTelemetry enabled="false" sampleIntervalMs="500" />
```

При активном `Coop` battle replacement по умолчанию автоматически пропускается. При несовпадении runtime API TOR replacement также не устанавливается и оригинальная логика остаётся активной.

## Установка

Скопировать:

```text
Modules\KaiTOR_Stability
```

в Bannerlord `Modules` и включить **KaiTOR Stability после TOR_Core**.

Для KaiTOR Online порядок должен быть:

```text
TOR_Core -> KaiTOR_Stability -> Coop
```

и та же версия `KaiTOR_Stability` должна быть активна на сервере и всех клиентах.

Для измерения и first-load optimization запускать через:

```text
Tools\KaiTORLoadMonitor.exe
```

При необходимости путь можно указать явно:

```text
KaiTORLoadMonitor.exe --game-root "D:\steam\steamapps\common\Mount & Blade II Bannerlord"
```

## Лог

```text
%LOCALAPPDATA%\KaiTORStability\KaiTORStability.log
```

Основные события:

```text
PRELAUNCH_START
SHADER_SOURCE_PRESTAGE
SESSION_START
MODULE_LOAD
COOP_COMPAT_ACTIVE
SHADER_ACCELERATOR_READY
SHADER_CACHE_ROSTER
SHADER_ACCELERATOR_FALLBACK
SHADER_START
SHADER_PROGRESS
SHADER_COMPLETE
SHADER_TELEMETRY_ERROR
OPTIMIZATION_ACTIVE
OPTIMIZATION_SKIP
OPTIMIZATION_FALLBACK
INITIAL_SCREEN_READY
LOAD_COMPLETE
```

## Что измерять на первом запуске

Для полезного сравнения сохранять один лог целиком. Из него можно получить:

- длительность prestage;
- сколько shader-source файлов реально пришлось копировать;
- время до загрузки нашего module;
- начало/конец shader compilation;
- скорость уменьшения очереди;
- общую длительность до главного экрана;
- статус battle/shader acceleration;
- был ли активирован Coop compatibility guard.

## Безопасность изменений

- оригинальный `TOR_Core.dll` не заменяется;
- TaleWorlds DLL не входят в пакет;
- `0Harmony.dll` не дублируется;
- first-load prestage копирует только `.rs/.rsh` по тому же правилу актуальности, что TOR;
- при ошибке prestage запуск не блокируется;
- KaiTOR Stability не применяет и не удаляет Shader Cache Redirector;
- startup priority boost откатывается;
- сохранения не переписываются;
- при активном Coop gameplay replacement отключён по умолчанию;
- при несовместимом TOR API оптимизации fail-open и оставляют оригинальную логику.
