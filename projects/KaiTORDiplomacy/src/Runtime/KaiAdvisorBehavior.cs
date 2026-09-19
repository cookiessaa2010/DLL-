using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Read-only political council. It derives advice exclusively from information already
/// available to the player-facing campaign systems and never executes decisions.
/// </summary>
public sealed class KaiAdvisorBehavior : CampaignBehaviorBase
{
    public override void RegisterEvents() { }
    public override void SyncData(IDataStore dataStore) { }

    public IEnumerable<string> GetAdvice()
    {
        var playerClan = Clan.PlayerClan;
        var kingdom = playerClan?.Kingdom;
        if (playerClan == null || kingdom == null)
        {
            yield return "Your clan does not currently belong to a kingdom.";
            yield break;
        }

        var war = Campaign.Current?.GetCampaignBehavior<KaiWarExhaustionBehavior>();
        if (war != null)
        {
            foreach (var enemy in Kingdom.All
                         .Where(k => k != null && !k.IsEliminated && k != kingdom && kingdom.IsAtWarWith(k))
                         .OrderBy(k => k.StringId, StringComparer.Ordinal))
            {
                var ours = war.GetExhaustion(kingdom, enemy);
                var theirs = war.GetExhaustion(enemy, kingdom);
                if (ours >= 65f)
                    yield return $"War with {enemy.Name}: our exhaustion is high ({ours:0}%). Peace should be considered.";
                else if (theirs - ours >= 25f)
                    yield return $"War with {enemy.Name}: their exhaustion ({theirs:0}%) substantially exceeds ours ({ours:0}%).";
            }
        }

        var diplomacy = Campaign.Current?.GetCampaignBehavior<KaiDiplomacyBehavior>();
        if (diplomacy?.RuntimeEnabled == true)
        {
            foreach (var target in Kingdom.All
                         .Where(k => k != null && !k.IsEliminated && k != kingdom && !kingdom.IsAtWarWith(k))
                         .OrderByDescending(k => diplomacy.GetNapAcceptanceScore(kingdom, k))
                         .Take(3))
            {
                var score = diplomacy.GetNapAcceptanceScore(kingdom, target);
                if (score >= 35 && !diplomacy.IsNonAggressionPactActive(kingdom, target))
                    yield return $"{target.Name} appears receptive to a non-aggression pact (assessment {score}).";
            }

            foreach (var target in Kingdom.All
                         .Where(k => k != null && k != kingdom && diplomacy.IsNonAggressionPactActive(kingdom, k)))
            {
                var trust = diplomacy.GetTrust(kingdom, target);
                if (trust >= 40)
                    yield return $"The pact with {target.Name} rests on strong accumulated trust ({trust}).";
            }
        }

        var dynasty = Campaign.Current?.GetCampaignBehavior<KaiDynasticMarriageBehavior>();
        if (dynasty != null)
        {
            foreach (var other in Kingdom.All
                         .Where(k => k != null && !k.IsEliminated && k != kingdom)
                         .Select(k => new { Kingdom = k, Strength = dynasty.GetDynasticStrength(kingdom, k) })
                         .Where(x => x.Strength >= 35f)
                         .OrderByDescending(x => x.Strength)
                         .Take(3))
            {
                yield return $"The ruling houses of {kingdom.Name} and {other.Kingdom.Name} have significant dynastic ties ({other.Strength:0}/100).";
            }
        }

        foreach (var clan in kingdom.Clans
                     .Where(c => c != null && !c.IsEliminated && !c.IsUnderMercenaryService && c.IsNoble)
                     .OrderBy(c => c.StringId, StringComparer.Ordinal))
        {
            var adultHeirs = clan.Heroes.Count(h =>
                h != null &&
                h.IsAlive &&
                !h.IsChild &&
                h != clan.Leader);

            if (clan.Leader != null && adultHeirs == 0)
                yield return $"House {clan.Name} has no other living adult noble and may face a succession problem.";
        }



        var threat = Campaign.Current?.GetCampaignBehavior<KaiThreatBehavior>();
        if (threat != null)
        {
            var ownThreat = threat.GetThreat(kingdom);
            if (ownThreat >= 60f)
                yield return $"Other realms are likely to view our expansion as a major threat ({ownThreat:0}/100).";
        }

        if (!GetBasicAdvicePresence(kingdom, war, diplomacy, dynasty) &&
            (threat == null || threat.GetThreat(kingdom) < 60f))
            yield return "The council sees no immediate strategic warning.";
    }

    private static bool GetBasicAdvicePresence(
        Kingdom kingdom,
        KaiWarExhaustionBehavior war,
        KaiDiplomacyBehavior diplomacy,
        KaiDynasticMarriageBehavior dynasty)
    {
        if (kingdom == null)
            return false;
        if (kingdom.FactionsAtWarWith.Any())
            return true;
        if (diplomacy != null && Kingdom.All.Any(k =>
                k != null && k != kingdom && diplomacy.IsNonAggressionPactActive(kingdom, k)))
            return true;
        if (dynasty != null && Kingdom.All.Any(k =>
                k != null && k != kingdom && dynasty.GetDynasticStrength(kingdom, k) >= 35f))
            return true;
        return false;
    }
}
