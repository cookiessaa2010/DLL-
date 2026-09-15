# KaiTOR — расы, браки, потомство и жизненный цикл

Этот документ фиксирует правила для The Old Realms 1.3.15, чтобы KaiTOR возвращал живые династии, не создавая несовместимых детей и не ломая save.

## TOR culture id != лоровая раса

- `empire` = Empire
- `vlandia` = Bretonnia
- `khuzait` = Sylvania
- `mousillon` = Mousillon
- `battania` = Asrai
- `eonir` = Eonir
- `sturgia` = Dawi
- `aserai` = Greenskins

В интерфейсе KaiTOR `sturgia` всегда трактуется как Dawi, а не vanilla Sturgia.

## Женщины и семейная система

| TOR культура | Женщины в лоре | Реализация TOR | Правило KaiTOR |
|---|---:|---|---|
| Empire | Да | женские hero/notable templates есть | брак + безопасное человеческое потомство |
| Bretonnia | Да | Damsel/Prophetess и другие | брак + безопасное человеческое потомство |
| Sylvania | Да | смертные женщины + Lahmian vampires | брак; vampire/undead pregnancy запрещена |
| Mousillon | Да | смертные женские templates, undead/vampire mix | брак; undead/vampire pregnancy запрещена |
| Asrai | Да | female elf templates | брак + elf offspring при совместимом `Race` |
| Eonir | Да | female elf templates | брак + elf offspring при совместимом `Race` |
| Dawi (`sturgia`) | Да, редки | полноценной female Dawi body/template chain сейчас нет; `townswoman_dawi`/`village_woman_dawi` имеют `is_female=false` | брак разрешён, vanilla pregnancy запрещена |
| Greenskins (`aserai`) | нет обычной половой семейной модели | технические female culture slots используют Orc body | обычный брак/pregnancy запрещены |

Неплейабельные Druchii/Chaos/Beastmen пока не включены в автоматическую marriage matrix стабильной версии.

## Все браки мира, а не только PlayerClan

TOR `TORMarriageModel` отключает vanilla marriage полностью. KaiTOR заменяет этот запрет на world marriage policy:

- штатный `RomanceCampaignBehavior` Bannerlord снова рассматривает NPC ↔ NPC пары;
- родство, возраст, текущий супруг, engagement и прочие vanilla ограничения остаются;
- поддерживаемые культуры: Empire / Bretonnia / Sylvania / Mousillon / Asrai / Eonir / Dawi;
- Greenskins и non-vampire undead исключены;
- NPC marriage выполняется самим Bannerlord через его стандартный `MarriageAction`, а не прямым действием KaiTOR;
- одновидовые/биологически безопасные пары используют native NPC marriage chance;
- разрешённые, но бездетные межрасовые/межвидовые союзы у AI получают множитель шанса `0.25`, поэтому встречаются, но не заполняют весь мир.

Так в мире могут появляться, например, Dawi ↔ Human, Dawi ↔ Elf, Human ↔ Elf и Vampire ↔ mortal социальные союзы.

## Межрасовый брак != гибридный ребёнок

Stable policy KaiTOR:

- межрасовый/межвидовой брак допустим как социальный и династический союз;
- гибридных детей нет;
- KaiTOR не создаёт half-dwarf/half-elf/custom race;
- `KaiPregnancyModel` проверяет **все браки мира**, а не только PlayerClan.

Vanilla pregnancy допускается только когда:

1. `CharacterObject.Race` родителей совпадает;
2. ни один родитель не Dawi;
3. ни один родитель не Greenskin;
4. ни один родитель не Vampire;
5. ни один родитель не Undead.

После этого шанс/длительность/двойня/пол/материнская смертность остаются полностью у исходного Bannerlord/TOR `PregnancyModel`.

Причина дополнительной технической защиты: Bannerlord 1.3.x `HeroCreator.DeliverOffSpring` требует одинаковый `CharacterObject.Race` родителей.

## Warning для игрока

Если player-facing marriage разрешён, но biological offspring запрещён, перед финальным marriage barter показывается warning:

> брак разрешён, но эта пара не сможет иметь биологических детей; KaiTOR не будет генерировать потомство для этой пары.

Игрок может продолжить или отменить финальную стадию. Для обходных arranged/barter путей остаётся резервное информационное сообщение на `BeforeHeroesMarried`.

Warning behavior ничего не сериализует.

## Религия после брака

Свадьба не конвертирует религию супруга.

TOR хранит религию на Hero. Для новых nobles TOR сам определяет dominant religion в порядке:

1. отец;
2. clan leader;
3. culture.

KaiTOR не переписывает этот механизм. Поэтому межрасовый/межкультурный союз не означает автоматическое обращение в веру супруга.

## Жизненный цикл

TOR 1.3.15 сам устанавливает `CampaignOptions.IsLifeDeathCycleDisabled = true`, поэтому без KaiTOR:

- `Hero.Age` остаётся на `_defaultAge`;
- естественное старение фактически заморожено;
- old-age death не работает;
- vanilla pregnancy/child growth lifecycle не может полноценно работать.

KaiTOR после загрузки TOR возвращает `IsLifeDeathCycleDisabled = false`.

### Естественная смерть по расам

KaiTOR не меняет battle death. `KaiHeroDeathProbabilityModel` меняет только natural old-age mortality:

- Empire/Bretonnia и смертные Sylvania/Mousillon: исходная human curve активного Bannerlord/TOR model;
- Dawi: old age начинает учитываться после ~180 лет, hard cap ~420;
- Asrai/Eonir: vanilla human old-age death = 0 в горизонте кампании;
- Vampire/Undead: old-age death = 0;
- Greenskins: old-age death = 0.

TOR `TORCampaignTimeModel` сохраняется без изменений. В обычном режиме его год = 84 campaign days; в Fast calendar = 24 campaign days, поэтому поколения могут реально меняться в долгой кампании.

### Нерешённое противоречие: возраст совершеннолетия

Bannerlord `AgeModel` глобальный и не принимает Hero/race. Сейчас child stages остаются vanilla:

- infant 3;
- child 6;
- teenager 14;
- adult 18.

Это удобно для gameplay, но не является точным возрастом взросления долгоживущих рас. Пока KaiTOR трактует 18 как **игровой возраст полной активности героя**, а не буквальную лоровую биологическую зрелость эльфа/Dawi. Если мы захотим race-specific maturity, потребуется отдельный behavior для marriage/party/governorship/education activation.

## Dawi

Warhammer Dwarf women существуют и редки, но TOR 1.3.15 не даёт безопасной female Dawi template chain. Поэтому:

- Dawi могут вступать в брак с Human/Elf;
- такие союзы бездетны;
- Dawi vanilla pregnancy вообще отключена;
- династическая устойчивость Dawi поддерживается долгой жизнью и механизмом новых/cadet houses, а не генерацией несуществующих female templates.

## Vampires

Female vampires существуют и TOR их реализует. Social marriage разрешён. Обычная Bannerlord pregnancy запрещена. Vampire/Undead определяется через TOR `HeroExtensions` reflection bridge.

## Greenskins

Greenskins не участвуют в обычной marriage/pregnancy системе. Их фракции могут продолжать развиваться через политическое перемещение clans и создание новых vassal houses/boss clans, но не через human family reproduction.

## Save safety

- KaiTOR не сериализует custom Hero/Child/Race graph;
- pregnancy guard не хранит state;
- marriage warning не хранит state;
- lifecycle использует существующие `BirthDay/Age` данные Bannerlord;
- new AI house cooldown хранится как primitive `Dictionary<string,double>`;
- созданные новые clans — штатные Bannerlord `Clan` objects, а не custom save types;
- existing TOR families не мигрируются насильно.

## Гибридные дети

В стабильной версии их не будет. Изменение этого правила потребовало бы отдельного offspring pipeline: explicit race/body, template, equipment, age stages, inheritance и многократный save/load/ взросление test.
