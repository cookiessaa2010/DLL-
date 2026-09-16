KaiCleave v0.3.0-heavy-block-ui — тестовая сборка
Bannerlord 1.3.15.110062 / The Old Realms 1.3.15

УСТАНОВКА
1. Удалить/переименовать старую папку Modules\KaiCleave.
2. Скопировать папку KaiCleave из архива в Modules.
3. В лаунчере включить Bannerlord.Harmony и KaiCleave.
4. В игре нажать F10 — откроется встроенное меню KaiCleave.

ЧТО НОВОГО
- Встроенные настройки на F10, без обязательного MCM/ButterLib/UIExtenderEx.
- Настройки применяются сразу и сохраняются в Modules\KaiCleave\KaiCleave.ini.
- Отдельное поведение для тяжёлого оружия:
  * TwoHandedSword
  * TwoHandedAxe
  * TwoHandedMace
  * OneHandedPolearm
  * TwoHandedPolearm
  * LowGripPolearm
- HeavyShieldContinue=true по умолчанию: после щита тяжёлый взмах может продолжить траекторию.
- HeavyWeaponBlockContinue=false по умолчанию: обычный блок оружием/парирование останавливает взмах, пока опция не включена вручную.
- Одноручное оружие НЕ получает проход через щит/парирование.
- Блок не считается поражённой целью и не расходует MaxTargetsPerSwing.
- NPC cleave не включён. PlayerOnly=true остаётся безопасным режимом.
- FullMomentum=true: никакого поэтапного снижения урона по следующим целям не добавлялось.

ОСНОВНОЙ ТЕСТ
1. Возьми двуручный меч/топор или древковое.
2. F10 -> убедись, что "2H / polearm through shield" = ON.
3. Поставь противников так, чтобы один блокировал щитом, а второй стоял сразу за ним по дуге удара.
4. Сделай горизонтальный взмах.
Ожидается: первый противник корректно блокирует щитом, но траектория тяжёлого оружия не обязана завершаться на нём; следующая валидная цель может получить обычный Bannerlord/TOR hit.

ТЕСТ ПАРИРОВАНИЯ
1. F10 -> включи "2H / polearm through weapon block".
2. Повтори тест с блоком оружием вместо щита.
3. После проверки выключи эту опцию, если не нравится влияние на дуэли.

КОНТРОЛЬНЫЕ ТЕСТЫ
- Одноручный меч/топор должен останавливаться на щите как раньше.
- NPC/тролль не должен получить cleave игрока.
- Один и тот же агент не должен получать повторный damage от одного взмаха.
- MaxTargetsPerSwing должен учитывать только реальные поражённые цели.
- С выключенным HeavyShieldContinue щит снова обязан останавливать cleave.
- С выключенным HeavyWeaponBlockContinue оружейный блок/парирование обязан останавливать cleave.

ЛОГ
Modules\KaiCleave\KaiCleave.log
При DebugLogging=true ищи строки:
- heavy-shield-continue
- heavy-weapon-block-continue
- momentum-preserved
- native-reaction
- duplicate-suppressed
- CLEAVE_TIMING

Если будет странное прохождение через щит/парирование, пришли KaiCleave.log и опиши оружие + ситуацию.
