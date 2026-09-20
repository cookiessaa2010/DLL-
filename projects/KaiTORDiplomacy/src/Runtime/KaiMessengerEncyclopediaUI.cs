using System;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using Bannerlord.UIExtenderEx.ViewModels;
using KaiTOR.Diplomacy.Models;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace KaiTOR.Diplomacy.Runtime;

/// <summary>
/// Shokuho/Diplomacy-style messenger button:
/// append a 227x40 button directly after the hero InformationText block.
/// This intentionally targets the Hero currently displayed in EncyclopediaHeroPage.
/// </summary>
[PrefabExtension(
    "EncyclopediaHeroPage",
    "descendant::RichTextWidget[@Text='@InformationText']")]
internal sealed class KaiMessengerEncyclopediaPrefabExtension : PrefabExtensionInsertPatch
{
    public override InsertType Type => InsertType.Append;

    [PrefabExtensionXmlDocument]
    public XmlDocument GetDocument()
    {
        var doc = new XmlDocument();
        doc.LoadXml(
            "<ListPanel" +
            " WidthSizePolicy=\"CoverChildren\"" +
            " HeightSizePolicy=\"CoverChildren\"" +
            " HorizontalAlignment=\"Center\"" +
            " VerticalAlignment=\"Center\"" +
            " MarginTop=\"10\"" +
            " LayoutImp.LayoutMethod=\"VerticalBottomToTop\">" +
            "<Children>" +
            "<ButtonWidget" +
            " WidthSizePolicy=\"Fixed\"" +
            " HeightSizePolicy=\"Fixed\"" +
            " SuggestedWidth=\"227\"" +
            " SuggestedHeight=\"40\"" +
            " HorizontalAlignment=\"Center\"" +
            " VerticalAlignment=\"Center\"" +
            " IsEnabled=\"@IsMessengerAvailable\"" +
            " Command.Click=\"ExecuteSendMessenger\"" +
            " DoNotPassEventsToChildren=\"true\"" +
            " Brush=\"Popup.PartySelection.Confirm\">" +
            "<Children>" +
            "<TextWidget" +
            " WidthSizePolicy=\"StretchToParent\"" +
            " HeightSizePolicy=\"StretchToParent\"" +
            " Text=\"@SendMessengerActionName\"" +
            " Brush=\"Popup.PartySelection.Button.Text\"" +
            " VerticalAlignment=\"Center\"" +
            " HorizontalAlignment=\"Center\" />" +
            "<HintWidget" +
            " WidthSizePolicy=\"StretchToParent\"" +
            " HeightSizePolicy=\"StretchToParent\"" +
            " DataSource=\"{SendMessengerHint}\"" +
            " Command.HoverBegin=\"ExecuteBeginHint\"" +
            " Command.HoverEnd=\"ExecuteEndHint\"" +
            " IsDisabled=\"true\" />" +
            "</Children>" +
            "</ButtonWidget>" +
            "</Children>" +
            "</ListPanel>");
        return doc;
    }
}

[ViewModelMixin(nameof(EncyclopediaHeroPageVM.RefreshValues))]
internal sealed class KaiMessengerEncyclopediaMixin : BaseViewModelMixin<EncyclopediaHeroPageVM>
{
    private readonly HintViewModel _emptyHint = new();
    private bool _isMessengerAvailable;
    private HintViewModel _sendMessengerHint;
    private string _sendMessengerActionName;

    public KaiMessengerEncyclopediaMixin(EncyclopediaHeroPageVM viewModel) : base(viewModel)
    {
        _sendMessengerHint = _emptyHint;
        _sendMessengerActionName = new TextObject("Отправить гонца").ToString();
    }

    public override void OnRefresh()
    {
        try
        {
            var hero = ResolveHero();
            if (hero == null)
            {
                IsMessengerAvailable = false;
                SendMessengerHint = new HintViewModel(new TextObject("Персонаж недоступен."));
                return;
            }

            var messenger = Campaign.Current?.GetCampaignBehavior<KaiMessengerBehavior>();
            TextObject reason;
            if (messenger == null)
            {
                IsMessengerAvailable = false;
                reason = new TextObject("Система гонцов не запущена.");
            }
            else
            {
                IsMessengerAvailable = messenger.CanSendMessenger(hero, out reason);
            }

            SendMessengerHint = IsMessengerAvailable
                ? _emptyHint
                : new HintViewModel(reason ?? new TextObject("Этот персонаж сейчас недоступен."));

            KaiRuntimeLog.Write(
                "MESSENGER_ENCYCLOPEDIA_REFRESH",
                $"hero={hero.StringId}; enabled={IsMessengerAvailable}; female={hero.IsFemale}; dawi={KaiRaceLifecycle.IsDawi(hero)}");
        }
        catch (Exception ex)
        {
            IsMessengerAvailable = false;
            SendMessengerHint = new HintViewModel(new TextObject("Гонец временно недоступен."));
            KaiRuntimeLog.Exception("MESSENGER_ENCYCLOPEDIA_REFRESH_FAILED", ex);
        }
    }

    [DataSourceMethod]
    public void ExecuteSendMessenger()
    {
        var hero = ResolveHero();
        if (hero == null)
            return;

        KaiRuntimeLog.Write(
            "MESSENGER_ENCYCLOPEDIA_CLICK",
            $"hero={hero.StringId}; female={hero.IsFemale}; dawi={KaiRaceLifecycle.IsDawi(hero)}");

        var messenger = Campaign.Current?.GetCampaignBehavior<KaiMessengerBehavior>();
        if (messenger == null)
        {
            KaiMessengerService.ShowQuick("Система гонцов не запущена.");
            OnRefresh();
            return;
        }

        messenger.SendMessenger(hero);
        OnRefresh();
    }

    [DataSourceProperty]
    public string SendMessengerActionName
    {
        get => _sendMessengerActionName;
        private set
        {
            if (_sendMessengerActionName == value)
                return;
            _sendMessengerActionName = value;
            OnPropertyChangedWithValue(value, nameof(SendMessengerActionName));
        }
    }

    [DataSourceProperty]
    public bool IsMessengerAvailable
    {
        get => _isMessengerAvailable;
        private set
        {
            if (_isMessengerAvailable == value)
                return;
            _isMessengerAvailable = value;
            OnPropertyChangedWithValue(value, nameof(IsMessengerAvailable));
        }
    }

    [DataSourceProperty]
    public HintViewModel SendMessengerHint
    {
        get => _sendMessengerHint;
        private set
        {
            if (ReferenceEquals(_sendMessengerHint, value))
                return;
            _sendMessengerHint = value;
            OnPropertyChangedWithValue(value, nameof(SendMessengerHint));
        }
    }

    private Hero ResolveHero() => ViewModel?.Obj as Hero;
}
