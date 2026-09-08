using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace KaiTOROnline.Bannerlord1315ApiProbe
{
    /// <summary>
    /// Compile-only probe for the campaign API surface that KaiTOR Online depends on.
    /// If a member disappears or changes signature in the pinned 1.3.15 references,
    /// CI fails here before we touch the much larger Bannerlord Coop source tree.
    /// </summary>
    public static class ApiSurfaceProbe
    {
        public static void ProbePlayerIdentity(Hero hero, MobileParty party)
        {
            Hero mainHero = Hero.MainHero;
            MobileParty mainParty = MobileParty.MainParty;

            Clan clan = hero.Clan;
            PartyBase partyBase = party.Party;
            Settlement settlement = party.CurrentSettlement;
            Vec2 position = party.GetPosition2D;

            _ = mainHero;
            _ = mainParty;
            _ = clan;
            _ = partyBase;
            _ = settlement;
            _ = position;
        }

        public static void ProbeCampaignClock()
        {
            Campaign campaign = Campaign.Current;
            CampaignTime now = CampaignTime.Now;

            _ = campaign;
            _ = now;
        }

        public static void ProbePartyState(Hero hero, MobileParty party)
        {
            bool active = party.IsActive;
            float speed = party.Speed;
            int memberCount = party.MemberRoster.TotalManCount;
            int prisonerCount = party.PrisonRoster.TotalManCount;
            int caravanCount = hero.OwnedCaravans == null ? 0 : hero.OwnedCaravans.Count;

            _ = active;
            _ = speed;
            _ = memberCount;
            _ = prisonerCount;
            _ = caravanCount;
        }
    }
}
