# KaiTOR Diplomacy v0.7.0 — Strategic Diplomacy Expansion

Target:
- Mount & Blade II: Bannerlord 1.3.15.110062
- The Old Realms 1.3.15
- Launcher version: v1.3.15.70

## Architecture
TOR remains the owner of war/peace, alliances, trade agreements, ServeAsAHireling, races, religions, duels, books, post-battle systems and assimilation. KaiTOR adds persisted political memory, wrappers, event listeners, safe native actions and additive score modifiers.

KaiTOR does not replace:
- TORDiplomacyModel
- TORAllianceModel
- TORTradeAgreementModel
- ServeAsAHirelingCampaignBehavior
- TOR duel/book/post-battle systems

## A — Compatibility hardening
- Ordinary AI -> player marriage offers are owned by native MarriageOfferCampaignBehavior.
- KaiIncomingMarriageProposalBehavior is now special-offer-only and has no periodic ordinary matchmaking.
- NAP receives a last-line Harmony guard over DeclareWarAction.ApplyInternal and direct FactionManager.DeclareWar.
- Ordinary TOR/direct war paths cannot bypass an active NAP.
- Mandatory world wars caused by kingdom creation, rebellion or claim on throne use a forced-breach path: NAP ends, penalties/cooldown apply and NAP_FORCED_BREACH is logged.

## B — KaiTOR Messenger
- Send Messenger action is exposed from the hero Encyclopedia without replacing TOR/Bannerlord encyclopedia prefabs.
- Known/valid hero requirement; self, child, prisoner, same-party and duplicate-target restrictions.
- Travel time 6–72 campaign hours, plus a bounded moving-target chase grace period.
- Cost: 75 base + 1 gold per estimated travel hour.
- Target position is recalculated hourly.
- Persisted primitive save state under kaitor_messenger_v1_* keys.
- Arrival choices: Start conversation / Later / Recall messenger.
- Real Bannerlord ConversationMission is used so TOR and KaiTOR dialogue options remain available.
- TOR Hireling service/battle state prevents unsafe automatic conversation opening.
- Logs: MESSENGER_SENT / ARRIVED / FAILED / CONVERSATION_OPEN.

## C — War Exhaustion
- Separate 0–100 exhaustion for each side of an active kingdom war.
- Uses native StanceLink duration/casualty/raid/siege statistics.
- Town loss weighs more than castle loss.
- Noble capture/death, caravan destruction, army collapse and landless state add pressure.
- Repeated losses use diminishing returns where appropriate.
- Exhaustion is an additive modifier to TOR peace scoring; it is not a second war AI.

## D — Peace Terms
Player-ruler can negotiate with the enemy ruler for:
- status-quo peace;
- demanded reparations;
- offered reparations;
- return of one holding captured during this war;
- peace followed by a 90-day NAP.

Acceptance reads the active TOR/Bannerlord DiplomacyModel peace score. Execution uses native MakePeaceAction, GiveGoldAction and ChangeOwnerOfSettlementAction paths.

## E — Dynastic Diplomacy 2.0
- Persisted dynastic political memory/strength between ruling houses.
- Marriage closeness to clan rulers controls initial strength.
- Memory decays slowly instead of disappearing when a temporary bond expires.
- Additive modifiers affect TOR Alliance, Trade Agreement, War and Peace scores without overriding TOR hard failures.
- Dynastic ties improve KaiTOR NAP acceptance and successful pact trust gain.
- War between tied ruling houses weakens dynastic memory and can create a grievance.

## F — Council
- Read-only Council section in the safe KaiTOR hub.
- Reports war exhaustion, NAP opportunities/trust, dynastic ties, succession risk and threat.
- Council does not execute actions and does not reveal unknown-hero data.

## G — TOR Service Record
- Reads TOR ServeAsAHireling through a reflection bridge; no second Serve as Soldier.
- Stores lord served, faction, start/end, duration, battles, victories, career and result.
- A read-only Harmony observer watches TOR's own LeaveEnlistingParty path to record true desertion semantics.
- Logs SERVICE_RECORD_START / LEAVE_MARK / END.

## H — Realm House Promotion
- Existing KaiRealmHouseGrowthBehavior gains a player-ruler elevation route.
- Eligible companion can found a new noble house using Clan.CreateCompanionToLordClan.
- No automatic fief is granted by this phase.
- Safe native MultiSelectionInquiry frontend.
- Log: REALM_HOUSE_ELEVATED.

## I — Grant Fief
- Player ruler can grant a safe player-clan fortification to an eligible clan.
- Uses Campaign.Current.KingdomManager.GiftSettlementOwnership.
- Costs 25 influence; recipient gets a small influence bonus.
- Resolves a landless-house grievance when appropriate.
- Log: FIEF_GRANTED.

## J — Right of Conquest
- Successful siege capturer receives a temporary Conquest Claim.
- Claim never transfers ownership directly.
- The claimant receives x1.35 merit inside native SettlementClaimantDecision.CalculateMeritOfOutcome.
- If the normal election rejects the claimant, a grievance is created.
- Logs CONQUEST_CLAIM_CREATED / RESOLVED.

## K — Threat
- Persistent 0–100 expansion threat plus a dynamic military-strength pressure component.
- Town/castle conquest, kingdom destruction, treaty breach and victory streaks increase threat.
- Peaceful time decays threat; voluntary release/transfer of territory can reduce it.
- Threat modifies TOR Alliance/Trade/War/Peace scores and KaiTOR NAP acceptance.
- No coalition engine and no second strategic war AI.
- Log: THREAT_CHANGE.

## L — Grievances
Persisted political memory only; no civil-war engine yet.
Current grievance sources:
- denied conquest claim;
- NAP breach;
- dynastic promise breach;
- executed relative;
- fief removed;
- strong landless house;
- severe negative relation with ruler.

Grievances decay and resolve over time. Logs GRIEVANCE_CREATED / GRIEVANCE_RESOLVED.

## Diagnostics
- kaitor_diplomacy.messenger_status
- kaitor_diplomacy.nap_guard_status
- kaitor_diplomacy.war_status
- kaitor_diplomacy.dynasty_status
- kaitor_diplomacy.service_status
- kaitor_diplomacy.realm_status
- kaitor_diplomacy.threat_status
- kaitor_diplomacy.grievance_status
- kaitor_diplomacy.live_test_snapshot

## Regression requirements
- FamilyLifecycle remains active.
- Dawi/Vampire/Greenskin automatic population remains active according to v0.6.4 FamilyLifecycle rules.
- LiveFix1 marriage-menu refresh, ASCII-safe console and safe diplomacy Gauntlet must remain intact.
- Co-op remains a separate module/UI.
