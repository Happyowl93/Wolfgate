using System.Numerics;
using Content.Client._WF.CombatConsole;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;

namespace Content.Client._WF.Drydock;

/// <summary>
/// A row of refit-mark lamps, Mk II upward, lit up to the lowest mark the ship's machines all carry.
/// </summary>
public sealed class WFDrydockTierPips : Control
{
    private static readonly string[] Numerals = { "II", "III", "IV", "V", "VI" };

    private Font? _font;
    private string? _fontSkin;

    /// <summary>Refit tiers on offer.</summary>
    public int MaxTier { get; set; }

    /// <summary>Lowest tier among the ship's machines; 0 is stock.</summary>
    public int Tier { get; set; }

    /// <summary>Whether the ship has machines for this refit; without them every lamp is dark.</summary>
    public bool Lit { get; set; }

    public WFDrydockTierPips()
    {
        SetHeight = 26;
        MinWidth = 96;
        MouseFilter = MouseFilterMode.Ignore;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var count = Math.Clamp(MaxTier, 0, Numerals.Length);
        if (count == 0)
            return;

        var skin = WFInstrumentTheme.Skin;
        if (_fontSkin != skin.Id)
        {
            _font = IoCManager.Resolve<IResourceCache>().GetFont(skin.MenuFonts, 12);
            _fontSkin = skin.Id;
        }

        var gap = 3 * UIScale;
        var width = (PixelWidth - gap * (count - 1)) / count;
        for (var i = 0; i < count; i++)
        {
            var box = UIBox2.FromDimensions(new Vector2(i * (width + gap), 0), new Vector2(width, PixelHeight));
            var lit = Lit && i < Tier;
            var color = i == count - 1 ? skin.Good : skin.Accent;
            var inner = WFConsoleAnnunciator.Face(handle, box, UIScale, color, lit);
            var text = lit ? WFInstrumentTheme.Digital ? color : skin.Ink : skin.TextMuted;
            var scale = WFInstrumentText.FitScale(handle, _font!, Numerals[i], UIScale, inner.Width - 2 * UIScale, inner.Height);
            if (scale == 0)
                continue;
            var size = handle.GetDimensions(_font!, Numerals[i], scale).X;
            handle.DrawString(_font!, inner.Center - new Vector2(size / 2, _font!.GetHeight(scale) / 2f), Numerals[i], scale, text);
        }
    }
}
