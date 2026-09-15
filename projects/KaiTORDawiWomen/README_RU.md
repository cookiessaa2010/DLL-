# KaiTOR Dawi Women

Цель: добавить в The Old Realms 1.3.15 настоящих женщин Dawi без человеческих суррогатных моделей и без Blender.

## Архитектура

- `KaiTOR_Diplomacy` уже держит Dawi pregnancy закрытой, пока не зарегистрирован реальный female-dwarf template `kaitor_dawi_woman_lord`.
- Этот проект является отдельным optional asset/runtime-модулем.
- До подтверждения полного набора meshes/skeleton/action sets модуль должен оставаться SAFE-OFF и не регистрировать female template.
- Нельзя переопределять TOR race `dwarf` вслепую: сначала сравнивается skeleton/skin chain TOR и TAOM.

## TAOM-кандидат

Подтверждённые имена из публичного TAOM armory snapshot:

- `dwarf_skeleton_a`
- `as_dwarf_warrior`
- `as_dwarf_female_warrior`
- `sk_dwarf_bm_f1_body`
- `sk_dwarf_bm_f1_shoulder`
- `sk_dwarf_bm_f1_head`
- `sk_dwarf_underwear_female_a`

TAOM female dwarf использует отдельную female body-mesh family, а не уменьшенную human woman.

## Правило брони

Первая версия не предполагает совместимость TOR torso/arms/legs с TAOM rig. Безопасная схема:

- TAOM female dwarf body + совместимая TAOM dwarf armor;
- TOR weapons/shields;
- TOR helmets только после визуального теста;
- TOR body/arm/leg armor только после фактического rig test.

## Что нужно от установленного TAOM

Для автоматического аудита указывается путь к `LOTRLOME_Armory`. Скрипт проверяет XML и AssetPackages на обязательные female-dwarf ресурсы. Он не объявляет пакет READY, если критическая цепочка отсутствует.

После получения реальных asset files следующий этап — минимизация пакета и live-test на Bannerlord/TOR 1.3.15.
