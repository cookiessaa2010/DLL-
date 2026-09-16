namespace KaiTORPortraitFix
{
    /// <summary>
    /// Disabled in 0.4.1-safe-isolation.
    /// This patch used to transpile BasicCharacterTableau.RefreshCharacterTableau. Because the
    /// user's non-save UI poses were originally correct, we remove this renderer mutation while
    /// keeping the SavedGameVM preview-code restore and read-only diagnostics active.
    /// </summary>
    internal static class SavePreviewGenderPatch
    {
        internal const bool Enabled = false;

        internal static void LogDisabled()
        {
            PortraitFixLog.Event(
                "MUTATOR_DISABLED",
                "name=save-preview-gender; enabled=false; reason=safe-isolation");
        }
    }
}
