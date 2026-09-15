using System;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;

namespace KaiTOR.Diplomacy.Runtime;

internal static class TorMarketCultureBridge
{
    public static bool Validate(CultureObject targetCulture, Settlement settlement, out string reason)
    {
        reason = string.Empty;

        if (settlement == null || !settlement.IsTown)
            return true;

        if (targetCulture == null)
        {
            reason = "Target culture is null for market conversion.";
            return false;
        }

        var workshops = Campaign.Current?.GetCampaignBehavior<WorkshopsCampaignBehavior>();
        if (workshops == null)
        {
            reason = "Native WorkshopsCampaignBehavior was not found.";
            return false;
        }

        var workshopType = typeof(WorkshopsCampaignBehavior);
        if (workshopType.GetMethod("IsItemPreferredForTown", BindingFlags.Instance | BindingFlags.NonPublic) == null ||
            workshopType.GetMethod("GetRandomItem", BindingFlags.Instance | BindingFlags.NonPublic) == null)
        {
            reason = "Bannerlord 1.3.15 culture-aware workshop market hooks were not found.";
            return false;
        }

        var consumption = Campaign.Current?.GetCampaignBehavior<ItemConsumptionBehavior>();
        if (consumption == null)
        {
            reason = "Native ItemConsumptionBehavior was not found.";
            return false;
        }

        if (typeof(ItemConsumptionBehavior).GetMethod("UpdateSupplyAndDemand", BindingFlags.Static | BindingFlags.NonPublic) == null)
        {
            reason = "Bannerlord 1.3.15 market supply/demand refresh hook was not found.";
            return false;
        }

        return true;
    }

    public static bool Refresh(Settlement settlement, CultureObject targetCulture, out string reason)
    {
        reason = string.Empty;
        if (settlement == null || !settlement.IsTown)
            return true;

        if (!Validate(targetCulture, settlement, out reason))
            return false;

        if (settlement.Culture != targetCulture || settlement.Town.Culture != targetCulture)
        {
            reason = $"Town culture did not switch to '{targetCulture.StringId}' before market refresh.";
            return false;
        }

        try
        {
            // Workshop output selection already filters manufactured merchandise by Town.Culture.
            // Do not purge Settlement.ItemRoster: old-culture stock may include goods sold by the player
            // and should leave the market naturally through consumption/trade rather than being destroyed.
            // We only force the market model to recalculate its supply/demand state immediately.
            var updateSupplyAndDemand = typeof(ItemConsumptionBehavior).GetMethod(
                "UpdateSupplyAndDemand",
                BindingFlags.Static | BindingFlags.NonPublic);

            updateSupplyAndDemand!.Invoke(null, new object[] { settlement.Town });
            return true;
        }
        catch (Exception ex)
        {
            reason = $"Market refresh failed: {ex.GetBaseException().Message}";
            return false;
        }
    }

    public static string Describe(CultureObject culture)
    {
        if (culture == null)
            return "market BLOCKED (culture null)";

        var workshopType = typeof(WorkshopsCampaignBehavior);
        var cultureFilter = workshopType.GetMethod("IsItemPreferredForTown", BindingFlags.Instance | BindingFlags.NonPublic);
        var randomItem = workshopType.GetMethod("GetRandomItem", BindingFlags.Instance | BindingFlags.NonPublic);
        var supplyDemand = typeof(ItemConsumptionBehavior).GetMethod("UpdateSupplyAndDemand", BindingFlags.Static | BindingFlags.NonPublic);

        return cultureFilter != null && randomItem != null && supplyDemand != null
            ? "market FULL"
            : "market BLOCKED (native market hooks missing)";
    }
}
