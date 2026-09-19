using System;
using System.Linq;
using System.Reflection;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

internal static class TorHirelingBridge
{
    private const string BehaviorTypeName =
        "TOR_Core.CampaignMechanics.ServeAsAHireling.ServeAsAHirelingCampaignBehavior, TOR_Core";

    private static Type _behaviorType;
    private static MethodInfo _getCampaignBehaviorGeneric;
    private static MethodInfo _isEnlistedMethod;

    public static bool IsEnlisted()
    {
        try
        {
            var campaign = Campaign.Current;
            if (campaign == null)
                return false;

            _behaviorType ??= Type.GetType(BehaviorTypeName, false);
            if (_behaviorType == null)
                return false;

            _getCampaignBehaviorGeneric ??= typeof(Campaign)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                    m.Name == "GetCampaignBehavior" &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 0);

            if (_getCampaignBehaviorGeneric == null)
                return false;

            var behavior = _getCampaignBehaviorGeneric
                .MakeGenericMethod(_behaviorType)
                .Invoke(campaign, null);

            if (behavior == null)
                return false;

            _isEnlistedMethod ??= _behaviorType.GetMethod(
                "IsEnlisted",
                BindingFlags.Instance | BindingFlags.Public);

            return _isEnlistedMethod?.Invoke(behavior, null) as bool? == true;
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("TOR_HIRELING_BRIDGE_FAILED", ex, "stage=IsEnlisted");
            return false;
        }
    }
}
