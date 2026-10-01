using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace KaiTOR.UnlimitedCompanions
{
    [PrefabExtension(
        "ClanMembers",
        "descendant::ListPanel[@Id='LastSeenLocationParent']/Children")]
    internal sealed class CompanionBulkRecallPrefabExtension : PrefabExtensionInsertPatch
    {
        public override InsertType Type => InsertType.Append;

        [PrefabExtensionXmlDocument]
        public XmlDocument GetDocument()
        {
            var doc = new XmlDocument();
            doc.LoadXml(
                "<ButtonWidget" +
                " Id=\"KaiTORBulkRecallButton\"" +
                " DoNotPassEventsToChildren=\"true\"" +
                " WidthSizePolicy=\"Fixed\"" +
                " HeightSizePolicy=\"Fixed\"" +
                " SuggestedWidth=\"170\"" +
                " SuggestedHeight=\"40\"" +
                " VerticalAlignment=\"Center\"" +
                " MarginLeft=\"10\"" +
                " Brush=\"ButtonBrush2\"" +
                " UpdateChildrenStates=\"true\"" +
                " Command.Click=\"ExecuteBulkRecall\"" +
                " IsVisible=\"@IsBulkRecallVisible\"" +
                " IsEnabled=\"@IsBulkRecallEnabled\">" +
                "<Children>" +
                "<TextWidget" +
                " WidthSizePolicy=\"StretchToParent\"" +
                " HeightSizePolicy=\"StretchToParent\"" +
                " Brush=\"Kingdom.GeneralButtons.Text\"" +
                " Text=\"@BulkRecallButtonText\" />" +
                "<HintWidget" +
                " DataSource=\"{BulkRecallHint}\"" +
                " WidthSizePolicy=\"StretchToParent\"" +
                " HeightSizePolicy=\"StretchToParent\"" +
                " Command.HoverBegin=\"ExecuteBeginHint\"" +
                " Command.HoverEnd=\"ExecuteEndHint\" />" +
                "</Children>" +
                "</ButtonWidget>");
            return doc;
        }
    }

    [ViewModelMixin(nameof(ClanLordItemVM.RefreshValues))]
    internal sealed class CompanionBulkRecallMixin : BaseViewModelMixin<ClanLordItemVM>
    {
        private bool _isBulkRecallVisible;
        private bool _isBulkRecallEnabled;
        private string _bulkRecallButtonText;
        private HintViewModel _bulkRecallHint;

        public CompanionBulkRecallMixin(ClanLordItemVM viewModel)
            : base(viewModel)
        {
            _bulkRecallButtonText = Translate("Призвать всех (0)", "Recall all (0)");
            _bulkRecallHint = new HintViewModel(new TextObject(
                Translate("Нет свободных спутников для призыва.", "No idle companions are available to recall.")));
        }

        public override void OnRefresh()
        {
            try
            {
                RefreshState();
            }
            catch (Exception ex)
            {
                IsBulkRecallVisible = false;
                IsBulkRecallEnabled = false;
                BulkRecallHint = new HintViewModel(new TextObject(
                    Translate("Массовый призыв временно недоступен.", "Bulk recall is temporarily unavailable.")));
                DiagnosticLog.Write("RECALL_ALL_UI_ERROR|" + ex.GetType().FullName + "|" + ex.Message);
            }
        }

        [DataSourceMethod]
        public void ExecuteBulkRecall()
        {
            try
            {
                TextObject globalReason;
                if (!CampaignUIHelper.GetMapScreenActionIsEnabledWithReason(out globalReason))
                {
                    ShowInfo(
                        Translate("Призыв недоступен", "Recall unavailable"),
                        globalReason != null
                            ? globalReason.ToString()
                            : Translate("Сейчас это действие недоступно.", "This action is not available right now."));
                    RefreshState();
                    return;
                }

                var candidates = GetEligibleIdleCompanions();
                if (candidates.Count == 0)
                {
                    ShowInfo(
                        Translate("Призвать спутников", "Recall companions"),
                        Translate(
                            "Нет свободных спутников, ожидающих в поселениях.",
                            "There are no idle companions waiting in settlements."));
                    RefreshState();
                    return;
                }

                string title = Translate("Призвать всех спутников", "Recall all companions");
                string text = Translate(
                    "Будут призваны свободные спутники: " + candidates.Count +
                    ". Они отправятся к вашему отряду с обычным временем пути Bannerlord/The Old Realms. " +
                    "Губернаторы, пленные, лидеры отрядов, занятые заданиями и другие недоступные спутники не затрагиваются.",
                    "Idle companions to recall: " + candidates.Count +
                    ". They will travel to your main party using the normal Bannerlord/The Old Realms travel delay. " +
                    "Governors, prisoners, party leaders, quest-assigned and otherwise unavailable companions are left untouched.");

                InformationManager.ShowInquiry(
                    new InquiryData(
                        title,
                        text,
                        true,
                        true,
                        Translate("Призвать", "Recall"),
                        Translate("Отмена", "Cancel"),
                        () => RecallCandidates(candidates),
                        null),
                    false,
                    false);

                DiagnosticLog.Write("RECALL_ALL_CONFIRM|count=" + candidates.Count);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write("RECALL_ALL_ERROR|stage=ExecuteBulkRecall|" + ex.GetType().FullName + "|" + ex.Message);
                ShowInfo(
                    Translate("Ошибка", "Error"),
                    Translate("Не удалось подготовить массовый призыв. Подробности записаны в лог.", "Could not prepare bulk recall. Details were written to the log."));
            }
        }

        [DataSourceProperty]
        public bool IsBulkRecallVisible
        {
            get => _isBulkRecallVisible;
            private set
            {
                if (_isBulkRecallVisible == value)
                    return;
                _isBulkRecallVisible = value;
                OnPropertyChangedWithValue(value, nameof(IsBulkRecallVisible));
            }
        }

        [DataSourceProperty]
        public bool IsBulkRecallEnabled
        {
            get => _isBulkRecallEnabled;
            private set
            {
                if (_isBulkRecallEnabled == value)
                    return;
                _isBulkRecallEnabled = value;
                OnPropertyChangedWithValue(value, nameof(IsBulkRecallEnabled));
            }
        }

        [DataSourceProperty]
        public string BulkRecallButtonText
        {
            get => _bulkRecallButtonText;
            private set
            {
                if (_bulkRecallButtonText == value)
                    return;
                _bulkRecallButtonText = value;
                OnPropertyChangedWithValue(value, nameof(BulkRecallButtonText));
            }
        }

        [DataSourceProperty]
        public HintViewModel BulkRecallHint
        {
            get => _bulkRecallHint;
            private set
            {
                if (ReferenceEquals(_bulkRecallHint, value))
                    return;
                _bulkRecallHint = value;
                OnPropertyChangedWithValue(value, nameof(BulkRecallHint));
            }
        }

        private void RefreshState()
        {
            var clan = Clan.PlayerClan;
            var companions = clan?.Companions;

            IsBulkRecallVisible = companions != null && companions.Any(h => h != null && h.IsPlayerCompanion);
            if (!IsBulkRecallVisible)
            {
                IsBulkRecallEnabled = false;
                BulkRecallButtonText = Translate("Призвать всех (0)", "Recall all (0)");
                BulkRecallHint = new HintViewModel(new TextObject(
                    Translate("В клане пока нет спутников.", "Your clan has no companions yet.")));
                return;
            }

            TextObject globalReason;
            bool globalAllowed = CampaignUIHelper.GetMapScreenActionIsEnabledWithReason(out globalReason);
            var candidates = GetEligibleIdleCompanions();
            int count = candidates.Count;

            BulkRecallButtonText = Translate(
                "Призвать всех (" + count + ")",
                "Recall all (" + count + ")");

            IsBulkRecallEnabled = globalAllowed && count > 0;

            if (!globalAllowed)
            {
                BulkRecallHint = new HintViewModel(
                    globalReason ?? new TextObject(Translate("Сейчас это действие недоступно.", "This action is not available right now.")));
            }
            else if (count == 0)
            {
                BulkRecallHint = new HintViewModel(new TextObject(
                    Translate(
                        "Нет свободных спутников, ожидающих в поселениях. Занятые спутники не будут перемещены.",
                        "No idle companions are waiting in settlements. Busy companions will not be moved.")));
            }
            else
            {
                BulkRecallHint = new HintViewModel(new TextObject(
                    Translate(
                        "Призвать " + count + " свободных спутников к основному отряду. Используется штатное время пути.",
                        "Recall " + count + " idle companions to the main party using the normal travel delay.")));
            }
        }

        private static List<Hero> GetEligibleIdleCompanions()
        {
            var result = new List<Hero>();
            var clan = Clan.PlayerClan;
            var mainParty = MobileParty.MainParty;

            if (Campaign.Current == null || clan == null || mainParty == null || clan.Companions == null)
                return result;

            foreach (var hero in clan.Companions)
            {
                if (!IsEligibleIdleCompanion(hero, mainParty))
                    continue;

                result.Add(hero);
            }

            return result;
        }

        private static bool IsEligibleIdleCompanion(Hero hero, MobileParty mainParty)
        {
            if (hero == null ||
                !hero.IsAlive ||
                !hero.IsPlayerCompanion ||
                hero.CompanionOf != Clan.PlayerClan ||
                hero.PartyBelongedTo != null ||
                hero.CurrentSettlement == null ||
                hero.GovernorOf != null)
            {
                return false;
            }

            TextObject reason;
            return FactionHelper.IsMainClanMemberAvailableForRecall(hero, mainParty, out reason);
        }

        private void RecallCandidates(List<Hero> requested)
        {
            int queued = 0;
            int skipped = 0;
            int failed = 0;

            foreach (var hero in requested)
            {
                try
                {
                    if (!IsEligibleIdleCompanion(hero, MobileParty.MainParty))
                    {
                        skipped++;
                        DiagnosticLog.Write(
                            "RECALL_ALL_SKIP|hero=" + (hero?.StringId ?? "null") +
                            "|reason=state_changed");
                        continue;
                    }

                    string settlement = hero.CurrentSettlement?.StringId ?? "none";
                    TeleportHeroAction.ApplyDelayedTeleportToParty(hero, MobileParty.MainParty);
                    queued++;

                    DiagnosticLog.Write(
                        "RECALL_ALL_QUEUED|hero=" + hero.StringId +
                        "|name=" + hero.Name +
                        "|from=" + settlement);
                }
                catch (Exception ex)
                {
                    failed++;
                    DiagnosticLog.Write(
                        "RECALL_ALL_HERO_ERROR|hero=" + (hero?.StringId ?? "null") +
                        "|" + ex.GetType().FullName + "|" + ex.Message);
                }
            }

            DiagnosticLog.Write(
                "RECALL_ALL_DONE|requested=" + requested.Count +
                "|queued=" + queued +
                "|skipped=" + skipped +
                "|failed=" + failed);

            string summary = Translate(
                "Отправились к вашему отряду: " + queued +
                (skipped > 0 ? ". Пропущено: " + skipped : string.Empty) +
                (failed > 0 ? ". Ошибок: " + failed : string.Empty) + ".",
                "Heading to your party: " + queued +
                (skipped > 0 ? ". Skipped: " + skipped : string.Empty) +
                (failed > 0 ? ". Errors: " + failed : string.Empty) + ".");

            InformationManager.DisplayMessage(new InformationMessage(summary));

            try
            {
                ViewModel?.RefreshValues();
                RefreshState();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write("RECALL_ALL_UI_ERROR|stage=post_recall_refresh|" + ex.GetType().FullName + "|" + ex.Message);
            }
        }

        private static void ShowInfo(string title, string text)
        {
            InformationManager.ShowInquiry(
                new InquiryData(title, text, true, false, Translate("Хорошо", "OK"), string.Empty, null, null),
                false,
                false);
        }

        private static string Translate(string russian, string english)
        {
            string language = BannerlordConfig.Language ?? string.Empty;
            bool isRussian =
                language.IndexOf("russian", StringComparison.OrdinalIgnoreCase) >= 0 ||
                language.IndexOf("рус", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(language, "ru", StringComparison.OrdinalIgnoreCase);

            return isRussian ? russian : english;
        }
    }
}
