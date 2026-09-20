# DiplomacyFinish — безопасный функциональный UI

- Главный экран дипломатии переведён на статическую Gauntlet-композицию, основанную на live-tested DIAG-A.
- Убраны динамические `IsVisible` / `IsDisabled` биндинги и перекрывающиеся вкладки, рядом с которыми наблюдался нативный `0xC0000005`.
- Функции не урезаны: выбор державы открывается через native inquiry; пакт/разрыв пакта вызываются с главного экрана; семейные дела и смена культуры поселения маршрутизируются в стабильные native GameMenu.
- Сохранены текущие исправления Realm House и FamilyLifecycle из master.
- Основной экран не содержит идентификаторов/сервисов KaiTOR Co-op.
- Сборка считается кандидатом на live-test, а не подтверждённым релизом, пока не пройдёт реальный тест в Bannerlord + TOR.

# KaiTOR Diplomacy & Dynasty v0.6.4 — изменения

Цель этой сборки — закрыть мастер-ТЗ v0.6.4 без переписывания уже подтверждённых систем NAP и смены народности.

## Замороженные рабочие системы
- Смена народности поселения: существующая реализация сохранена.
- NAP core: существующий KingdomDecision backend сохранён; сроки 30/60/90/180, 150 влияния за предложение, 100 за разрыв, блок войны и cooldown не заменялись.

## A — Family Fix
- «Семейные дела» остаются в городах и замках.
- Основные разделы переведены на GameMenu.
- «Брачные союзы» используют реальный MarriageBarterable -> Barter -> MarriageAction.
- Усыновление использует AdoptHeroAction.
- Женский однополый брак для дома игрока остаётся социальным браком без обычной беременности.
- Добавлены family/adoption/marriage diagnostics.

## B — Dawi Lifecycle
- Социальный брак отделён от биологической беременности.
- Dawi получили race-aware возраст для браков и фертильности; человеческий hard-limit 18–45 больше не применяется напрямую к Dawi.
- Long-lived NPC marriage age-gap больше не уходит в отрицательный коэффициент.
- Автогенерация Dawi-женщин остаётся выключенной до визуального live-test TPAC/template.
- Добавлены `kaitor_diplomacy.dawi_status` и `kaitor_diplomacy.dawi_spawn_test` для безопасной проверки одной женщины-гнома перед включением автопопуляции.
- Игровые меню и уведомления приведены к внутриигровому русскому стилю без технических терминов `native/barter/TOR/Bannerlord`.
- Удалён ручной перенос Dawi-женщины через Hero.Clan.

## C — Realm House
- Единственный runtime-путь новых домов: Clan.CreateCompanionToLordClan.
- Разрешён безопасный основатель, находящийся в обычной lord-party, если он не party leader и не находится в опасном состоянии.
- Первый дефицит проверяется ежедневно; очередь ~0.25 дня; commit в безопасное окно около 04:00.
- Cooldown после успеха: 42 игровых дня.
- Подробный KaiTORRealmHouse.log с причинами отказов.
- Старый unsafe cadet-house implementation удалён.

## D — TOR Children
- Стандартный Bannerlord AgeModel не заменяется.
- Стадии 8/14/16 дополняют обычное взросление.
- Реализован ProfessionEffectBridge по фактическим TOR Character Creation effects.
- Поддержаны career/attributes/abilities/known lore/casting/religious influence/perks/skills/specializations.
- TOR specialization attribute bonus теперь де-дублируется так же, как в Character Creation.
- Vampire/Necromancer/Blood Knight/Necrarch эффекты применяются к самому ребёнку/AI-герою без вызова небезопасных TOR InitialCareerSetup, привязанных к Hero.MainHero.
- Не переносятся стартовые телепорты, фракционные join-actions и стартовые companion grants.
- AI-дети используют тот же TOR lifecycle без popup.
- Если эффект выбора этапа 14 лет временно не применился, он ставится в безопасную очередь повторной попытки без повторной выдачи уже успешно применённых бонусов.

## E — Blood Kiss
- Blood Kiss находится в разговоре с конкретным героем.
- Доступ только настоящему Vampire; Necromancer сам по себе не Vampire.
- Mortal human: TOR-compatible vampire race + MinorVampire career.
- Foreign clan leader: охрана блокирует действие, clan graph не меняется.
- Undead/Vampire/Greenskin/unsupported race: нет эффекта.
- Troll: целевой hero не переносится между кланами; игрок получает одного настоящего TOR troll warrior как боевого воина.
- Добавлен прямой vampire-only маршрут из TOR troll encounter, потому что штатное приветствие тролля закрывает разговор до hero_main_options.
- Blood Kiss conversion получает безопасный персональный MinorVampire package TOR без изменения player/world state.
- Cooldown и persistence добавлены.

## F — Racial Population
- Старый KaiRacialPopulationBehavior удалён.
- Vampire population и Greenskin spores разделены на независимые behavior.
- Vampire AI не трогает игрока и использует bounded quota/cooldown.
- Greenskin population не использует marriage/pregnancy; hero создаётся через HeroCreator с native clan initialization.
- Раздельные save keys и логи.
- Ошибка очистки неудачного Greenskin spawn больше не подавляется пустым catch и записывается в лог.

## G — Political Layer
- Один общий native marriage barter bridge для Family UI, AI offers и political marriage.
- Политический брак: 500 000 динаров через persisted escrow.
- При отмене/ошибке escrow возвращается; при состоявшейся свадьбе передаётся другому дому.
- Dynastic Bond: 180 дней, +20 relation, +30 diplomacy trust; при разных державах использует существующий NAP backend, а не второй treaty system.
- AI marriage proposals: AI только предлагает; игрок принимает/отказывает; AI female+female proposals выключены.
- Возрастной score входящих брачных предложений для Dawi/эльфов использует нормализованный социальный возраст.
- Нарушение активного Dynastic Bond войной всегда отзывает его +30 trust, даже если NAP не был создан или уже снят.
- AI -> Player NAP: иностранный AI платит стоимость предложения, а решение проходит через совет player-led kingdom; игрок не оплачивает входящее предложение и не получает автоматическое решение за себя.
- Добавлен ruler dialogue frontend для NAP 30/60/90/180, использующий тот же KaiNonAggressionPactDecision.
- Добавлены STARTUP/DIPLOMACY_UI_READY/DIPLOMACY_UI_FAILED diagnostics.
- Старый settlement Diplomacy Office удалён.
- Удалён последний неактивный KaiCadetHouseSafeBehavior со старым Clan.CreateClan/founder.Clan path; CI запрещает его возврат.

## FULL package
- В ModuleData возвращены проверенные Dawi XML/action-set/skin patch files.
- FULL-Test архив обязан содержать проверенный `kaitor_dawi_female.tpac` из v0.6.3.3: 29 213 977 байт, SHA-256 `6f4d7d3dae74b66ca34631d091e843e4e93193ae50d0496da31096e24f831699`.
- Автогенерация Dawi-женщин всё ещё OFF до визуального live-test.

## Статус
CODE DONE / BUILD должен быть подтверждён CI.
LIVE TEST PASSED ставится только после проверки в реальной кампании Bannerlord 1.3.15 + TOR 1.3.15.


## UI hardening
- Добавлен независимый раздел «Управление державой KaiTOR» в городе/замке.
- Раздел даёт доступ к обзору договоров и доверия, выбору державы и срока NAP, внесению пакта на совет, выбору и разрыву действующего пакта, семейным делам, смене народности и состоянию систем.
- Переходы семейного UI теперь логируются через UI_NAV_BEGIN/UI_NAV_OK/UI_NAV_FAILED; при ошибке игрок получает видимое сообщение вместо «мёртвого клика».
- Добавлены команды `kaitor_diplomacy.ui_status` и `kaitor_diplomacy.ui_family`.
- Если приватный `KingdomDiplomacyVM._forceDecision` недоступен, игрок получает явное сообщение, а уже созданное решение остаётся доступно в списке решений королевства.


## Полноценный Gauntlet UI
- Добавлен собственный экран `KaiTORDiplomacyHubUIMovie` на отдельном layer `KaiTORDiplomacyLayer`.
- Основная кнопка в городе/замке теперь открывает Gauntlet dashboard; старый GameMenu-hub остаётся fallback.
- Вкладки: Обзор / Дипломатия / Семья / Держава и население.
- В Diplomacy tab: список держав, доверие, NAP status, выбор срока 30/60/90/180, внесение NAP и разрыва на совет.
- Family tab показывает дом/супруга/наследников и открывает стабильный Family Affairs workflow.
- Realm & Population tab показывает Dawi/Vampire/Greenskin/Realm House status, даёт Dawi live-test и переход к смене народности.
- RU/ENG localization добавлена отдельным набором `kaitor_diplomacy_ui_*`.
- UI технически изолирован от KaiTOR Co-op: отдельные movie/layer/namespace/module/localization prefixes; CI содержит collision guard.
- `dawi_spawn_test` теперь возвращает имя созданной дворфийки и явно помещает её в указанное безопасное поселение.


## Единый live-test лог
- Добавлен `%LOCALAPPDATA%\KaiTORDiplomacy\KaiTOR-LiveTest.log`.
- Все события `KaiRuntimeLog` автоматически зеркалируются в единый файл.
- Vampire/Greenskin population logs также зеркалируются туда с отдельным source.
- Автоматические lifecycle отметки: session launched, game loaded, save started, save finished.
- После загрузки и успешного сохранения пишется полный snapshot состояния кампании.
- Snapshot включает: модели, behaviors, UI/movie/layer, NAP/history/trust/breach/cooldown, dynastic bonds, членов семьи, TOR education 8/14/16, Dawi/Vampire/Greenskin, Realm House, текущую народность поселения и мировые счётчики.
- Освобождение лорда пишет `MERCY_RELEASE` с relationBefore/relationAfter/actualDelta и ожидаемым +50.
- Команды:
  - `kaitor_diplomacy.live_test_start`
  - `kaitor_diplomacy.live_test_snapshot`
  - `kaitor_diplomacy.live_test_path`
  - `kaitor_diplomacy.live_test_mark <текст>`


## H — Lore Family Lifecycle
- Проверено исходное `TORMarriageModel`: TOR возвращает `false` для всех marriage-checks; KaiTOR теперь полностью владеет разрешением семейных союзов.
- Убрана зависимость браков от TOR/vanilla marriage gate. Для обычных пар используются KaiTOR lore-rules и стандартные безопасные ограничения Bannerlord: герой активен, свободен, Lord, не template/minor/notable, не занят map event/army, не помолвлен, не близкий родственник.
- Верхнего возрастного лимита для брака больше нет. Используется только лорный возраст взросления.
- Humans/mortals: marriage 18+; pregnancy использует обычную human-like биологию.
- Dawi: marriage 30+ без верхнего лимита; pregnancy 30+ без человеческого cutoff 45; biological-age curve насыщается, но не выключает фертильность.
- Elves: marriage 18+ без верхнего лимита; pregnancy 18+ без человеческого cutoff 45.
- Vampires: социальный брак разрешён; обычная беременность запрещена; размножение остаётся через Blood Kiss.
- Greenskins: обычные браки/беременность выключены; размножение остаётся через spores.
- Ordinary undead: conventional marriage/pregnancy выключены.
- NPC auto-marriage: same culture + same FaceGen race; player-arranged marriage сохраняет более широкие social rules, но pregnancy по-прежнему требует same race + opposite sex.
- Удалён fallback, который при недоступном vampire helper блокировал беременность всем mortal жителям vampire cultures; actual vampire race по-прежнему блокируется.
- Dawi automatic women population включена: bounded shortage fill, максимум 3 generated women на AI-клан, 336 дней cooldown на клан.
- Vampire automatic population = ON; Greenskin spore population = ON.
- Добавлена команда `kaitor_diplomacy.family_rules` для проверки реальной активной матрицы правил.


## Live-test regression fixes — 20.09.2026
- Удалён нерабочий текстовый «гиперлинк» гонца из InformationText.
- Гонец перенесён на отдельную нативную Gauntlet-кнопку в энциклопедии героя без UIExtenderEx/ButterLib.
- Слой гонца не забирает фокус и снимается при закрытии страницы героя.
- Обычные совместимые смертные пары переведены на Bannerlord 1.3.15 fertility curve вместо наблюдавшегося постоянного нуля.
- Добавлены STABILITY_DAILY / STABILITY_WEEKLY и MARRIAGE_BIOLOGY.
- Dawi female skin/action assets временно изолированы из regression-пакета после кросс-расовой визуальной регрессии; template сохранён для совместимости старого сейва.
- Старый отдельный diplomacy dashboard остаётся исключён из пакета; Kingdom Diplomacy использует нативный интерфейс.
