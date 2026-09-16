namespace KaiTORPortraitFix
{
    /// <summary>
    /// Disabled in 0.4.1-safe-isolation.
    /// The previous pose experiment attempted to alter BasicCharacterTableau idle animation,
    /// but its IL replacement never applied in 0.4.0. Keep this file as an explicit marker so
    /// no renderer mutation is registered while we isolate the earlier regression.
    /// </summary>
    internal static class BasicPreviewPosePatch
    {
        internal const bool Enabled = false;

        internal static void LogDisabled()
        {
            PortraitFixLog.Event(
                "MUTATOR_DISABLED",
                "name=basic-preview-pose; enabled=false; reason=safe-isolation");
        }
    }
}
