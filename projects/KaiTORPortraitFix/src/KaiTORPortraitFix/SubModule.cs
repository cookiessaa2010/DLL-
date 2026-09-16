using System;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace KaiTORPortraitFix
{
    public sealed class SubModule : MBSubModuleBase
    {
        private const string HarmonyId = "com.kaitor.bannerlord.portraitfix";
        private static bool _patched;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            PortraitFixLog.Event(
                "SESSION_START",
                "KaiTOR Portrait Fix 0.6.0-stable; Bannerlord=1.3.15.110062; scope=SavedGameVM preview restore + targeted ActionIndexCache inventory-idle repair; diagnostics=false");

            if (_patched)
                return;

            try
            {
                var harmony = new Harmony(HarmonyId);
                harmony.PatchAll(typeof(SubModule).Assembly);
                _patched = true;

                PortraitFixLog.Event(
                    "PATCH_APPLY",
                    "success=true; harmonyId=" + HarmonyId +
                    "; vmPreviewRestore=true; actionIndexCacheRepair=true; runtimeFallbacks=true; diagnostics=false");
            }
            catch (Exception ex)
            {
                _patched = false;
                PortraitFixLog.Event("PATCH_APPLY", "success=false; " + ex);
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            PortraitFixLog.Event(
                "COLD_MENU_READY",
                "patchInstalled=" + _patched + "; log=" + PortraitFixLog.PathName);
        }
    }
}
