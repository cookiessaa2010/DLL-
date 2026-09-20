# DiplomacyFinish — обязательный live-test перед релизом

Эта сборка закрывает нативный краш главного Gauntlet-экрана консервативным функциональным layout. CI проверяет компиляцию, XML, safety-контракты Realm House/FamilyLifecycle и изоляцию от KaiTOR Co-op; реальный Bannerlord/TOR runtime всё равно нужно подтвердить вручную.

1. Загрузить существующее сохранение Bannerlord 1.3.15.110062 + TOR 1.3.15.
2. Открыть Королевство -> Дипломатия и выбрать другую державу. В нижнем ряду должна добавляться только одна KaiTOR-функция: «Пакт о ненападении» (или «Разорвать пакт» при активном договоре). Никакой дополнительной кнопки/панели справа быть не должно.
3. Нажать «Пакт о ненападении», выбрать 30/60/90/180 дней и проверить создание council decision. При активном пакте проверить «Разорвать пакт». Каждое действие должно либо создать решение, либо показать явную игровую причину отказа.
4. На городских задворках рядом с «Посетить таверну» проверить «Принять в род»: открыть список кандидатов, отменить, вернуться назад, затем выполнить одно допустимое усыновление.
5. Брачные предложения проверять только через обычный диалог с лордом. Для удалённого контакта открыть энциклопедию лорда -> «Отправить гонца» -> дождаться прибытия -> открыть настоящий разговор.
6. В принадлежащем игроку городе/замке проверить «Сменить культуру поселения». При уже совпадающей культуре пункт не должен показываться.
7. Сохранить игру, перезагрузить сохранение и повторно проверить панель, гонца, таверну и культуру.
8. При появлении нового AI Realm House проверить KaiTORRealmHouse.log: после NATIVE_FACTORY_OK ожидается PROMOTE_OCCUPATION_OK (если требовалось), затем REALM_CREATE_SUCCESS; лидер нового клана должен быть Lord.
9. При установленном KaiTOR Co-op открыть/закрыть оба интерфейса по очереди; movie/layer Diplomacy не должны пересекаться с Co-op.

Кандидат считается готовым к релизу только после прохождения этих пунктов без краша.

## UI terminal-path audit
CI автоматически проверяет, что каждый видимый Command.Click в Gauntlet XML имеет соответствующий публичный метод VM. Дополнительно проверяются конечные пути:
- native Kingdom Diplomacy -> единственная NAP-кнопка без дополнительной Gauntlet-панели;
- NAP create/review/break -> council decision или явное сообщение;
- town_backstreet -> adoption -> candidate/apply/back;
- settlement culture -> confirmation/result или явная причина блока;
- encyclopedia courier link -> confirm/send/arrival/conversation/defer;
- ruler dialogue NAP -> decision + visible confirmation;
- Blood Kiss/troll choices -> effect или видимое сообщение об ошибке;
- childless marriage warning -> native barter либо close;
- education selection -> applied effect + visible confirmation.

Старые ActionOpenFamily, ActionOpenFallbackHub, city-family entry и отдельный incoming-marriage popup считаются ошибкой и CI не должен пропускать их возврат.

# KaiTOR v0.6.4 — LIVE TEST checklist

Сборка не считается полностью готовой, пока каждый критический путь не проверен в игре.

## 0. Старт
- [ ] FULL-архив содержит ModuleData Dawi и AssetPackages/kaitor_dawi_female.tpac.
- [ ] TPAC: 29 213 977 байт; SHA-256 6f4d7d3dae74b66ca34631d091e843e4e93193ae50d0496da31096e24f831699.
- [ ] Старая save v0.6.3.3 загружается.
- [ ] Новая кампания запускается.
- [ ] Нет crash при старте.
- [ ] В %LOCALAPPDATA%/KaiTORDiplomacy/KaiTORDiplomacy.log есть STARTUP и DIPLOMACY_UI_READY.

## A. Family
- [ ] В городе/замке нет старого пункта «Семейные дела».
- [ ] На городских задворках рядом с «Посетить таверну» есть единственный семейный пункт «Принять в род».
- [ ] «Принять в род» открывает список кандидатов или явное сообщение, если кандидатов нет.
- [ ] «Назад» из меню принятия в род возвращает на городские задворки.
- [ ] AdoptHeroAction проходит; ребёнок виден в family tree.
- [ ] Save/load сохраняет усыновление.
- [ ] Брак игрока предлагается только через обычный диалог с лордом.
- [ ] Childless marriage warning: «Продолжить» реально возвращает в native final barter, «Не сейчас» закрывает диалог.
- [ ] Female+female player-house marriage проходит, если разрешена моделью.
- [ ] Female+female пара не получает обычную беременность.
- [ ] После save/load spouse/clan/family tree корректны.
- [ ] KaiTOR-Family.log пишет MARRIAGE_SUCCESS / PREGNANCY_CONCEIVED / BIRTH_SUCCESS и недельные счётчики.

## B. Dawi
- [ ] `kaitor_diplomacy.dawi_status` показывает готовность ресурсов и состояние автопопуляции.
- [ ] `kaitor_diplomacy.dawi_spawn_test` создаёт ровно одну тестовую женщину-гнома в безопасном AI-клане.
- [ ] Female Dawi manual live-test: лицо.
- [ ] Тело/skeleton.
- [ ] Portrait/encyclopedia.
- [ ] Equipment.
- [ ] Settlement scene animations.
- [ ] Save/load.
- [ ] Dawi age 30+ может вступить в брак.
- [ ] Dawi женщина старше 45 получает ненулевой pregnancy chance; человеческий верхний cutoff не применяется.
- [ ] Dawi женщина старше 180 всё ещё проходит lore pregnancy gate; biological chance curve насыщается, но не становится 0 только из-за возраста.
- [ ] Dawi беременность проходит.
- [ ] Рождается Dawi ребёнок.
- [ ] Automatic Dawi women population уже ON: после weekly tick shortage-клан получает не более одной новой женщины за cooldown.
- [ ] На одном AI Dawi-клане auto-generated женщин не становится больше 3.

## C. Realm House
- [ ] В AI kingdom есть clan deficit.
- [ ] Ускорить 1–2 игровых дня.
- [ ] Новый noble clan появился через native factory.
- [ ] Founder стал leader/Lord.
- [ ] Kingdom/culture правильные.
- [ ] Нет двойного roster/clan membership.
- [ ] Исходный клан/партия целы.
- [ ] Save/load нового клана работает.
- [ ] Если за 8–10 игровых дней клана нет — тест FAIL; приложить KaiTORRealmHouse.log.

## D. TOR Children
- [ ] Player child: stage 8.
- [ ] Stage 14.
- [ ] Stage 16 profession.
- [ ] Required specialization появляется отдельно.
- [ ] Magister full effects.
- [ ] Dawi Slayer/Runelord full effects.
- [ ] Priest specialization full effects.
- [ ] Нет телепорта ребёнка на CC spawn coordinates.
- [ ] AI child получает TOR stages без popup.
- [ ] Save/load сохраняет choices/effects.

## E. Blood Kiss
- [ ] Necromancer без vampire race не видит Blood Kiss.
- [ ] Vampire видит «Даровать Поцелуй крови».
- [ ] Mortal human -> vampire + MinorVampire.
- [ ] Foreign clan leader -> guard failure, clan graph unchanged.
- [ ] Undead -> no effect.
- [ ] Greenskin -> no effect.
- [ ] Обычный TOR troll encounter у Vampire открывает специальный Blood Kiss route, а не закрывается штатным greeting.
- [ ] Troll -> hero не становится spouse/vampire и не меняет clan; в player party появляется troll warrior.
- [ ] Cooldown переживает save/load.

## F. Population
- [ ] Vampire AI population работает, MainHero/player kingdom не меняется автоматически.
- [ ] Vampire quota/cooldown ограничивают рост.
- [ ] Greenskin spore pressure растёт.
- [ ] Greenskin AI companion создаётся нативно в host clan.
- [ ] Нет marriage/pregnancy Greenskin.
- [ ] Save/load сохраняет pressure/cooldowns.
- [ ] Проверить KaiTORVampirePopulation.log и KaiTORGreenskinPopulation.log.

## G. Political layer
- [ ] В native Kingdom -> Diplomacy при выбранной державе есть KaiTOR NAP action.
- [ ] «Предложить пакт о ненападении» -> выбор 30/60/90/180 -> создаётся council decision.
- [ ] При уже созданном decision «Рассмотреть пакт» открывает native council UI либо показывает fallback-сообщение.
- [ ] При активном NAP «Разорвать пакт о ненападении» -> подтверждение -> council decision.
- [ ] Любой stale-state (нет kingdom, недостаточно influence, изменились условия) даёт явное сообщение, а не бесшумный клик.
- [ ] Ruler conversation позволяет предложить NAP 30/60/90/180; после выбора появляется подтверждение результата.
- [ ] AI -> Player NAP появляется только если PlayerClan правит kingdom.
- [ ] Входящий NAP проходит через player council и не списывает 150 влияния с игрока.
- [ ] Обычные AI-кланы сами заключают браки через KaiWorldMarriageBehavior; результат виден в KaiTOR-Family.log.
- [ ] Старый Kingdom UI NAP по-прежнему проходит полный проверенный цикл.
- [ ] KaiDynasticMarriageBehavior остаётся backend-кодом и не считается пользовательским UI-путём этой сборки.
- [ ] KaiIncomingMarriageProposalBehavior не зарегистрирован и не должен показывать отдельное popup-меню.

## Mercy
- [ ] После победы над вражеским лордом выбрать отпускание.
- [ ] Игра использует штатный Bannerlord-путь отношения: базовый +4 заменён на +50.
- [ ] Не появляется отдельный второй бонус к конкретному лорду.
- [ ] Итоговое изменение отношений отображается один раз штатным уведомлением.
- [ ] Повторить для ReleasedAfterBattle и ReleasedByChoice.
- [ ] В KaiTOR-LiveTest.log есть MERCY_RELATION_PATCH для обоих маршрутов и нет старого MERCY_RELEASE.

## Regression
- [ ] Смена культуры поселения по-прежнему работает.
- [ ] Старый NAP по-прежнему работает.
- [ ] SAVE -> EXIT -> LOAD: семьи, NAP, cooldown, children, realm houses, dynastic bonds intact.

## Long simulation
- [ ] 1 год.
- [ ] 5 лет.
- [ ] 20 лет.
Проверить crash, семьи, population, браки, рождения, старение, новые кланы, договоры, cooldown и save integrity.


## UI hardening live-test
- [ ] Королевство -> Дипломатия -> выбранная держава показывает только одну дополнительную NAP-кнопку.
- [ ] Справа от NAP-кнопки нет обрезанной кнопки «Дипломатия и династия».
- [ ] «Пакт о ненападении» -> выбор 30/60/90/180 -> council decision.
- [ ] При активном договоре показывается «Разорвать пакт».
- [ ] При уже созданном решении показывается «Рассмотреть пакт»/«Рассмотреть разрыв пакта».
- [ ] Нативные Alliance / War / Trade actions остаются полностью видимыми и кликабельными.
- [ ] Ни одно из дипломатических действий не вызывает crash.

## Native Diplomacy UI / anti-collision live-test
- [ ] В Kingdom -> Diplomacy нет дополнительной KaiTOR Gauntlet-панели.
- [ ] KaiTOR добавляет только NAP action к нативному списку действий.
- [ ] На 1600x900 все нативные действия и NAP-кнопка помещаются без выхода за правую границу.
- [ ] Co-op UI не затрагивается.
- [ ] После NAP/разрыва native Kingdom screen остаётся рабочим.
- [ ] Save/load сохраняет NAP, trust, breach и cooldown.

## Единый лог live-test
- [ ] В начале теста выполнить `kaitor_diplomacy.live_test_start`.
- [ ] Команда возвращает путь `%LOCALAPPDATA%\KaiTORDiplomacy\KaiTOR-LiveTest.log`.
- [ ] После открытия Kingdom -> Diplomacy в логе нет попыток открыть `GAUNTLET_UI_OPEN`; NAP работает через нативный экран.
- [ ] После NAP/разрыва присутствуют council/NAP события и snapshot отражает актуальный trust/breach/cooldown.
- [ ] После семейных действий присутствуют marriage/dynastic события.
- [ ] После теста +50 присутствует `MERCY_RELEASE` с relationBefore/relationAfter и actualDelta.
- [ ] После Dawi spawn присутствует `DAWI_WOMAN_CREATE` с именем/кланом/поселением.
- [ ] Save пишет `SAVE_STARTED` и `SAVE_OVER success=True`; после него автоматически идёт snapshot.
- [ ] После reload есть `GAME_LOADED` и новый snapshot с теми же persistent состояниями.
- [ ] В конце выполнить `kaitor_diplomacy.live_test_snapshot` и передать один файл `KaiTOR-LiveTest.log`.


## H. Lore family lifecycle
- [ ] `kaitor_diplomacy.family_rules` показывает: Human 18+/18-45, Dawi 30+/30+, Elf 18+/18+, Vampire marriage/Blood Kiss, Greenskin spores, Undead OFF.
- [ ] Human/mortal NPC opposite-sex same-race pair может заключить AI marriage.
- [ ] Dawi opposite-sex dwarf pair 30+ может заключить AI marriage.
- [ ] Elf opposite-sex elf pair 18+ может заключить AI marriage.
- [ ] Vampire opposite-sex vampire pair 18+ может заключить social marriage, но `PREGNANCY_BLOCKED`.
- [ ] Mortal human в khuzait/mousillon culture не блокируется только из-за vampire culture id.
- [ ] Greenskin не получает conventional marriage/pregnancy; spore population продолжает работать.
- [ ] Ordinary undead не получает conventional marriage/pregnancy.
- [ ] Межрасовая пара может быть social-only только через разрешённый player-arranged путь; vanilla offspring pipeline для cross-race всегда заблокирован.
- [ ] Старый Dawi/Elf возраст 100+ не делает NPC marriage chance отрицательным.
- [ ] Save/load сохраняет супругов, беременности, детей и Dawi auto-population cooldown.


## Регрессия после live-test 20.09.2026
- [ ] Существующий сейв с уже созданными Dawi-женщинами загружается.
- [ ] Новые Dawi-женщины автоматически НЕ создаются: генерация SAFE-OFF до отдельной сертификации ассетов.
- [ ] Dawi/Vampire/Greenskin/обычные лица не темнеют и не получают чужие материалы.
- [ ] На городских задворках рядом с «Посетить таверну» есть «Принять в род».
- [ ] В Kingdom -> Diplomacy нет пятой обрезанной KaiTOR-кнопки; NAP работает только через нативный action.
- [ ] Отпускание лорда даёт одно штатное изменение отношения +50, без второго отдельного бонуса.
- [ ] Обычные совместимые пары 18-45 получают ненулевой шанс беременности по Bannerlord-кривой, если клановый population factor не обнуляет шанс.
- [ ] В KaiTOR-LiveTest.log появляются STABILITY_DAILY и STABILITY_WEEKLY.
- [ ] В энциклопедии живого лорда отображается отдельная кнопка «Отправить гонца» с ценой в динарах.
- [ ] Нажатие кнопки пишет MESSENGER_CLICK и открывает подтверждение отправки.
- [ ] После подтверждения кнопка становится «Гонец в пути», а по прибытии открывается предложение начать настоящий разговор с лордом.
- [ ] После закрытия энциклопедии слой KaiTORMessengerEncyclopediaLayer удаляется; управление картой не остаётся захваченным.
