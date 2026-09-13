KaiCleave v0.2.1-coop-beta
Bannerlord v1.3.15.110062 + The Old Realms 1.3.15 + KaiTOR Online

НАЗНАЧЕНИЕ
KaiCleave позволяет одному реальному взмаху оружия проходить через несколько противников. Каждый принятый хит получает полный исходный импульс атаки, но урон каждой цели отдельно рассчитывается штатной моделью Bannerlord / TOR: броня, тип урона, сопротивления, перки, часть тела и другие модификаторы не обходятся.

ЧТО ИЗМЕНЕНО В v0.2.1
- Добавлен безопасный режим для KaiTOR Coop.
- При активном Coop модуль определяет роль процесса до установки Harmony-патчей.
- В обычной одиночной игре KaiCleave работает как раньше.
- В KaiTOR authoritative campaign-process (/server + /coopsave) combat-патчи активны.
- На Coop-клиентах KaiCleave загружен для совпадения module list/version, но damage/collision Harmony-патчи НЕ устанавливаются.
- PlayerOnly=true на Coop-сервере больше не использует attacker.IsMainAgent. Сервер проверяет, что Hero атакующего зарегистрирован в Coop PlayerManager.
- Если Coop player lookup недоступен, PlayerOnly fail-closed: cleave не применяется, а не распространяется на NPC.
- Сохранён финальный postfix TORAgentApplyDamageModel на авторитетной стороне.
- Обычный Bannerlord multiplayer по-прежнему не патчится.

БАЗОВАЯ ЛОГИКА CLEAVE
- Принудительное SlicedThrough после валидного попадания по врагу.
- Полный momentum для следующей цели без падения урона по цепочке.
- Щит, блок, парирование, chamber block, стены и zero-damage hit останавливают удар штатно.
- Один и тот же враг не получает повторный RegisterBlow в рамках определённого swing.
- Максимум целей за swing настраивается; по умолчанию 12.
- По умолчанию работают только обычные swing-атаки; thrust отключён.
- По умолчанию cleave только для зарегистрированных игроков.

УСТАНОВКА ДЛЯ ОБЫЧНОГО TOR
1. Закрыть игру.
2. Скопировать папку KaiCleave в:
   Mount & Blade II Bannerlord\Modules\KaiCleave
3. В лаунчере включить KaiCleave.
4. Поставить KaiCleave НИЖЕ TOR_Core.
5. Запустить игру.

УСТАНОВКА ДЛЯ KAITOR COOP
На authoritative server и на КАЖДОМ клиенте должна стоять одна и та же версия KaiCleave v0.2.1.
Рекомендуемый порядок:

TOR_Armory
TOR_Environment
TOR_Core
KaiCleave
KaiTOR_Stability
Coop

KaiTOR launcher автоматически вставляет KaiCleave перед KaiTOR_Stability/Coop, если мод установлен и проходит preflight.
Если KaiTOR_Stability не установлен, порядок будет TOR_Core -> KaiCleave -> Coop.

Важно: Coop проверяет сторонние модули и их версии. Нельзя оставлять KaiCleave только у одного клиента или только на сервере.

ОЖИДАЕМЫЕ РЕЖИМЫ В ЛОГЕ
Одиночная игра:
combat patches ACTIVE
runtime mode=Standalone

KaiTOR authoritative server:
combat patches ACTIVE
runtime mode=AuthoritativeServer

KaiTOR client:
combat patches PASSIVE on Coop client; authoritative server owns cleave
runtime mode=PassiveClient

НАСТРОЙКИ
Файл: Modules\KaiCleave\KaiCleave.ini

Основные значения:
Enabled=true
PlayerOnly=true
MaxTargetsPerSwing=12
DebugLogging=true
FullMomentum=true
ForceSlicedThrough=true
AllowThrusts=false
AllowFriendlyTargets=false

Оружие по умолчанию:
OneHandedSword=true
TwoHandedSword=true
OneHandedAxe=true
TwoHandedAxe=true
OneHandedPolearm=true
TwoHandedPolearm=true
LowGripPolearm=true
Mace=false
TwoHandedMace=false
Dagger=false
Pick=false

КАК ТЕСТИРОВАТЬ В KAITOR COOP
1. Запустить authoritative KaiTOR campaign server с KaiCleave.
2. Подключить минимум два клиента с той же версией KaiCleave.
3. Проверить module validation: подключение должно пройти без mismatch.
4. На серверном KaiCleave.log должна быть строка mode=AuthoritativeServer и combat patches ACTIVE.
5. На клиентских KaiCleave.log должна быть строка mode=PassiveClient и combat patches PASSIVE.
6. Войти в один бой двумя игроками.
7. Каждый игрок делает горизонтальный swing по 3-6 стоящим рядом врагам.
8. Проверить, что cleave применяется к обоим зарегистрированным игрокам, а не только к host/main agent.
9. Проверить, что обычные NPC-солдаты не получают cleave при PlayerOnly=true.
10. Проверить щит/парирование/стены и MaxTargetsPerSwing.
11. Завершить бой и убедиться, что campaign state у всех клиентов совпадает.

ЛОГ
Файл: Modules\KaiCleave\KaiCleave.log
При DebugLogging=true дополнительно видно:
- runtime mode и роль процесса;
- активны или пассивны combat-патчи;
- факт установки TOR final-reaction patch;
- victim index;
- weapon class;
- inflicted damage;
- absorbed by armor;
- momentum;
- attack progress;
- номер цели в текущем swing;
- реакция до/после SlicedThrough;
- подавленные дубли и лимит целей.

БЕЗОПАСНОСТЬ
- KaiCleave не изменяет TaleWorlds.Native.dll или TOR_Core.dll.
- Клиенты KaiTOR не рассчитывают дополнительную cleave-логику локально.
- Авторитетный сервер остаётся единственным источником изменения damage/collision state.
- PlayerOnly на сервере привязан к зарегистрированным Coop Hero через PlayerManager.TryGetControlledObjectInfo.
- При ошибке определения владельца cleave для этого атакующего не применяется.

Если будет краш или рассинхрон, пришли:
1. KaiCleave.log с сервера;
2. KaiCleave.log с проблемного клиента;
3. stack trace / crash report;
4. оружие и число целей;
5. кто атаковал: host или другой подключённый игрок.
