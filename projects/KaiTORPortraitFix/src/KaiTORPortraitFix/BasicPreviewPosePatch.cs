namespace KaiTORPortraitFix
{
    /// <summary>
    /// Disabled in 0.5.1 action-binding diagnostics.
    /// 0.5.0 proved the intended IL replacement did not apply on Bannerlord 1.3.15,
    /// so this build performs no pose mutation. We only observe the runtime tableau,
    /// action-set and animation binding state.
    /// </summary>
    internal static class BasicPreviewPosePatch
    {
        internal const bool Enabled = false;

        internal static void LogDisabled()
        {
            PortraitFixLog.Event(
                "MUTATOR_DISABLED",
                "name=basic-preview-pose; enabled=false; reason=0.5.1-action-binding-diagnostics");
        }
    }
}
