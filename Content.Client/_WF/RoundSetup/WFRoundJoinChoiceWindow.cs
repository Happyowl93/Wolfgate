using System.Numerics;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._WF.RoundSetup;

/// <summary>Offers the scenario crew flow first, with standard station joining as the legacy option.</summary>
public sealed class WFRoundJoinChoiceWindow : DefaultWindow
{
    /// <summary>Closes the chooser before opening the selected joining flow.</summary>
    public WFRoundJoinChoiceWindow(Action crew, Action standard, bool pregame = false)
    {
        Title = Loc.GetString(pregame ? "wf-round-setup-ready-choice-title" : "wf-round-setup-home-jobs");
        MinSize = new Vector2(320, 250);
        SetSize = new Vector2(540, 300);
        var body = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 12, Margin = new Thickness(12) };
        Contents.AddChild(body);
        AddChoice(pregame ? "ready-crew-default" : "join-crew-default", pregame ? "ready-crew-help" : "join-crew-help", "RoundJoinCrew", crew, true);
        AddChoice(pregame ? "ready-standard-legacy" : "join-standard-legacy", pregame ? "ready-standard-help" : "join-standard-help", "RoundJoinStandard", standard, false);

        void AddChoice(string title, string help, string name, Action action, bool primary)
        {
            var button = new Button { Name = name, Text = Loc.GetString("wf-round-setup-" + title), MinHeight = 48, HorizontalExpand = true };
            button.Label.ClipText = false;
            if (primary)
                button.AddStyleClass("ButtonAccept");
            button.OnPressed += _ => { Close(); action(); };
            body.AddChild(button);
            var description = new RichTextLabel { HorizontalExpand = true };
            description.SetMessage(FormattedMessage.FromUnformatted(Loc.GetString("wf-round-setup-" + help)));
            body.AddChild(description);
        }
    }
}
