namespace KaiTORPortraitFix
{
    /// <summary>
    /// The 0.5.1 deep reflection diagnostics isolated the failure window. They are disabled in the
    /// 0.5.2 recovery test to keep the log focused on static ActionIndexCache state and recovery.
    /// </summary>
    internal static class ActionBindingDiagnostics
    {
        internal const bool Enabled = false;

        internal static void LogDisabled()
        {
            PortraitFixLog.Event(
                "DIAGNOSTIC_DISABLED",
                "name=action-binding-deep-scan; enabled=false; reason=0.5.2-static-action-cache-recovery");
        }
    }
}
