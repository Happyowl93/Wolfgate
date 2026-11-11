using System.Text;
using System.Text.RegularExpressions;
using Content.Server.Speech;
using Content.Shared._WF.Narcotics;
using Robust.Shared.Random;

namespace Content.Server._WF.Narcotics;

/// <summary>
/// How a high sounds to everyone: a heavy stimulant high rushes speech, doubling words and turning full stops into
/// exclamations; a heavy opioid high trails off into pauses. An overdose pushes both further.
/// </summary>
public sealed class WFDrugSpeechSystem : EntitySystem
{
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private WFIntoxicationSystem _intoxication = default!;

    private static readonly Regex Token = new(@"\S+");
    private static readonly Regex FullStop = new(@"(?<=\p{L})\.(?=\s|$)");
    private static readonly Regex Ending = new(@"[.!?,;:]+$");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<WFIntoxicationComponent, AccentGetEvent>(OnAccent);
    }

    private void OnAccent(EntityUid uid, WFIntoxicationComponent comp, AccentGetEvent args)
    {
        var stimulant = _intoxication.FamilyPhase(comp, WFDrugFamily.Stimulant);
        if (stimulant >= WFDrugPhase.Heavy)
            args.Message = Rush(args.Message, stimulant == WFDrugPhase.Overdose);

        var opioid = _intoxication.FamilyPhase(comp, WFDrugFamily.Opioid);
        if (opioid >= WFDrugPhase.Heavy)
            args.Message = Trail(args.Message, opioid == WFDrugPhase.Overdose);
    }

    /// <summary>Doubles some words, shouts some in an overdose, and ends sentences with exclamations.</summary>
    public string Rush(string message, bool overdose)
    {
        var repeat = overdose ? 0.3f : 0.15f;
        var shout = overdose ? 0.3f : 0f;
        var rushed = Token.Replace(message, match =>
        {
            var word = match.Value;
            if (_random.Prob(shout))
                word = word.ToUpperInvariant();

            var core = Ending.Replace(word, string.Empty);
            return core.Length > 0 && _random.Prob(repeat) ? $"{core}, {word}" : word;
        });

        return FullStop.Replace(rushed, "!");
    }

    /// <summary>Puts pauses after some words and lets the end trail off.</summary>
    public string Trail(string message, bool overdose)
    {
        var pause = overdose ? 0.25f : 0.12f;
        var tokens = Token.Matches(message);
        var result = new StringBuilder();
        var from = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            result.Append(message, from, token.Index - from);
            from = token.Index + token.Length;

            var last = i == tokens.Count - 1;
            if (last)
            {
                result.Append(Ending.Replace(token.Value, string.Empty)).Append("...");
                continue;
            }

            result.Append(token.Value);
            if (!Ending.IsMatch(token.Value) && _random.Prob(pause))
                result.Append("...");
        }

        result.Append(message, from, message.Length - from);
        return result.ToString();
    }
}
