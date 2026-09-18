using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Party;

namespace KaiTOR.Diplomacy.Runtime;

internal static class KaiMarriageBarterBridge
{
    public static bool TryStart(Hero member, Hero target, Clan targetClan, out string reason)
    {
        reason = string.Empty;
        var model = Campaign.Current?.Models?.MarriageModel;
        var mainHero = Hero.MainHero;

        if (model == null || mainHero == null || member == null || target == null || targetClan == null)
        {
            reason = "Брачные переговоры сейчас недоступны.";
            return false;
        }
        if (member.Clan != Clan.PlayerClan)
        {
            reason = "Выбранный герой больше не принадлежит вашему дому.";
            return false;
        }
        if (target.Clan != targetClan || targetClan.Leader == null || !targetClan.Leader.IsAlive || targetClan.Leader.IsPrisoner)
        {
            reason = "Другой дом сейчас не может вести брачные переговоры.";
            return false;
        }
        if (!model.IsSuitableForMarriage(member) || !model.IsSuitableForMarriage(target) ||
            !model.IsCoupleSuitableForMarriage(member, target))
        {
            reason = "Эта пара больше не соответствует условиям брака.";
            return false;
        }

        try
        {
            var current = Romance.GetRomanticLevel(member, target);
            if (current < Romance.RomanceLevelEnum.MatchMadeByFamily)
                ChangeRomanticStateAction.Apply(member, target, Romance.RomanceLevelEnum.MatchMadeByFamily);

            var leader = targetClan.Leader;
            var marriage = new MarriageBarterable(
                mainHero,
                PartyBase.MainParty,
                member,
                target);

            BarterManager.Instance.StartBarterOffer(
                mainHero,
                leader,
                PartyBase.MainParty,
                leader.PartyBelongedTo?.Party,
                null,
                (barterable, data, _) =>
                    BarterManager.Instance.InitializeMarriageBarterContext(
                        barterable,
                        data,
                        new Tuple<Hero, Hero>(member, target)),
                0,
                false,
                new Barterable[] { marriage });

            KaiRuntimeLog.Write(
                "MARRIAGE_BARTER_BEGIN",
                $"member={member.StringId}; target={target.StringId}; leader={leader.StringId}; targetClan={targetClan.StringId}");
            return true;
        }
        catch (Exception ex)
        {
            reason = "Брачные переговоры не удалось начать.";
            KaiRuntimeLog.Exception(
                "MARRIAGE_FAILED",
                ex,
                $"stage=bridge_start; member={member.StringId}; target={target.StringId}; clan={targetClan.StringId}");
            return false;
        }
    }
}
