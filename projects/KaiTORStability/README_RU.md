# KaiTOR Stability 0.3.0 — тестовая версия

Отдельный compatibility/stability-мод для **Mount & Blade II: Bannerlord 1.3.15.110062 + The Old Realms 1.3.15**.

Цель проекта — не менять баланс TOR, а уменьшать известные горячие участки, ускорять тяжёлые shader/cache-процессы и делать долгую загрузку измеримой.

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

Поэтому **KaiTOR Stability не требует возвращать shader cache на C:**. Battle optimization, shader telemetry и shader-cache accelerator работают одинаково в обоих вариантах. Redirector остаётся отдельным необязательным патчем — KaiTOR Stability сам native DLL не изменяет.

### 3. Реальная shader telemetry

Мод раз в 500 мс опрашивает официальный engine counter:

```text
TaleWorlds.Engine.Utilities.GetNumberOfShaderCompilationsInProgress()
```

В лог пишутся только изменения очереди и редкий heartbeat. Если движок добавляет новую волну компиляции, ETA автоматически начинает измеряться заново.

### 4. Shader Cache Accelerator — phase 1

Официальный TOR `Build Shader Cache` формирует специальный custom-battle roster. В текущем TOR каждый обычный soldier добавляется **4 раза**, даже когда у него только один battle-equipment вариант.

KaiTOR Stability 0.3.0 патчит только приватный `TORShaderGameManager.GetPlayerParty()` и применяет консервативное правило:

- soldier с 0/1 battle-equipment вариантом: оставляем 1 копию вместо 4;
- soldier с 2+ вариантами: сохраняем все оригинальные TOR-копии;
- heroes/non-soldiers не урезаются;
- сам shader compiler, его native threads и cache format не изменяются.

Таким образом мы удаляем только явно избыточные roster entries и пока не пытаемся угадывать, какой equipment-вариант TOR хотел прогреть.

В лог пишется фактическая экономия:

```text
SHADER_CACHE_ROSTER|original=...; optimized=...; saved=...; uniqueCharacters=...; collapsedSingleLoadoutTroops=...; preservedMultiLoadoutTroops=...
```

Если private API TOR отличается от ожидаемого, patch не ставится и оригинальный Build Shader Cache остаётся без изменений.

Отключение:

```xml
<ShaderCacheAcceleration enabled="false" singleLoadoutCopies="1" />
```

## Что планируется для более сильного ускорения Build Shader Cache

Phase 2 — вместо одной огромной roster-сцены подавать контент управляемыми пакетами, ориентируясь на реальный `GetNumberOfShaderCompilationsInProgress()` и освобождая RAM между пакетами. Это требует теста в самой игре, потому что нужно доказать, что все equipment/material combinations продолжают попадать в cache.

## Первая загрузка после установки TOR

Для первой загрузки самый перспективный путь отличается от runtime-ускорения Build Shader Cache.

TaleWorlds официально поддерживает **precompiled module shader cache**: при публикации мода через Modding Kit с `Compile Shaders` готовый cache кладётся в `Modules/<ModuleName>/Shaders/D3D11`. Если такой cache совместим с точной версией Bannerlord/TOR, новый игрок не должен локально компилировать все эти combinations с нуля.

Поэтому план первой загрузки:

1. получить чистый TOR 1.3.15/1.16 asset set;
2. сгенерировать module-level precompiled cache через официальный Bannerlord Modding Kit;
3. проверить его на нескольких ПК/видеокартах и на обычном shader path + нашем Redirector;
4. измерить cold-start с пустым локальным cache;
5. только после проверки рассматривать distribution cache pack.

Мы **не будем** раздавать случайный локальный `%ProgramData%` cache от одного ПК: такой cache может содержать vanilla/другие моды и быть привязан к версии/настройкам. Нужен именно корректно сгенерированный module-level cache.

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

Для тестов запуска рекомендуется:

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
SHADER_ACCELERATOR_READY
SHADER_CACHE_ROSTER
SHADER_ACCELERATOR_FALLBACK
SHADER_START
SHADER_PROGRESS
SHADER_COMPLETE
SHADER_TELEMETRY_ERROR
OPTIMIZATION_ACTIVE
OPTIMIZATION_FALLBACK
INITIAL_SCREEN_READY
```

## Как тестировать shader accelerator

Для чистого сравнения нужны два прогона на одном ПК и одной версии TOR:

1. очистить cache одинаковым способом и запустить TOR `Build Shader Cache` с `<ShaderCacheAcceleration enabled="false" ... />`;
2. записать время и peak RAM;
3. снова привести cache к тому же исходному состоянию;
4. включить accelerator и повторить;
5. сравнить время, peak RAM и `SHADER_CACHE_ROSTER`.

После ускоренного прогона отдельно проверить несколько разных армий/битв и UI portraits, чтобы убедиться, что не осталось непрогретых combinations.

## Безопасность изменений

- оригинальный `TOR_Core.dll` не заменяется;
- оригинальные TaleWorlds DLL не входят в пакет;
- `0Harmony.dll` не дублируется в пакете — используется уже загруженная TOR/Harmony runtime;
- KaiTOR Stability не применяет и не удаляет Shader Cache Redirector;
- сохранения не переписываются модом;
- при несовместимом TOR API shader/battle optimization fail-open и оставляют оригинальную логику.
