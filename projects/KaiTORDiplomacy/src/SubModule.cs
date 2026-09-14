using KaiTOR.Diplomacy.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace KaiTOR.Diplomacy;

public sealed class SubModule : MBSubModuleBase
{
    protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
    {
        base.OnGameStart(game, gameStarterObject);

        if (gameStarterObject is CampaignGameStarter campaignStarter)
        {
            campaignStarter.AddBehavior(new KaiDiplomacyBehavior());
        }
    }
}
