using System.Linq;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._NF.Bank;
using Content.Shared._WF.ScratchTickets;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Graphics;
using Robust.Shared.Input;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.ScratchTickets;

/// <summary>
/// A scratch ticket drawn as the card itself, with no window frame: drag it by the card, close it at the corner.
/// </summary>
public sealed partial class WFScratchTicketWindow : BaseWindow
{
    private const string TitleFontPath = "/Fonts/_Goobstation/ChelseaMarket/ChelseaMarket.ttf";
    private const string BoldFontPath = "/Fonts/NotoSans/NotoSans-Bold.ttf";
    private const string PlainFontPath = "/Fonts/NotoSans/NotoSans-Regular.ttf";
    private const float ScratchSoundGap = 0.3f;
    private const float LegendRow = 32f;

    // Ore sprites fill under half of their canvas, so they are drawn enlarged and overhang their slot with empty margin.
    private const float LegendIcon = 64f;
    private const float CellIcon = 96f;

    private static readonly SoundSpecifier ScratchSound = new SoundCollectionSpecifier("PaperScribbles", AudioParams.Default.WithVolume(-8f));

    private static readonly Color Gold = Color.FromHex("#FFD23E");
    private static readonly Color TitleEdge = Color.FromHex("#3A1F00");
    private static readonly Color Cream = Color.FromHex("#F6EFD8");
    private static readonly Color Ink = Color.FromHex("#2A2418");
    private static readonly Color PrizeInk = Color.FromHex("#9C1A12");
    private static readonly Color CellEdge = Color.FromHex("#C8BA98");
    private static readonly Color Shade = Color.FromHex("#0A2A20");

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceCache _resources = default!;

    private readonly WFScratchFoil _foil = new();
    private readonly Font _titleFont;
    private readonly Font _ribbonFont;
    private readonly Font _boldFont;
    private readonly Font _prizeFont;
    private readonly Font _smallFont;

    private OwnedTexture? _art;
    private List<WFScratchSymbol> _symbols = new();
    private Texture[] _icons = Array.Empty<Texture>();
    private int[] _cells = Array.Empty<int>();
    private string _serial = string.Empty;
    private TimeSpan _nextScratchSound;
    private int _winner = -1;
    private int _prize;
    private bool _stateSeen;
    private bool _scratched;

    /// <summary>The player scratched enough foil off.</summary>
    public event Action? OnRevealed;

    public WFScratchTicketWindow()
    {
        IoCManager.InjectDependencies(this);

        _titleFont = _resources.GetFont(TitleFontPath, 42);
        _ribbonFont = _resources.GetFont(BoldFontPath, 15);
        _boldFont = _resources.GetFont(BoldFontPath, 14);
        _prizeFont = _resources.GetFont(BoldFontPath, 12);
        _smallFont = _resources.GetFont(PlainFontPath, 10);

        MouseFilter = MouseFilterMode.Stop;
        AddChild(_foil);

        _foil.Scratched += OnScratched;
        _foil.Revealed += () => OnRevealed?.Invoke();
    }

    /// <summary>Takes the ticket's symbols and serial number and puts its saved foil on.</summary>
    public void SetTicket(EntityUid uid, WFScratchTicketComponent ticket, byte[] alpha)
    {
        var sprites = _entMan.System<SpriteSystem>();
        _symbols = ticket.Symbols;
        _icons = _symbols.Select(symbol => sprites.Frame0(symbol.Icon)).ToArray();
        _serial = ((uint) uid.Id * 2654435761u % 1000000u).ToString("D6");
        _foil.SetAlpha(alpha);
    }

    public void UpdateState(WFScratchTicketState state)
    {
        _cells = state.Cells;
        _winner = WFScratchTicketRules.Winner(state.Cells);

        // A ticket opened already scratched shows no foil; one finished just now loses what is left of it.
        if (state.Scratched && !_stateSeen)
            _foil.Visible = false;
        else if (state.Scratched)
            _foil.Clear();

        _stateSeen = true;
        _scratched = state.Scratched;
        _prize = state.Prize;
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        _foil.Measure(WFScratchCardArt.Foil.Size);
        return new Vector2(WFScratchCardArt.Width, WFScratchCardArt.Height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _foil.Arrange(WFScratchCardArt.Foil);
        return finalSize;
    }

    protected override void EnteredTree()
    {
        base.EnteredTree();

        var load = TextureLoadParameters.Default;
        load.SampleParameters = new TextureSampleParameters { Filter = true };
        _art = _clyde.CreateBlankTexture<Rgba32>((WFScratchCardArt.PixelWidth, WFScratchCardArt.PixelHeight), "wf-scratch-card", load);
        _art.SetSubImage<Rgba32>(Vector2i.Zero, (WFScratchCardArt.PixelWidth, WFScratchCardArt.PixelHeight), WFScratchCardArt.Paint());
    }

    protected override void ExitedTree()
    {
        base.ExitedTree();

        _art?.Dispose();
        _art = null;
    }

    protected override DragMode GetDragModeFor(Vector2 relativeMousePos)
    {
        return DragMode.Move;
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        if (args.Function == EngineKeyFunctions.UIClick && WFScratchCardArt.Close.Contains(args.RelativePosition))
        {
            args.Handle();
            Close();
            return;
        }

        base.KeyBindDown(args);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (_art != null)
            handle.DrawTextureRect(_art, PixelSizeBox);

        DrawCloseMark(handle);

        // The title sits on its own shadow, as if stamped in foil.
        var title = Loc.GetString("wf-scratch-ticket-card-title");
        var centre = WFScratchCardArt.Width / 2f;
        DrawText(handle, _titleFont, title, centre, 15f, Shade, new TextOutline(3f * UIScale, Shade), 270f);
        DrawText(handle, _titleFont, title, centre, 11f, Gold, new TextOutline(3f * UIScale, TitleEdge), 270f);

        var top = _symbols.Count == 0 ? 0 : _symbols.Max(symbol => symbol.Prize);
        DrawText(handle, _ribbonFont, Loc.GetString("wf-scratch-ticket-card-top-prize", ("amount", BankSystemExtensions.ToSpesoString(top))),
            centre, WFScratchCardArt.Ribbon.Top + 5f, Color.White, new TextOutline(1f * UIScale, TitleEdge), WFScratchCardArt.Ribbon.Width - 30f);

        DrawLegend(handle);
        DrawText(handle, _prizeFont, Loc.GetString("wf-scratch-ticket-card-tagline"), centre, WFScratchCardArt.Legend.Bottom + 5f,
            Cream, new TextOutline(1f * UIScale, Shade), WFScratchCardArt.Width - 40f);

        DrawCells(handle);

        var bottom = WFScratchCardArt.Frame.Bottom;
        DrawText(handle, _boldFont, Status(), centre, bottom + 3f, Gold, new TextOutline(1f * UIScale, Shade), WFScratchCardArt.Width - 30f);

        var right = (WFScratchCardArt.Barcode.Right + WFScratchCardArt.Width - 14f) / 2f;
        DrawText(handle, _prizeFont, Loc.GetString("wf-scratch-ticket-card-serial", ("serial", _serial)), right, bottom + 28f, Cream, null, 180f);
        DrawText(handle, _smallFont, Loc.GetString("wf-scratch-ticket-card-small-print-redeem"), right, bottom + 49f, Cream, null, 180f);
        DrawText(handle, _smallFont, Loc.GetString("wf-scratch-ticket-card-small-print-issuer"), centre, bottom + 66f, Cream.WithAlpha(0.8f), null, WFScratchCardArt.Width - 30f);
    }

    private void DrawCloseMark(DrawingHandleScreen handle)
    {
        var box = WFScratchCardArt.Close;
        var inset = new Vector2(6f, 6f);
        for (var offset = -1; offset <= 1; offset++)
        {
            var nudge = new Vector2(offset, 0f);
            handle.DrawLine((box.TopLeft + inset) * UIScale + nudge, (box.BottomRight - inset) * UIScale + nudge, Cream);
            handle.DrawLine((box.TopRight + inset * new Vector2(-1f, 1f)) * UIScale + nudge, (box.BottomLeft + inset * new Vector2(1f, -1f)) * UIScale + nudge, Cream);
        }
    }

    /// <summary>The prize table: best prizes first, down the left column then the right.</summary>
    private void DrawLegend(DrawingHandleScreen handle)
    {
        var box = WFScratchCardArt.Legend;
        var prizes = Enumerable.Range(0, _symbols.Count)
            .Where(index => _symbols[index].Prize > 0)
            .OrderByDescending(index => _symbols[index].Prize)
            .ToList();
        var rows = Math.Max(1, (prizes.Count + 1) / 2);
        var top = box.Top + (box.Height - rows * LegendRow) / 2f;

        for (var i = 0; i < prizes.Count; i++)
        {
            var symbol = prizes[i];
            var corner = new Vector2(box.Left + 10f + (i / rows) * box.Width / 2f, top + i % rows * LegendRow);
            var overhang = new Vector2(LegendIcon - LegendRow, LegendIcon - LegendRow) / 2f;
            handle.DrawTextureRect(_icons[symbol], UIBox2.FromDimensions((corner - overhang) * UIScale, new Vector2(LegendIcon, LegendIcon) * UIScale));

            var words = Loc.GetString("wf-scratch-ticket-card-match", ("count", WFScratchTicketRules.Match));
            var at = (corner + new Vector2(LegendRow + 10f, 6f)) * UIScale;
            var advance = handle.DrawString(_boldFont, at, words, UIScale, Ink);
            handle.DrawString(_boldFont, at + new Vector2(advance.X + 5f * UIScale, 0f), BankSystemExtensions.ToSpesoString(_symbols[symbol].Prize), UIScale, PrizeInk);
        }
    }

    /// <summary>What the foil hides: each cell's ore over its prize, winners on gold.</summary>
    private void DrawCells(DrawingHandleScreen handle)
    {
        var box = WFScratchCardArt.Foil;
        var size = box.Width / WFScratchTicketRules.Columns;

        for (var i = 0; i < _cells.Length && i < WFScratchTicketRules.CellCount; i++)
        {
            var symbol = _cells[i];
            if (symbol < 0 || symbol >= _symbols.Count)
                continue;

            var corner = box.TopLeft + new Vector2(i % WFScratchTicketRules.Columns, i / WFScratchTicketRules.Columns) * size;
            var cell = UIBox2.FromDimensions(corner * UIScale, new Vector2(size, size) * UIScale);
            if (_scratched && symbol == _winner)
                handle.DrawRect(cell, Gold);

            handle.DrawRect(cell, CellEdge, false);
            handle.DrawTextureRect(_icons[symbol],
                UIBox2.FromDimensions((corner + new Vector2((size - CellIcon) / 2f, (size - CellIcon) / 2f - 6f)) * UIScale, new Vector2(CellIcon, CellIcon) * UIScale));

            var prize = _symbols[symbol].Prize;
            DrawText(handle, _prizeFont, prize > 0 ? BankSystemExtensions.ToSpesoString(prize) : Loc.GetString("wf-scratch-ticket-card-dud"),
                corner.X + size / 2f, corner.Y + size - 20f, prize > 0 ? PrizeInk : Ink, null, size - 6f);
        }
    }

    /// <summary>Draws a line of text centred on <paramref name="centre"/>, shrunk to fit <paramref name="maxWidth"/>.</summary>
    private void DrawText(DrawingHandleScreen handle, Font font, string text, float centre, float top, Color colour, TextOutline? outline, float maxWidth)
    {
        var scale = UIScale;
        var width = handle.GetDimensions(font, text, scale).X;
        if (width > maxWidth * UIScale)
        {
            scale *= maxWidth * UIScale / width;
            width = maxWidth * UIScale;
        }

        handle.DrawString(font, new Vector2(centre * UIScale - width / 2f, top * UIScale), text, scale, colour, outline);
    }

    private string Status()
    {
        if (!_scratched)
            return string.Empty;

        return _prize > 0
            ? Loc.GetString("wf-scratch-ticket-card-won", ("amount", BankSystemExtensions.ToSpesoString(_prize)))
            : Loc.GetString("wf-scratch-ticket-card-lost");
    }

    private void OnScratched()
    {
        if (_timing.RealTime < _nextScratchSound)
            return;

        _nextScratchSound = _timing.RealTime + TimeSpan.FromSeconds(ScratchSoundGap);
        _entMan.System<SharedAudioSystem>().PlayGlobal(ScratchSound, Filter.Local(), false);
    }
}
