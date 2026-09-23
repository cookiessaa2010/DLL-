using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.MountAndBlade;

namespace KaiTOR.UnlimitedCompanions
{
    public sealed class SubModule : MBSubModuleBase
    {
        private static readonly Harmony HarmonyInstance = new Harmony("kaitor.unlimitedcompanions");

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            HarmonyInstance.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    [HarmonyPatch(typeof(DefaultClanTierModel), "GetCompanionLimit", new Type[] { typeof(Clan) })]
    internal static class CompanionLimitPatch
    {
        private const int UnlimitedCompanionLimit = 2500;

        private static void Postfix(Clan clan, ref int __result)
        {
            if (clan == Clan.PlayerClan && __result < UnlimitedCompanionLimit)
            {
                __result = UnlimitedCompanionLimit;
            }
        }
    }
}
