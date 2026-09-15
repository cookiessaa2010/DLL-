using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Transparent decorator over the active Bannerlord/TOR pregnancy model.
/// KaiTOR restores world marriages, so offspring safety must apply to every married
/// hero, not only the player clan. All probability/duration values remain owned by
/// the underlying model for biologically safe pairings.
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

        // This is a biological guard, not a social-marriage rule. Cross-race pairs,
        // Dawi, Greenskins, vampires and other undead never enter Bannerlord's vanilla
        // offspring generator. Every safe pairing keeps the original model unchanged.
        return TorFamilySafety.CanUseVanillaPregnancy(hero, spouse)
            ? _baseModel.GetDailyChanceOfPregnancyForHero(hero)
            : 0f;
    }
}
