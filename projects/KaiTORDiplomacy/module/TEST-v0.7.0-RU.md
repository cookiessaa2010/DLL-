# KaiTOR v1.3.15.70 — Strategic Expansion LIVE TEST

This build is not release-certified until the critical paths below pass in Bannerlord 1.3.15.110062 + TOR 1.3.15.

## 0 — Start / save compatibility
- [ ] Existing v0.6.4 FamilyLifecycle save loads without a new campaign.
- [ ] No crash during module/campaign start.
- [ ] Run `kaitor_diplomacy.live_test_start`.
- [ ] `kaitor_diplomacy.live_test_snapshot` lists messenger, warExhaustion, peaceTerms, service, claims, threat and grievances.
- [ ] Save -> exit -> reload preserves all newly created strategic state.
- [ ] Existing NAP, family, Dawi/Vampire/Greenskin population and Realm Houses still work.

## A — Marriage + NAP compatibility
- [ ] Ordinary AI marriage offer arrives once through native marriage notification; no duplicate KaiTOR weekly popup.
- [ ] Special KaiTOR marriage proposal can still be queued/reviewed when applicable.
- [ ] Active NAP blocks normal council war.
- [ ] Active NAP blocks direct TOR war path.
- [ ] Active NAP blocks alliance/call-to-war path unless the war is a mandatory world-state exception.
- [ ] Mandatory kingdom-creation/rebellion/claim-on-throne war ends NAP as NAP_FORCED_BREACH with penalties rather than corrupting world state.

## B — Messenger
- [ ] Known adult active hero page contains «Отправить гонца».
- [ ] Unknown hero / child / prisoner / same-party hero cannot receive a messenger.
- [ ] Gold cost is deducted once.
- [ ] ETA is between 6 and 72 hours at dispatch.
- [ ] Moving target is tracked rather than using a fixed old position.
- [ ] Save/load during travel continues the same messenger.
- [ ] Arrival offers: Начать разговор / Позже / Отозвать гонца.
- [ ] «Позже» leaves messenger waiting.
- [ ] «Отозвать» removes messenger cleanly.
- [ ] During TOR Hireling/battle/siege/conversation, no automatic conversation opens.
- [ ] Start conversation opens a real conversation mission with target Hero and TOR/KaiTOR dialogue options.

## C — War Exhaustion
- [ ] Start a fresh war: both effective values begin near zero apart from duration.
- [ ] Casualties increase the losing side's exhaustion.
- [ ] Raid increases victim exhaustion.
- [ ] Castle loss increases exhaustion.
- [ ] Town loss increases exhaustion more than castle loss.
- [ ] Noble capture/death changes exhaustion.
- [ ] Repeated events show diminishing marginal effect.
- [ ] `kaitor_diplomacy.war_status` reports both sides.
- [ ] Diplomacy UI shows us/them exhaustion for selected wartime realm.
- [ ] TOR still owns actual war/peace decisions.

## D — Peace Terms
- [ ] Conversation with enemy ruler offers peace terms only when player is ruler.
- [ ] Status quo can resolve through native MakePeaceAction.
- [ ] Demand reparations transfers gold target -> player ruler.
- [ ] Offer reparations transfers gold player ruler -> target.
- [ ] Return fief only offers a fortification captured from the player's kingdom during this war.
- [ ] Returned fief uses native ownership events and remains correct after save/load.
- [ ] Peace + NAP creates a 90-day NAP when cooldown/rules permit.
- [ ] Rejected terms do not force peace.

## E — Dynastic Diplomacy
- [ ] Cross-house marriage creates persisted dynastic strength.
- [ ] Close ruling-family marriage is stronger than distant-member marriage.
- [ ] Strength persists after the old 180-day active bond expires and then decays slowly.
- [ ] Alliance/trade/NAP become more attractive without overriding TOR hard lore restrictions.
- [ ] War willingness is reduced between strongly tied ruling houses.
- [ ] War between tied houses weakens memory and creates grievance.
- [ ] Save/load preserves dynastic strength.

## F — Council
- [ ] Safe hub opens «Совет».
- [ ] Council can report high war exhaustion.
- [ ] Council can identify a receptive NAP target.
- [ ] Council can report dynastic ties, succession risk and high threat.
- [ ] Council never performs an action by itself.
- [ ] No unknown-hero private data is exposed.

## G — TOR Service Record
- [ ] Enlist through TOR's original ServeAsAHireling flow.
- [ ] SERVICE_RECORD_START is logged.
- [ ] Lord, faction, career and duration are recorded.
- [ ] Battles/victories update while enlisted.
- [ ] Honourable leave after 25+ days records honourable.
- [ ] TOR desertion path records desertion.
- [ ] KaiTOR does not change TOR service menus/rewards/battles.
- [ ] Save/load during service keeps the current record.

## H — Realm House Promotion
- [ ] Player must be ruler.
- [ ] Eligible companion appears in «Возвести героя в дворянство».
- [ ] Unsafe/prisoner/governor/party-leader/family-conflict candidate is rejected.
- [ ] New clan uses native factory, founder becomes Lord/leader, kingdom/culture/race remain correct.
- [ ] No duplicate party/clan membership.
- [ ] No fief is silently granted.
- [ ] Save/load preserves the new house.

## I — Grant Fief
- [ ] Player ruler can select only safe player-clan fortifications.
- [ ] Recipient list contains only eligible non-mercenary clans in the same kingdom.
- [ ] Transfer costs 25 influence.
- [ ] Transfer uses normal Bannerlord/TOR ownership events.
- [ ] Recipient receives the fief after save/load.
- [ ] Landless-house grievance resolves where applicable.

## J — Right of Conquest
- [ ] Siege capture creates CONQUEST_CLAIM_CREATED for actual capturer.
- [ ] Native claimant election still happens.
- [ ] Claimant receives extra merit but is not guaranteed ownership.
- [ ] Claim expires after its lifetime if unresolved.
- [ ] Rejected legitimate claim creates grievance.

## K — Threat
- [ ] Town conquest raises threat more than castle conquest.
- [ ] Destroying a kingdom raises last conqueror's threat.
- [ ] Repeated victories build some threat.
- [ ] Long peace decays threat.
- [ ] Voluntary cross-kingdom gift/barter of territory can reduce threat.
- [ ] Threat is visible in selected-realm diplomacy summary and `threat_status`.
- [ ] TOR Alliance/Trade/War/Peace remain the actual decision systems.
- [ ] No coalition or second war AI is created.

## L — Grievances
- [ ] Denied conquest claim creates grievance.
- [ ] NAP breach creates grievance.
- [ ] Dynastic promise breach creates grievance.
- [ ] Execution of a noble creates grievance for victim clan against killer clan.
- [ ] Strong landless clan can gain grievance against ruler.
- [ ] Bad ruler relation can gain grievance.
- [ ] Grievances decay and eventually resolve.
- [ ] No faction, secession or civil war is created in this version.

## UI / regression
- [ ] Main KaiTOR Gauntlet opens without native crash.
- [ ] No ScrollablePanel / NavigatableListPanel crash regression.
- [ ] Four existing tabs still switch.
- [ ] Selected realm shows NAP/trust/breach/cooldown plus strategic metrics.
- [ ] RU and ENG strings are present.
- [ ] Console remains readable through ASCII-safe output.
- [ ] Co-op movies/UI remain unaffected.

## Final log
- [ ] Run `kaitor_diplomacy.live_test_snapshot`.
- [ ] Send one file: `%LOCALAPPDATA%\KaiTORDiplomacy\KaiTOR-LiveTest.log`.
