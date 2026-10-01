using Content.Shared._WF.EmoteVoices;
using Content.Shared.Chat.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.EmoteVoices;

/// <summary>Creator card with a voice dropdown and a play button for the scream and the laugh.</summary>
public sealed partial class EmoteVoicePicker : PanelContainer
{
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>A voice was picked for an emote; null is the species default.</summary>
    public event Action<ProtoId<EmotePrototype>, ProtoId<EmoteVoicePrototype>?>? OnVoiceSelected;

    /// <summary>The play button was pressed for an emote.</summary>
    public event Action<ProtoId<EmotePrototype>>? OnPlayPressed;

    private readonly Dictionary<ProtoId<EmotePrototype>, (OptionButton Button, List<ProtoId<EmoteVoicePrototype>?> Voices)> _rows = new();

    public EmoteVoicePicker()
    {
        IoCManager.InjectDependencies(this);
        StyleClasses.Add("CreatorCard");
        HorizontalExpand = true;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
        };
        AddChild(box);

        box.AddChild(new Label
        {
            Text = Loc.GetString("emote-voice-heading"),
            StyleClasses = { "CreatorHeading" },
        });

        AddRow(box, EmoteVoiceRules.Scream, "emote-voice-scream");
        AddRow(box, EmoteVoiceRules.Laugh, "emote-voice-laugh");
    }

    private void AddRow(BoxContainer box, ProtoId<EmotePrototype> emote, LocId label)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };

        row.AddChild(new Label
        {
            Text = Loc.GetString(label),
            VAlign = Label.VAlignMode.Center,
            StyleClasses = { "CreatorFieldLabel" },
        });
        row.AddChild(new Control { HorizontalExpand = true });

        // Item ids index into voices; id 0 is the species default.
        var voices = new List<ProtoId<EmoteVoicePrototype>?> { null };
        var button = new OptionButton { MinWidth = 220 };
        button.AddItem(Loc.GetString("emote-voice-species-default"), 0);
        foreach (var voice in EmoteVoiceRules.VoicesFor(emote, _proto))
        {
            button.AddItem(Loc.GetString(voice.Name), voices.Count);
            voices.Add(voice.ID);
        }

        button.OnItemSelected += args =>
        {
            button.SelectId(args.Id);
            OnVoiceSelected?.Invoke(emote, voices[args.Id]);
        };
        row.AddChild(button);

        var play = new Button
        {
            Text = Loc.GetString("emote-voice-play"),
            ToolTip = Loc.GetString("emote-voice-play-tooltip"),
        };
        play.OnPressed += _ => OnPlayPressed?.Invoke(emote);
        row.AddChild(play);

        box.AddChild(row);
        _rows[emote] = (button, voices);
    }

    /// <summary>Selects a profile's voice for an emote; null or an unknown voice shows the species default.</summary>
    public void SetVoice(ProtoId<EmotePrototype> emote, ProtoId<EmoteVoicePrototype>? voice)
    {
        if (!_rows.TryGetValue(emote, out var row))
            return;

        row.Button.SelectId(Math.Max(0, row.Voices.IndexOf(voice)));
    }
}
