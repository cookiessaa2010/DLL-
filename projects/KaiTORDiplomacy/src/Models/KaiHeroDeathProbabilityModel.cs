using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.Library;

namespace KaiTOR.Diplomacy.Models;

/// <summary>
/// Race-aware natural old-age mortality for TOR. Battle death is not handled by this
/// model and remains completely owned by Bannerlord/TOR. Humans delegate to the active
/// base model; long-lived/non-aging Warhammer peoples avoid Bannerlord's human age cap.
/// </summary>
public sealed class KaiHeroDeathProbabilityModel : HeroDeathProbabilityCalculationModel
{
    private const float DawiOldAgeStart = 180f;
    private const float DawiHardMaxAge = 420f;
    private const float DawiPeakAnnualMortality = 0.30f;

    private readonly HeroDeathProbabilityCalculationModel _baseModel;

    public KaiHeroDeathProbabilityModel(HeroDeathProbabilityCalculationModel baseModel)
    {
        _baseModel = baseModel;
    }

    public string UnderlyingModelTypeName => _baseModel?.GetType().FullName ?? "<null>";

    public override float CalculateHeroDeathProbability(Hero hero)
    {
        if (hero == null)
            return 0f;

        // Vampires and other undead do not die from biological old age.
        if (TorFamilySafety.IsVampire(hero) || TorFamilySafety.IsUndead(hero))
            return 0f;

        // Elves live far beyond a normal Bannerlord campaign horizon. Greenskins do
        // not have a normal senescence lifecycle in Warhammer.
        if (TorFamilySafety.IsCulture(hero, "battania") ||
            TorFamilySafety.IsCulture(hero, "eonir") ||
            TorFamilySafety.IsCulture(hero, "aserai"))
            return 0f;

        if (TorFamilySafety.IsCulture(hero, "sturgia"))
            return CalculateDawiOldAgeProbability(hero.Age);

        // Empire, Bretonnia and mortal inhabitants of Sylvania/Mousillon keep the
        // active Bannerlord/TOR human mortality curve.
        return _baseModel?.CalculateHeroDeathProbability(hero) ?? 0f;
    }

    private static float CalculateDawiOldAgeProbability(float age)
    {
        if (age <= DawiOldAgeStart)
            return 0f;
        if (age >= DawiHardMaxAge)
            return 1f;

        var progress = MBMath.ClampFloat(
            (age - DawiOldAgeStart) / (DawiHardMaxAge - DawiOldAgeStart),
            0f,
            1f);

        // Mirror Bannerlord's concept: convert a gradually increasing annual mortality
        // probability into a daily probability. Only the age range is Dawi-specific.
        var annualMortality = DawiPeakAnnualMortality * progress;
        return 1f - MathF.Pow(1f - annualMortality, 1f / CampaignTime.DaysInYear);
    }
}
