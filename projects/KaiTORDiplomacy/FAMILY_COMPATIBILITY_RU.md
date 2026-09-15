# KaiTOR — расы, женщины, браки и безопасное потомство

Этот документ фиксирует правила для The Old Realms 1.3.15, чтобы KaiTOR не создавал несовместимые семьи/детей и не повреждал кампанию.

## Важное различие: культура TOR != раса Bannerlord

TOR переиспользует vanilla culture id:

- `empire` = Empire
- `vlandia` = Bretonnia
- `khuzait` = Sylvania
- `mousillon` = Mousillon
- `battania` = Asrai
- `eonir` = Eonir
- `sturgia` = Dawi
- `aserai` = Greenskins

В интерфейсе KaiTOR эти vanilla-названия не должны трактоваться буквально. Например `sturgia` — это Dawi, а не Стургия Bannerlord.

## Аудит женщин: лор + фактические шаблоны TOR

| TOR культура | Лоровая раса/общество | Женщины в лоре | Женские шаблоны TOR | Решение KaiTOR |
|---|---|---:|---:|---|
| Empire (`empire`) | люди Империи | Да | Да | Брак разрешён |
| Bretonnia (`vlandia`) | бретоннские люди | Да | Да: Damsel/Prophetess и др. | Брак разрешён |
| Sylvania (`khuzait`) | смертные + вампиры | Да | Да: смертные женщины и Lahmian vampires | Брак разрешён; вампирам pregnancy запрещена |
| Mousillon (`mousillon`) | смертные + нежить/вампиры | Да | Да: Hedge Witch и другие женские шаблоны | Брак разрешён; нежити pregnancy запрещена |
| Asrai (`battania`) | эльфы Athel Loren | Да | Да | Брак разрешён |
| Eonir (`eonir`) | эльфы Laurelorn | Да | Да | Брак разрешён |
| Dawi (`sturgia`) | дворфы | Да, но редки | **Нет полноценной female Dawi реализации в текущем TOR**: `townswoman_dawi`/`village_woman_dawi` имеют `is_female=false` | Брак разрешён, включая Dawi ↔ Human/Elf; vanilla pregnancy запрещена |
| Greenskins (`aserai`) | Orc/Goblin Greenskins | Нет обычной половой/семейной модели; споровое размножение | Нет настоящих female Orc шаблонов; системные `townswoman` slots остаются Orc male body | Брак и pregnancy запрещены |

### Неплейабельные/вспомогательные культуры TOR

- Druchii: женщины существуют (Sorceresses, Witch Elves и т.п.), но культура не входит в TOR playable `Cultures.All`; KaiTOR marriage пока не включает её.
- Chaos/Norsca: женщины существуют в лоре, но TOR `chaos_culture` не входит в нашу поддерживаемую player-family матрицу.
- Beastmen: Beastwomen существуют в лоре, но TOR beastmen — отдельные неплейабельные race/culture templates; семейная механика KaiTOR их не включает.
- Skeleton/Wight и прочая нежить: биологическое потомство запрещено; non-vampire undead не считается допустимым супругом KaiTOR.

## Матрица брака

KaiTOR разрешает player-clan social marriage между любыми героями из поддерживаемых семи культур:

`Empire / Bretonnia / Sylvania / Mousillon / Asrai / Eonir / Dawi`

при условии, что стандартный `DefaultMarriageModel` Bannerlord также считает конкретных героев подходящими по возрасту, полу, текущему семейному состоянию и другим vanilla-условиям.

Это намеренно позволяет, например:

- Dawi ↔ Empire
- Dawi ↔ Bretonnia
- Dawi ↔ Asrai/Eonir
- Human ↔ Elf
- Human ↔ Vampire-culture hero
- Vampire-culture ↔ Vampire-culture

Greenskins исключены полностью из marriage/family pipeline.

NPC-to-NPC автоматические династические браки остаются выключенными, как и в TOR. KaiTOR открывает только семьи, связанные с `Clan.PlayerClan`.

## Матрица деторождения

Брак не означает автоматическую совместимость с Bannerlord offspring pipeline.

KaiTOR позволяет vanilla pregnancy только если одновременно выполняется всё:

1. брак связан с player clan;
2. оба героя принадлежат поддерживаемым marriage-культурам;
3. `CharacterObject.Race` обоих родителей **совпадает**;
4. ни один родитель не Dawi (`sturgia`);
5. ни один родитель не Greenskin (`aserai`);
6. TOR не считает ни одного родителя Vampire;
7. TOR не считает ни одного родителя Undead.

После этих проверок KaiTOR полностью делегирует шанс беременности оригинальному активному `PregnancyModel` Bannerlord/TOR.

### Почему cross-race marriage не получает vanilla child

Bannerlord 1.3.x `HeroCreator.DeliverOffSpring` содержит проверку равенства `mother.CharacterObject.Race` и `father.CharacterObject.Race`. Поэтому Dawi ↔ Elf или Dawi ↔ Human безопасно поженить, но нельзя отправлять такую пару в стандартный child generator.

Это не ограничение лора KaiTOR, а защитный технический барьер. В будущем отдельная система hybrid offspring может задавать шаблон ребёнка явно, но она не должна внедряться в стабильную версию до отдельного тестового цикла.

## Dawi: специальное решение

В Warhammer Dwarf women существуют и являются редкими. Однако TOR 1.3.15 не предоставляет нормальную female Dawi body/template цепочку для семейной генерации. Поэтому KaiTOR:

- не придумывает собственную dwarf female модель;
- не меняет TOR XML;
- разрешает Dawi вступать в брак с Human/Elf;
- блокирует vanilla pregnancy для Dawi, чтобы не породить героя с неправильным body/race/equipment template.

## Vampires: специальное решение

Женщины-вампиры каноничны и TOR их реализует (например Lahmian templates). Брак разрешён. Но Vampire — нежить; KaiTOR не использует обычную беременность Bannerlord для vampire hero.

Проверка Vampire/Undead выполняется через runtime reflection к `TOR_Core.Extensions.HeroExtensions`. Если TOR изменит контракт и vampire hook не удастся определить, KaiTOR fail-closed блокирует pregnancy для Sylvania/Mousillon вместо риска создать некорректного ребёнка.

## Greenskins: специальное решение

TOR содержит технические `townswoman_greenskins`/`village_woman_greenskins`, потому что Bannerlord требует соответствующие culture slots. Это не доказательство наличия Orc women: эти персонажи используют Orc body и не объявлены female. KaiTOR не трактует их как женщин и не включает Greenskins в marriage/pregnancy.

## Save-safety

- KaiTOR не сериализует новые Hero/Child/Race объекты.
- Pregnancy guard не записывает собственное состояние в save.
- Неопасные пары используют оригинальный PregnancyModel без изменения его коэффициентов.
- Небезопасные player-family пары получают chance `0` до `MakePregnantAction` и до `HeroCreator.DeliverOffSpring`.
- Мир TOR вне player clan не переписывается.
- Уже существующие TOR семьи/отношения не мигрируются и не изменяются автоматически.

## Что потребуется для настоящих межрасовых детей

Если позже понадобится ребёнок Dawi + Elf/Human, это отдельный этап. Понадобится явная политика наследования race/body, отдельный offspring template, оборудование/возрастные модели и тест save/load/ взросления. Делать это через обход vanilla assert без такой системы нельзя.
