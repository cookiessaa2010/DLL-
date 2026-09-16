# KaiTOR Diplomacy v0.4 safety invariants

- Do not replace TOR diplomacy, alliance, trade, marriage, war, peace, or decision-permission models.
- Preserve the existing fail-closed TOR compatibility gate.
- Preserve v0.3.1 LoadSafe behavior for existing TOR saves.
- Dawi women / pregnancy integration remains disabled; no v0.4 code may activate or depend on it.
- UI actions must call KaiTOR treaty APIs only after compatibility checks pass.
