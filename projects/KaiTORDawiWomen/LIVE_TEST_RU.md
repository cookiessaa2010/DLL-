# KaiTOR Dawi Women — первый live-test

Цель первого прогона — не сразу включить рождение Dawi, а доказать всю render-chain: female dwarf body -> skeleton -> animations -> face -> compatible equipment -> save/load.

## Фаза 0 — SAFE-OFF

С установленным только `KaiTOR_Diplomacy` команда:

```text
kaitor_dawi_women.status
```

должна показать:

- asset gate `MISSING/SAFE-OFF`;
- population behavior `ACTIVE`;
- Dawi pregnancy `blocked`.

Это нормальное состояние. Никаких surrogate human women создаваться не должно.

## Фаза 1 — аудит локальных asset-паков

Запустить:

```powershell
.\Tools\Audit-DawiAssets.ps1 `
  -TaomArmoryPath "D:\...\Modules\LOTRLOME_Armory" `
  -TorArmoryPath "D:\...\Modules\TOR_Armory" `
  -DeepScan `
  -ReportPath ".\dawi_asset_report.json"
```

До дальнейших действий обязательны TAOM resources:

- `dwarf_skeleton_a`
- `as_dwarf_female_warrior`
- `sk_dwarf_bm_f1_body`
- `sk_dwarf_bm_f1_shoulder`
- `sk_dwarf_bm_f1_head`
- `sk_dwarf_underwear_female_a`

`READY_FOR_MANUAL_RIG_TEST` означает только, что цепочка найдена. Это ещё не означает совместимость TOR armor.

## Фаза 2 — выделение candidate TPAC

```powershell
.\Tools\Stage-DawiAssetCandidates.ps1 `
  -TaomArmoryPath "D:\...\Modules\LOTRLOME_Armory" `
  -OutputPath ".\DawiAssetStage"
```

Папка `AssetPackages_CANDIDATE_ONLY` не является готовым модом. TPAC могут иметь зависимости на материалы/текстуры в других пакетах TAOM.

## Фаза 3 — один тестовый female Dawi

После сборки реального asset-модуля регистрируется только sentinel template:

`kaitor_dawi_woman_lord`

Критерии:

1. `is_female=true`;
2. `race=dwarf`;
3. реальный registered dwarf race, не human fallback;
4. female dwarf body не пропадает в Encyclopedia/scene;
5. нет CTD при civilian equipment;
6. нет CTD при battle equipment.

До выполнения этих условий автоматическое создание женщин и Dawi pregnancy не считаются release-ready.

## Фаза 4 — armor matrix

Проверить отдельно:

| Слот | Первый источник | Результат |
|---|---|---|
| body/torso | TAOM dwarf-compatible | обязателен первый PASS |
| arms/gloves | TAOM dwarf-compatible | обязателен первый PASS |
| legs/boots | TAOM dwarf-compatible | обязателен первый PASS |
| weapon | TOR Dawi | проверить attachment/grip |
| shield | TOR Dawi | проверить hand/strap/position |
| helmet | TOR Dawi | проверить head size/offset/clipping |
| TOR torso | TOR | только эксперимент после базового PASS |

На первом этапе TOR torso/arms/legs не считаются совместимыми автоматически.

## Фаза 5 — animation matrix

Одна female Dawi должна пройти без geometry/animation faults:

- idle в settlement;
- ходьба/бег;
- разговор;
- one-handed weapon;
- shield;
- two-handed axe/hammer;
- ranged weapon только если её equipment это использует;
- получение удара;
- падение/нокаут;
- mount не требуется для Dawi baseline.

## Фаза 6 — семья и save

Только после render PASS:

1. `kaitor_dawi_women.status` -> `READY`;
2. прожить несколько weekly ticks;
3. AI Dawi clan может получить ограниченное число adult female nobles;
4. PlayerClan автоматически не населять;
5. оформить Dawi <-> Dawi marriage;
6. проверить pregnancy;
7. сохранить/загрузить минимум 3 раза;
8. ребёнок должен иметь настоящий `dwarf` race;
9. ни один Dawi <-> Human/Elf брак не должен получить biological child.

## Stop conditions

Немедленно вернуть asset gate в SAFE-OFF, если обнаружено:

- human fallback body;
- invisible body/head;
- missing geometry;
- CTD на FaceGen/underwear/equipment;
- сильный skeleton deformation;
- TOR torso tearing/weight explosion;
- ребёнок неправильной race;
- save перестаёт загружаться.
