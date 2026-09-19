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
    private static PropertyInfo _enlistingLordProperty;
    private static PropertyInfo _durationProperty;
    private static PropertyInfo _victoriesProperty;
    private static Type _heroExtensionsType;
    private static MethodInfo _getCareerMethod;

    private static object GetBehavior()
    {
        try
        {
            var campaign = Campaign.Current;
            if (campaign == null)
                return null;

            _behaviorType ??= Type.GetType(BehaviorTypeName, false);
            if (_behaviorType == null)
                return null;

            _getCampaignBehaviorGeneric ??= typeof(Campaign)
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                    m.Name == "GetCampaignBehavior" &&
                    m.IsGenericMethodDefinition &&
                    m.GetParameters().Length == 0);

            return _getCampaignBehaviorGeneric?
                .MakeGenericMethod(_behaviorType)
                .Invoke(campaign, null);
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("TOR_HIRELING_BRIDGE_FAILED", ex, "stage=GetBehavior");
            return null;
        }
    }

    public static Hero GetEnlistingLord()
    {
        try
        {
            var behavior = GetBehavior();
            if (behavior == null) return null;
            _enlistingLordProperty ??= _behaviorType.GetProperty(
                "EnlistingLord", BindingFlags.Instance | BindingFlags.Public);
            return _enlistingLordProperty?.GetValue(behavior) as Hero;
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("TOR_HIRELING_BRIDGE_FAILED", ex, "stage=GetEnlistingLord");
            return null;
        }
    }

    public static float GetDurationDays()
    {
        try
        {
            var behavior = GetBehavior();
            if (behavior == null) return 0f;
            _durationProperty ??= _behaviorType.GetProperty(
                "DurationInDays", BindingFlags.Instance | BindingFlags.Public);
            var value = _durationProperty?.GetValue(behavior);
            return value is float f ? Math.Max(0f, f) : 0f;
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("TOR_HIRELING_BRIDGE_FAILED", ex, "stage=GetDurationDays");
            return 0f;
        }
    }

    public static int GetTorCountedVictories()
    {
        try
        {
            var behavior = GetBehavior();
            if (behavior == null) return 0;
            _victoriesProperty ??= _behaviorType.GetProperty(
                "ManuallyFoughtBattles", BindingFlags.Instance | BindingFlags.Public);
            var value = _victoriesProperty?.GetValue(behavior);
            return value is int i ? Math.Max(0, i) : 0;
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("TOR_HIRELING_BRIDGE_FAILED", ex, "stage=GetVictories");
            return 0;
        }
    }

    public static string GetCareerId()
    {
        try
        {
            _heroExtensionsType ??= Type.GetType("TOR_Core.Extensions.HeroExtensions, TOR_Core", false);
            _getCareerMethod ??= _heroExtensionsType?.GetMethod(
                "GetCareer",
                BindingFlags.Static | BindingFlags.Public,
                null,
                new[] { typeof(Hero) },
                null);

            var career = _getCareerMethod?.Invoke(null, new object[] { Hero.MainHero });
            if (career == null) return "none";

            var property = career.GetType().GetProperty("StringId", BindingFlags.Instance | BindingFlags.Public);
            return property?.GetValue(career)?.ToString() ?? "unknown";
        }
        catch (Exception ex)
        {
            KaiRuntimeLog.Exception("TOR_HIRELING_BRIDGE_FAILED", ex, "stage=GetCareerId");
            return "unknown";
        }
    }

    public static bool IsEnlisted()
    {
        try
        {
            var behavior = GetBehavior();
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
