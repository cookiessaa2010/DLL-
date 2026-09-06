KaiCleave v0.2.0-beta
Bannerlord v1.3.15.110062 + The Old Realms 1.3.15

НАЗНАЧЕНИЕ
KaiCleave позволяет одному реальному взмаху оружия проходить через несколько противников. Каждый принятый хит получает полный исходный импульс атаки, но урон каждой цели отдельно рассчитывается штатной моделью Bannerlord / TOR: броня, тип урона, сопротивления, перки, часть тела и другие модификаторы не обходятся.

ЧТО ИЗМЕНЕНО В v0.2
- Принудительное SlicedThrough после валидного попадания по врагу.
- Полный momentum для следующей цели без падения урона по цепочке.
- Финальный postfix на TORAgentApplyDamageModel, чтобы TOR не мог перезаписать решение после native helper.
- Щит, блок, парирование, chamber block, стены и zero-damage hit останавливают удар штатно.
- Один и тот же враг не получает повторный RegisterBlow в рамках определённого swing.
- Максимум целей за swing настраивается; по умолчанию 12.
- Фильтр классов оружия.
- По умолчанию работают только обычные swing-атаки; thrust отключён.
- По умолчанию cleave только для игрока.
- Добавлен KaiCleave.ini.
- Добавлен KaiCleave.log для тестов.
- Multiplayer не патчится.

УСТАНОВКА
1. Закрыть игру.
2. Скопировать папку KaiCleave в:
   Mount & Blade II Bannerlord\Modules\KaiCleave
3. В лаунчере включить KaiCleave.
4. Поставить KaiCleave НИЖЕ модулей The Old Realms / TOR_Core.
5. Запустить игру.

ОЖИДАЕМОЕ СООБЩЕНИЕ ПРИ ЗАГРУЗКЕ
[KaiCleave] 0.2.0-beta loaded | Bannerlord 1.3.15.110062 | TOR-ready

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

КАК ТЕСТИРОВАТЬ
1. Возьми длинный двуручный меч или топор.
2. Поставь 3-6 врагов близко друг к другу.
3. Сделай горизонтальный swing.
4. Проверь, что несколько целей получают урон одним взмахом.
5. Затем проверь цепочку: лёгкая броня -> тяжёлая броня -> крупная/особая цель TOR.
6. Убедись, что цифры урона отличаются из-за защиты цели, а не из-за искусственного falloff.
7. Проверь щит: попадание в активный щит должно остановить cleave.
8. Проверь парирование/блок: удар не должен проходить дальше.

ЛОГ
Файл: Modules\KaiCleave\KaiCleave.log
При DebugLogging=true в нём пишутся:
- victim index
- weapon class
- итоговый inflicted damage
- absorbed by armor
- momentum
- attack progress
- номер цели в текущем swing
- реакция до/после принудительного SlicedThrough
- дубли, которые были подавлены
- срабатывание лимита целей
- факт установки TOR final-reaction patch

ВАЖНО
Это beta для реального теста на твоей сборке TOR. Сборка компилируется именно против Bannerlord.ReferenceAssemblies 1.3.15.110062. Мод не изменяет TaleWorlds.Native.dll, TOR_Core.dll или другие файлы игры.

Если будет краш, пришли:
1. KaiCleave.log
2. stack trace / crash report
3. оружие, которым бил
4. что именно происходило в момент краша
