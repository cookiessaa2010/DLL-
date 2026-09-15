using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Transparent decorator over the active Bannerlord/TOR pregnancy model.
/// It changes only player-family fertility for marriages that KaiTOR enables.
/// Every probability/duration remains owned by the underlying model.
/// </summary>
public sealed class KaiPregnancyModel : PregnancyModel
{
    private readonly PregnancyModel _baseModel;

    public KaiPregnancyModel(PregnancyModel baseModel)
    {
        _baseModel = baseModel;
    }

    public string UnderlyingModelTypeName => _baseModel?.GetType().FullName ?? "<null>";

    public override float PregnancyDurationInDays => _baseModel?.PregnancyDurationInDays ?? 36f;
    public override float MaternalMortalityProbabilityInLabor => _baseModel?.MaternalMortalityProbabilityInLabor ?? 0f;
    public override float StillbirthProbability => _baseModel?.StillbirthProbability ?? 0f;
    public override float DeliveringFemaleOffspringProbability => _baseModel?.DeliveringFemaleOffspringProbability ?? 0.5f;
    public override float DeliveringTwinsProbability => _baseModel?.DeliveringTwinsProbability ?? 0f;

    public override float GetDailyChanceOfPregnancyForHero(Hero hero)
    {
        if (_baseModel == null || hero == null)
            return 0f;

        var spouse = hero.Spouse;
        if (spouse == null)
            return _baseModel.GetDailyChanceOfPregnancyForHero(hero);

        // Do not rewrite TOR's world simulation. This guard applies only to marriages
        // involving the player clan, which are the marriages KaiTOR re-enables.
        if (!KaiPlayerMarriageModel.InvolvesPlayerClan(hero, spouse))
            return _baseModel.GetDailyChanceOfPregnancyForHero(hero);

        if (!KaiPlayerMarriageModel.AreCulturesCompatible(hero.Culture?.StringId, spouse.Culture?.StringId))
            return 0f;

        return TorFamilySafety.CanUseVanillaPregnancy(hero, spouse)
            ? _baseModel.GetDailyChanceOfPregnancyForHero(hero)
            : 0f;
    }
}
