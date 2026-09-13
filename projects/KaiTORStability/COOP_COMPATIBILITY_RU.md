# KaiTOR Stability 0.4.1 + KaiTOR Online

Совместимость с KaiTOR Online сделана в безопасном режиме.

## Обязательное правило Coop

KaiTOR/Bannerlord Coop сравнивает community-модули сервера и клиента в обе стороны, включая Id и Version. Поэтому если `KaiTOR_Stability` включён хотя бы у одного клиента, **тот же мод и та же версия должны быть включены на авторитетном KaiTOR campaign-process сервере и у всех подключающихся клиентов**.

Рекомендуемый порядок:

```text
TOR_Armory
TOR_Environment
TOR_Core
KaiTOR_Stability
Coop
```

`Coop` остаётся последним.

## Что делает защитный режим

Когда активен мод `Coop`, KaiTOR Stability по умолчанию **не заменяет TOR StatusEffectMissionLogic**. Это единственная текущая оптимизация Stability, которая вмешивается в mission/gameplay tick. Так мы не вносим дополнительную разницу в симуляцию между сервером и клиентами до отдельного сетевого теста.

При этом остаются активны безопасные локальные функции:

- Load Monitor;
- проценты и ETA загрузки/компиляции;
- shader telemetry;
- first-load shader-source prestage;
- совместимость со стандартным shader cache и Kai Shader Cache Redirector;
- Shader Cache Accelerator для отдельной процедуры TOR `Build Shader Cache`.

Защитный режим управляется в `ModuleData/KaiTORStability.config.xml`:

```xml
<CoopCompatibility disableGameplayPatches="true" />
```

Оставлять `true` до отдельной live-проверки сетевого боя.

## Shader Cache Redirector

Redirector не является Bannerlord community-модулем и не попадает в список модулей Coop. Он меняет только локальный путь compiled shader cache. Поэтому один ПК может использовать стандартный cache, другой — наш redirect, при условии что версия Bannerlord/TOR/KaiTOR модулей совпадает.
