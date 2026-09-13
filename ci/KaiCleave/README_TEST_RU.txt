KaiCleave v0.2.1-coop-beta
Bannerlord v1.3.15.110062 + The Old Realms 1.3.15 + KaiTOR Online

НАЗНАЧЕНИЕ
KaiCleave позволяет одному реальному взмаху оружия проходить через несколько противников. Каждый принятый хит получает полный исходный импульс атаки, но урон каждой цели отдельно рассчитывается штатной моделью Bannerlord / TOR: броня, тип урона, сопротивления, перки, часть тела и другие модификаторы не обходятся.

ЧТО ИЗМЕНЕНО В v0.2.1
- Добавлен безопасный режим для KaiTOR Coop.
- Учтена реальная архитектура Coop-боя: damage/collision рассчитывается на battle peer, который владеет атакующим агентом; удалённые агенты являются inert puppets.
- Поэтому KaiCleave НЕ переносит расчёт cleave на campaign server. Наоборот: /server /coopsave процесс остаётся combat-passive.
- На каждом игровом Coop-клиенте combat-патчи активны, но cleave проходит только для локально принадлежащего зарегистрированного player Hero.
- Локальная принадлежность проверяется через Coop PlayerManager.TryGetControlledObjectInfo + ControlledObjectInfo.IsControlled, а не только через Agent.IsMainAgent.
- Remote player puppets и NPC не проходят owner-local проверку.
- Если Coop player lookup недоступен или ломается, cleave fail-closed и не применяется.
- Для безопасности PlayerOnly=false в Coop v0.2.1 пока трактуется как player-only. NPC cleave будет включён только после отдельной authority-aware интеграции с AgentRegistry.
- Сохранён финальный postfix TORAgentApplyDamageModel на battle peer, чтобы TOR не перезаписывал SlicedThrough после native helper.
- Обычный Bannerlord multiplayer по-прежнему не патчится.

ПОЧЕМУ ЭТО СОВМЕСТИМО С COOP
BannerlordCoop в бою работает P2P: каждый клиент симулирует принадлежащие ему реальные агенты, а чужие агенты являются puppets. Когда локальный агент бьёт чужого puppet, Coop перехватывает Blow и маршрутизирует его владельцу жертвы. KaiCleave v0.2.1 меняет traversal/momentum именно там, где рождается настоящий локальный swing, после чего штатный Coop damage router передаёт получившиеся blows как обычно.

БАЗОВАЯ ЛОГИКА CLEAVE
- Принудительное SlicedThrough после валидного попадания по врагу.
- Полный momentum для следующей цели без падения урона по цепочке.
- Щит, блок, парирование, chamber block, стены и zero-damage hit останавливают удар штатно.
- Один и тот же враг не получает повторный RegisterBlow в рамках определённого swing.
- Максимум целей за swing настраивается; по умолчанию 12.
- По умолчанию работают только обычные swing-атаки; thrust отключён.
- По умолчанию cleave только для игрока.

УСТАНОВКА ДЛЯ ОБЫЧНОГО TOR
1. Закрыть игру.
2. Скопировать папку KaiCleave в:
   Mount & Blade II Bannerlord\Modules\KaiCleave
3. В лаунчере включить KaiCleave.
4. Поставить KaiCleave НИЖЕ TOR_Core.
5. Запустить игру.

УСТАНОВКА ДЛЯ KAITOR COOP
На campaign server и на КАЖДОМ клиенте должна стоять одна и та же версия KaiCleave v0.2.1 и совместимая Bannerlord.Harmony v2.4.2.248.
Рекомендуемый порядок:

Bannerlord.Harmony
Native / базовые модули
TOR_Armory
TOR_Environment
TOR_Core
KaiCleave
KaiTOR_Stability
Coop

KaiTOR launcher автоматически валидирует KaiCleave + Harmony и вставляет KaiCleave перед KaiTOR_Stability/Coop.
Если KaiTOR_Stability не установлен, порядок будет TOR_Core -> KaiCleave -> Coop.

Важно: Coop проверяет сторонние модули и их версии. Нельзя оставлять KaiCleave только у одного клиента или только на campaign server.

ОЖИДАЕМЫЕ РЕЖИМЫ В ЛОГЕ
Одиночная игра:
runtime mode=Standalone
combat patches ACTIVE | owner-local battle authority

KaiTOR campaign server (/server /coopsave):
runtime mode=CoopCampaignServer
combat patches PASSIVE on Coop campaign server; battle peers own cleave simulation

Игровой KaiTOR клиент / battle peer:
runtime mode=CoopPeer
combat patches ACTIVE | owner-local battle authority

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
1. Запустить KaiTOR campaign server с KaiCleave.
2. Подключить минимум два клиента с той же версией KaiCleave.
3. Проверить module validation: подключение должно пройти без mismatch.
4. На campaign server KaiCleave.log должна быть строка mode=CoopCampaignServer и combat patches PASSIVE.
5. На обоих игровых клиентах KaiCleave.log должна быть строка mode=CoopPeer и combat patches ACTIVE.
6. Войти в один бой двумя игроками.
7. Каждый игрок делает горизонтальный swing по 3-6 стоящим рядом врагам.
8. Проверить, что cleave срабатывает отдельно для каждого локально управляемого игрока.
9. На копии удалённого player puppet не должно появляться второго локального cleave-расчёта.
10. Проверить, что обычные NPC-солдаты не получают cleave при PlayerOnly=true.
11. Проверить щит/парирование/стены и MaxTargetsPerSwing.
12. Завершить бой и убедиться, что HP/смерти/campaign state совпадают у всех участников.

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
- Campaign server не вмешивается в mission collision pipeline.
- Каждый battle peer применяет cleave только к своему зарегистрированному player Hero.
- Remote puppets не получают право инициировать cleave.
- PlayerOnly=false в Coop намеренно не включает NPC cleave в этой версии.
- При ошибке определения владельца cleave для этого атакующего не применяется.

Если будет краш или рассинхрон, пришли:
1. KaiCleave.log с campaign server;
2. KaiCleave.log с обоих игровых клиентов;
3. stack trace / crash report;
4. оружие и число целей;
5. кто атаковал и был ли это локальный player Hero или puppet.
