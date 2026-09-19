using System;
using System.Collections.Generic;
using System.Linq;
using KaiTOR.Diplomacy.UI;
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
            yield return KaiTORDiplomacyUiText.Get("kaitor_diplomacy_council_no_kingdom", "Your clan does not currently belong to a kingdom.");
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
                    yield return KaiTORDiplomacyUiText.Format(
                        "kaitor_diplomacy_council_war_high_exhaustion",
                        "War with {REALM}: our exhaustion is high ({OURS}%). Peace should be considered.",
                        ("REALM", enemy.Name),
                        ("OURS", ours.ToString("0")));
                else if (theirs - ours >= 25f)
                    yield return KaiTORDiplomacyUiText.Format(
                        "kaitor_diplomacy_council_enemy_exhaustion",
                        "War with {REALM}: their exhaustion ({THEIRS}%) substantially exceeds ours ({OURS}%).",
                        ("REALM", enemy.Name),
                        ("THEIRS", theirs.ToString("0")),
                        ("OURS", ours.ToString("0")));
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
                    yield return KaiTORDiplomacyUiText.Format(
                        "kaitor_diplomacy_council_nap_receptive",
                        "{REALM} appears receptive to a non-aggression pact (assessment {SCORE}).",
                        ("REALM", target.Name),
                        ("SCORE", score));
            }

            foreach (var target in Kingdom.All
                         .Where(k => k != null && k != kingdom && diplomacy.IsNonAggressionPactActive(kingdom, k)))
            {
                var trust = diplomacy.GetTrust(kingdom, target);
                if (trust >= 40)
                    yield return KaiTORDiplomacyUiText.Format(
                        "kaitor_diplomacy_council_pact_trust",
                        "The pact with {REALM} rests on strong accumulated trust ({TRUST}).",
                        ("REALM", target.Name),
                        ("TRUST", trust));
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
                yield return KaiTORDiplomacyUiText.Format(
                    "kaitor_diplomacy_council_dynastic",
                    "The ruling houses of {FIRST} and {SECOND} have significant dynastic ties ({STRENGTH}/100).",
                    ("FIRST", kingdom.Name),
                    ("SECOND", other.Kingdom.Name),
                    ("STRENGTH", other.Strength.ToString("0")));
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
                yield return KaiTORDiplomacyUiText.Format(
                    "kaitor_diplomacy_council_succession",
                    "House {CLAN} has no other living adult noble and may face a succession problem.",
                    ("CLAN", clan.Name));
        }



        var threat = Campaign.Current?.GetCampaignBehavior<KaiThreatBehavior>();
        if (threat != null)
        {
            var ownThreat = threat.GetThreat(kingdom);
            if (ownThreat >= 60f)
                yield return KaiTORDiplomacyUiText.Format(
                    "kaitor_diplomacy_council_threat",
                    "Other realms are likely to view our expansion as a major threat ({THREAT}/100).",
                    ("THREAT", ownThreat.ToString("0")));
        }

        if (!GetBasicAdvicePresence(kingdom, war, diplomacy, dynasty) &&
            (threat == null || threat.GetThreat(kingdom) < 60f))
            yield return KaiTORDiplomacyUiText.Get(
                "kaitor_diplomacy_ui_council_clear",
                "The council sees no immediate strategic warning.");
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
