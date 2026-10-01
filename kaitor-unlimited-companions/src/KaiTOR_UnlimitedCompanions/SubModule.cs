using System;
using System.IO;
using System.Reflection;
using Bannerlord.UIExtenderEx;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;

namespace KaiTOR.UnlimitedCompanions
{
    public sealed class SubModule : MBSubModuleBase
    {
        private static readonly Harmony HarmonyInstance = new Harmony("kaitor.unlimitedcompanions");
        private UIExtender _uiExtender;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
            DiagnosticLog.Write("LOAD|v1.3.15.08|reserve=200|patch=Clan.CompanionLimit");

            try
            {
                _uiExtender = UIExtender.Create("KaiTOR_UnlimitedCompanions");
                _uiExtender.Register(Assembly.GetExecutingAssembly());
                _uiExtender.Enable();
                DiagnosticLog.Write("UI|bulk_recall=ready|ui_extender=enabled");
            }
            catch (Exception ex)
            {
                // The companion-limit patch must remain usable even if the optional UI fails.
                DiagnosticLog.Write("UI_ERROR|" + ex.GetType().FullName + "|" + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Clan), nameof(Clan.CompanionLimit), MethodType.Getter)]
    internal static class CompanionLimitGetterPatch
    {
        private const int ReserveSlots = 200;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Clan __instance, ref int __result)
        {
            try
            {
                if (__instance == null || __instance != Clan.PlayerClan)
                    return;

                int currentCount = __instance.Companions != null ? __instance.Companions.Count : 0;
                int minimumLimit = currentCount > int.MaxValue - ReserveSlots
                    ? int.MaxValue
                    : currentCount + ReserveSlots;

                int originalResult = __result;
                if (__result < minimumLimit)
                    __result = minimumLimit;

                DiagnosticLog.WriteAdjustment(currentCount, originalResult, __result);
            }
            catch (Exception ex)
            {
                // Never let a companion-limit helper crash the campaign/UI.
                DiagnosticLog.Write("ERROR|" + ex.GetType().FullName + "|" + ex.Message);
            }
        }
    }

    internal static class DiagnosticLog
    {
        private static readonly object Sync = new object();
        private static int _remainingAdjustmentLines = 32;

        private static string LogPath
        {
            get
            {
                string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return Path.Combine(
                    documents,
                    "Mount and Blade II Bannerlord",
                    "Configs",
                    "KaiTOR_UnlimitedCompanions.log");
            }
        }

        internal static void WriteAdjustment(int currentCount, int originalLimit, int finalLimit)
        {
            if (_remainingAdjustmentLines <= 0)
                return;

            lock (Sync)
            {
                if (_remainingAdjustmentLines <= 0)
                    return;

                _remainingAdjustmentLines--;
                WriteInternal(
                    "LIMIT|companions=" + currentCount +
                    "|original=" + originalLimit +
                    "|final=" + finalLimit);
            }
        }

        internal static void Write(string message)
        {
            lock (Sync)
            {
                WriteInternal(message);
            }
        }

        private static void WriteInternal(string message)
        {
            try
            {
                string path = LogPath;
                string directory = Path.GetDirectoryName(path);
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                File.AppendAllText(
                    path,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "|" + message + Environment.NewLine);
            }
            catch
            {
                // Logging is diagnostic only and must never affect the game.
            }
        }
    }
}
