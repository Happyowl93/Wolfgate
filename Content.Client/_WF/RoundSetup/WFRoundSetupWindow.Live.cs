using Content.Shared._WF.RoundSetup;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.RoundSetup;

public sealed partial class WFRoundSetupWindow
{
    private string _postingDraft = string.Empty;

    /// <summary>Players already in the round can only form a crew around their current character.</summary>
    private void BuildFormedScenario()
    {
        _body.AddChild(Heading("formed-create"));
        _body.AddChild(Description("formed-create-help"));
        _body.AddChild(Heading("crew-title"));
        var crewName = new LineEdit { Text = _newTitle, PlaceHolder = Text("crew-title-placeholder"), HorizontalExpand = true,
            ToolTip = Text("crew-title-help") };
        crewName.OnTextChanged += _ => _newTitle = crewName.Text;
        _body.AddChild(crewName);
        _body.AddChild(DescriptionEditor(_newDescription, value => _newDescription = value));
        var card = Column(8);
        var title = PlainDescription(ScenarioName(WFRoundScenario.Formed));
        title.StyleClasses.Add("WFRoundSetupHeading");
        card.AddChild(title);
        card.AddChild(WFScenarioDifficultyLabel.Create(WFRoundScenario.Formed));
        card.AddChild(Description(ScenarioDescription(WFRoundScenario.Formed)));
        var create = Button("create", () => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Create)
        {
            Scenario = WFRoundScenario.Formed,
            Title = _newTitle,
            Description = _newDescription,
        }));
        CheckDescription(create, () => _newDescription);
        create.Name = "RoundSetupCreate" + WFRoundScenario.Formed;
        card.AddChild(create);
        _body.AddChild(Panel(card));
    }

    /// <summary>The host's recruiting switch and open-invitation posting.</summary>
    private void BuildRecruiting(WFRoundSetupLobby lobby)
    {
        var content = Column(6);
        content.AddChild(Heading("recruiting"));
        content.AddChild(Description("recruiting-help"));
        var toggle = new CheckBox { Text = Text("recruiting-toggle"), Pressed = lobby.RecruitingOn, Name = "CrewRecruiting" };
        toggle.OnToggled += args => SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.SetRecruiting)
        {
            LobbyId = lobby.Id,
            Recruiting = args.Pressed,
        });
        content.AddChild(toggle);
        if (lobby.RecruitingOn && !lobby.Recruiting)
            content.AddChild(Description("recruiting-hidden"));

        content.AddChild(Heading("posting"));
        content.AddChild(Description("posting-help"));
        var text = new LineEdit { Text = _postingDraft, PlaceHolder = Text("posting-placeholder"), HorizontalExpand = true,
            Name = "CrewPostingText" };
        text.IsValid = value => value.Length <= WFRoundSetupPolicy.MaximumPostingLength;
        text.OnTextChanged += _ => _postingDraft = text.Text;
        var wait = lobby.NextPosting is { } next && next > _watchTiming.CurTime ? next - _watchTiming.CurTime : (TimeSpan?) null;
        var post = Button("posting-send", () =>
        {
            if (!WFRoundSetupPolicy.TryNormalizePosting(_postingDraft, out var posting))
                return;
            SendRequest(new WFRoundSetupRequest(WFRoundSetupAction.Post) { LobbyId = lobby.Id, Posting = posting });
            _postingDraft = string.Empty;
        }, wait != null, "posting-wait");
        post.Name = "CrewPostingSend";
        post.AddStyleClass("ButtonAccept");
        content.AddChild(FlowRow(text, post));
        if (wait is { } remaining)
            content.AddChild(PlainDescription(Text("posting-cooldown", ("minutes", Math.Max(1, (int) Math.Ceiling(remaining.TotalMinutes))))));
        _body.AddChild(Panel(content));
    }
}
