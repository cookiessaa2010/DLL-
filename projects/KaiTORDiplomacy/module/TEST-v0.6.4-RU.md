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
- [ ] Город -> Семейные дела открывается.
- [ ] Замок -> Семейные дела открывается.
- [ ] Мой род -> состав рода открывается.
- [ ] Брачные союзы: член рода -> чужой дом -> кандидат -> barter.
- [ ] Обычная свадьба проходит.
- [ ] Принять в род показывает кандидатов.
- [ ] AdoptHeroAction проходит; ребёнок виден в family tree.
- [ ] Save/load сохраняет усыновление.
- [ ] Female+female player-house marriage проходит.
- [ ] Female+female пара не получает обычную беременность.
- [ ] После save/load spouse/clan/family tree корректны.

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
- [ ] Dawi женщина старше 45, но внутри Dawi fertility window, может получить ненулевой pregnancy chance.
- [ ] Dawi беременность проходит.
- [ ] Рождается Dawi ребёнок.
- [ ] Только после visual test можно включать automatic Dawi women population.

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
- [ ] Political marriage требует минимум 500 000.
- [ ] 500 000 уходят в escrow перед barter.
- [ ] Cancel barter -> полный refund.
- [ ] Successful marriage -> escrow получает другой дом.
- [ ] Dynastic Bond активен 180 дней.
- [ ] +20 relation.
- [ ] +30 trust.
- [ ] Объявление войны при активном Dynastic Bond снимает узы и отзывает их +30 trust; NAP breach при наличии NAP применяется отдельно.
- [ ] Между разными kingdom используется существующий NAP backend.
- [ ] Incoming AI marriage proposal можно принять/отклонить; auto-marriage отсутствует.
- [ ] AI не предлагает female+female автоматически.
- [ ] AI -> Player NAP появляется только если PlayerClan правит kingdom.
- [ ] Входящий NAP проходит через player council и не списывает 150 влияния с игрока.
- [ ] Ruler conversation позволяет предложить NAP 30/60/90/180.
- [ ] Старый Kingdom UI NAP по-прежнему проходит полный проверенный цикл.

## Mercy
- [ ] Освобождение побеждённого лорда по выбору игрока даёт +50 relation.
- [ ] Побег/автоматическое/чужое освобождение не даёт +50.

## Regression
- [ ] Смена народности поселения по-прежнему работает.
- [ ] Старый NAP по-прежнему работает.
- [ ] SAVE -> EXIT -> LOAD: семьи, NAP, cooldown, children, realm houses, dynastic bonds intact.

## Long simulation
- [ ] 1 год.
- [ ] 5 лет.
- [ ] 20 лет.
Проверить crash, семьи, population, браки, рождения, старение, новые кланы, договоры, cooldown и save integrity.


## UI hardening live-test
- [ ] В городе и замке видна кнопка «Управление державой KaiTOR».
- [ ] Кнопка открывает hub, «Назад» возвращает в исходное меню.
- [ ] «Семейные дела» из hub открываются и возвращаются обратно без мёртвого клика.
- [ ] «Выбрать державу для пакта» показывает список; выбранная держава отражается в меню.
- [ ] «Срок пакта» меняется между 30/60/90/180 днями.
- [ ] «Внести предложение о пакте» создаёт решение и всегда показывает результат.
- [ ] Активный пакт можно выбрать и вынести его разрыв на совет.
- [ ] «Народность владения» открывает существующее подтверждение смены культуры.
- [ ] `kaitor_diplomacy.ui_status` показывает состояние UI behaviors.
- [ ] `kaitor_diplomacy.ui_family` принудительно открывает «Семейные дела».
- [ ] При невозможности открыть нативный council UI нет бесшумного клика: появляется fallback-сообщение.


## Gauntlet UI / anti-collision live-test
- [ ] В городе, town_outside и замке видна кнопка «KaiTOR: Дипломатия и династия».
- [ ] Кнопка открывает именно `KaiTORDiplomacyHubUIMovie`, а не Co-op movie.
- [ ] Co-op main menu / Join / Options продолжают открывать `CoopConnectionUIMovie` и `CoopOptionsUIMovie` без изменений.
- [ ] Переключаются вкладки Обзор / Дипломатия / Семья / Держава и население.
- [ ] Список держав скроллится; выбор обновляет NAP/trust/breach/cooldown.
- [ ] Срок NAP циклически меняется 30 → 60 → 90 → 180 → 30.
- [ ] «Предложить пакт» создаёт council decision, а не прямой договор.
- [ ] При активном NAP кнопка разрыва создаёт council decision.
- [ ] «Открыть семейные дела» закрывает Gauntlet screen и открывает Family Affairs без мёртвого клика.
- [ ] «Сменить народность» закрывает screen и открывает существующий culture dialog.
- [ ] Dawi live-test из вкладки населения показывает имя + клан + поселение.
- [ ] Созданная Dawi-женщина реально присутствует в HeroesWithoutParty указанного поселения.
- [ ] После save/load Dawi-женщина, NAP, династические связи и UI runtime продолжают работать.
- [ ] `kaitor_diplomacy.ui_status` показывает `KaiTORDiplomacyHubUIMovie` и `KaiTORDiplomacyLayer`.
- [ ] В логах нет обращений Diplomacy к `CoopConnectionUIMovie`, `CoopOptionsUIMovie` или `GameInterface.Services.UI`.


## Единый лог live-test
- [ ] В начале теста выполнить `kaitor_diplomacy.live_test_start`.
- [ ] Команда возвращает путь `%LOCALAPPDATA%\KaiTORDiplomacy\KaiTOR-LiveTest.log`.
- [ ] После открытия KaiTOR UI присутствуют события `GAUNTLET_UI_OPEN` / UI navigation.
- [ ] После NAP/разрыва присутствуют council/NAP события и snapshot отражает актуальный trust/breach/cooldown.
- [ ] После семейных действий присутствуют marriage/dynastic события.
- [ ] После теста +50 присутствует `MERCY_RELEASE` с relationBefore/relationAfter и actualDelta.
- [ ] После Dawi spawn присутствует `DAWI_WOMAN_CREATE` с именем/кланом/поселением.
- [ ] Save пишет `SAVE_STARTED` и `SAVE_OVER success=True`; после него автоматически идёт snapshot.
- [ ] После reload есть `GAME_LOADED` и новый snapshot с теми же persistent состояниями.
- [ ] В конце выполнить `kaitor_diplomacy.live_test_snapshot` и передать один файл `KaiTOR-LiveTest.log`.
