using System.Numerics;
using Content.Shared._WG.Settlers;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._WG.Settlers;

/// <summary>What a click on the board does.</summary>
public enum SettlersPick : byte
{
    None,
    Lane,
    Outpost,
    Colony,
    Pirates,
}

/// <summary>
/// The board seen from above: sectors, numbers, docks, every player's pieces, the pirates and the last roll.
/// Lights the legal targets of the current pick and reports a click on one.
/// </summary>
public sealed class SettlersBoardControl : Control
{
    private static readonly float Sqrt3 = MathF.Sqrt(3f);
    private static readonly Color DockLine = Color.FromHex("#55607E");

    /// <summary>The board's width and height in hex radii, docks included.</summary>
    private static readonly float BoardWidth = 5f * Sqrt3 + 2.2f;
    private const float BoardHeight = 8f + 2.2f;

    private readonly Vector2[] _hex = new Vector2[6];
    private readonly Vector2[] _quad = new Vector2[4];
    private readonly Vector2[] _stars = new Vector2[90];
    private readonly float[] _starAlpha = new float[90];
    private readonly Texture[] _icons = new Texture[SettlersRules.ResourceCount];
    private readonly Texture _pirate;
    private readonly Font _numberFont;
    private readonly Font _smallFont;

    private Vector2 _mouse = new(-1000f);
    private int _hover = -1;

    public SettlersState? State;
    public int? Seat;
    public SettlersPick Pick;

    public event Action<SettlersPick, int>? OnPicked;

    public SettlersBoardControl()
    {
        MinSize = new Vector2(540f, 520f);
        HorizontalExpand = true;
        VerticalExpand = true;
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;

        for (var resource = 0; resource < _icons.Length; resource++)
        {
            _icons[resource] = SettlersArt.ResourceTexture(resource);
        }

        _pirate = SettlersArt.PirateTexture();
        _numberFont = SettlersArt.Font(13);
        _smallFont = SettlersArt.Font(9);

        // The same sky every time the window opens.
        var random = new System.Random(7);
        for (var i = 0; i < _stars.Length; i++)
        {
            _stars[i] = new Vector2(random.NextSingle(), random.NextSingle());
            _starAlpha[i] = 0.15f + random.NextSingle() * 0.5f;
        }
    }

    private float Radius => MathF.Min(PixelWidth / BoardWidth, PixelHeight / BoardHeight);

    private Vector2 ToPixel(Vector2 board) => new Vector2(PixelWidth, PixelHeight) * 0.5f + board * Radius;

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _mouse = args.RelativePixelPosition;
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        _mouse = new Vector2(-1000f);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick)
            return;

        _mouse = args.RelativePixelPosition;
        var target = FindHover();
        if (target < 0)
            return;

        OnPicked?.Invoke(Pick, target);
        args.Handle();
    }

    /// <summary>Whether the local player may pick this corner, side or tile now.</summary>
    private bool Legal(int target)
    {
        if (State is not { } state || Seat is not { } seat)
            return false;

        return Pick switch
        {
            SettlersPick.Outpost => state.CanPlaceOutpost(seat, target),
            SettlersPick.Colony => state.CanPlaceColony(seat, target),
            SettlersPick.Lane => state.CanPlaceLane(seat, target),
            SettlersPick.Pirates => target != state.Pirates,
            _ => false,
        };
    }

    private int TargetCount => Pick switch
    {
        SettlersPick.Outpost or SettlersPick.Colony => SettlersBoard.VertexCount,
        SettlersPick.Lane => SettlersBoard.EdgeCount,
        SettlersPick.Pirates => SettlersBoard.TileCount,
        _ => 0,
    };

    private Vector2 TargetPos(int target) => Pick switch
    {
        SettlersPick.Outpost or SettlersPick.Colony => SettlersBoard.VertexPos[target],
        SettlersPick.Lane => SettlersBoard.EdgeMid(target),
        _ => SettlersBoard.TileCenter[target],
    };

    /// <summary>The legal target nearest the mouse, if it is close enough.</summary>
    private int FindHover()
    {
        var reach = Pick == SettlersPick.Pirates ? 0.85f : 0.4f;
        var best = -1;
        var bestDistance = reach * Radius;
        for (var target = 0; target < TargetCount; target++)
        {
            var distance = Vector2.Distance(ToPixel(TargetPos(target)), _mouse);
            if (distance >= bestDistance || !Legal(target))
                continue;

            best = target;
            bestDistance = distance;
        }

        return best;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        handle.DrawRect(new UIBox2(0f, 0f, PixelWidth, PixelHeight), SettlersArt.Space);
        for (var i = 0; i < _stars.Length; i++)
        {
            handle.DrawCircle(_stars[i] * new Vector2(PixelWidth, PixelHeight), 1f, Color.White.WithAlpha(_starAlpha[i]));
        }

        if (State is not { } state)
            return;

        _hover = FindHover();
        var radius = Radius;

        for (var tile = 0; tile < SettlersBoard.TileCount; tile++)
        {
            DrawHex(handle, SettlersBoard.TileCenter[tile], 1.1f, SettlersArt.Rim);
        }

        DrawDocks(handle, state, radius);

        for (var tile = 0; tile < SettlersBoard.TileCount; tile++)
        {
            DrawTile(handle, state, tile, radius);
        }

        for (var edge = 0; edge < SettlersBoard.EdgeCount; edge++)
        {
            if (state.EdgeOwner[edge] >= 0)
                DrawLane(handle, edge, SettlersArt.SeatColors[state.EdgeOwner[edge]], radius);
        }

        for (var vertex = 0; vertex < SettlersBoard.VertexCount; vertex++)
        {
            if (state.VertexOwner[vertex] >= 0)
                DrawBuilding(handle, ToPixel(SettlersBoard.VertexPos[vertex]), SettlersArt.SeatColors[state.VertexOwner[vertex]], state.Colony[vertex], radius);
        }

        DrawPirates(handle, ToPixel(SettlersBoard.TileCenter[state.Pirates] - new Vector2(0f, 0.28f)), radius);
        DrawTargets(handle, radius);
        DrawDice(handle, state, radius);
    }

    private void DrawHex(DrawingHandleScreen handle, Vector2 center, float scale, Color color)
    {
        for (var i = 0; i < 6; i++)
        {
            var angle = MathF.PI / 3f * i - MathF.PI / 2f;
            _hex[i] = ToPixel(center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * scale);
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _hex, color);
    }

    private void DrawTile(DrawingHandleScreen handle, SettlersState state, int tile, float radius)
    {
        var center = SettlersBoard.TileCenter[tile];
        var sector = state.Sectors[tile];
        var color = SettlersArt.SectorColors[(int) sector];
        DrawHex(handle, center, 0.97f, Color.InterpolateBetween(color, Color.Black, 0.4f));
        DrawHex(handle, center, 0.88f, color);

        if (SettlersRules.Yield(sector) is { } yield)
        {
            var size = 0.62f * radius;
            var at = ToPixel(center - new Vector2(0f, 0.28f));
            handle.DrawTextureRect(_icons[(int) yield], UIBox2.FromDimensions(at - new Vector2(size * 0.5f), new Vector2(size)));
        }

        var number = state.Numbers[tile];
        if (number == 0)
            return;

        var token = ToPixel(center + new Vector2(0f, 0.32f));
        var produced = state.Rolled && state.Die1 + state.Die2 == number;
        if (produced)
            handle.DrawCircle(token, 0.33f * radius, SettlersArt.Rolled);

        handle.DrawCircle(token, 0.27f * radius, SettlersArt.Token);
        var text = number.ToString();
        var textColor = SettlersRules.IsHot(number) ? SettlersArt.TokenHot : SettlersArt.TokenText;
        var dimensions = handle.GetDimensions(_numberFont, text, UIScale);
        handle.DrawString(_numberFont, token - new Vector2(dimensions.X * 0.5f, dimensions.Y * 0.62f), text, UIScale, textColor);

        var pips = SettlersRules.Odds(number);
        var gap = 0.065f * radius;
        for (var pip = 0; pip < pips; pip++)
        {
            handle.DrawCircle(token + new Vector2((pip - (pips - 1) * 0.5f) * gap, 0.15f * radius), 0.022f * radius + 0.5f, textColor);
        }
    }

    /// <summary>Each dock: a marker off the coast with its trade, tied to the two corners that use it.</summary>
    private void DrawDocks(DrawingHandleScreen handle, SettlersState state, float radius)
    {
        for (var dock = 0; dock < SettlersBoard.DockCount; dock++)
        {
            var edge = SettlersBoard.DockEdges[dock];
            var mid = SettlersBoard.EdgeMid(edge);
            var at = mid + Vector2.Normalize(mid) * 0.62f;
            var pixel = ToPixel(at);
            var resource = state.Docks[dock];

            DrawThickLine(handle, pixel, ToPixel(SettlersBoard.VertexPos[SettlersBoard.EdgeA[edge]]), 0.05f * radius, DockLine);
            DrawThickLine(handle, pixel, ToPixel(SettlersBoard.VertexPos[SettlersBoard.EdgeB[edge]]), 0.05f * radius, DockLine);

            var fill = resource == SettlersRules.AnyDock ? SettlersArt.AnyDockColor : SettlersArt.SectorColors[resource];
            handle.DrawCircle(pixel, 0.3f * radius, SettlersArt.Outline);
            handle.DrawCircle(pixel, 0.26f * radius, fill);

            var ratio = resource == SettlersRules.AnyDock ? SettlersRules.AnyDockRatio : SettlersRules.ResourceDockRatio;
            var text = Loc.GetString("wg-settlers-ratio", ("ratio", ratio));
            if (resource != SettlersRules.AnyDock)
            {
                var size = 0.34f * radius;
                handle.DrawTextureRect(_icons[resource], UIBox2.FromDimensions(pixel - new Vector2(size * 0.5f, size * 0.62f), new Vector2(size)));
            }

            var dimensions = handle.GetDimensions(_smallFont, text, UIScale);
            var textAt = resource == SettlersRules.AnyDock
                ? pixel - dimensions * 0.5f
                : pixel + new Vector2(-dimensions.X * 0.5f, 0.06f * radius);
            handle.DrawString(_smallFont, textAt, text, UIScale, Color.White);
        }
    }

    private void DrawLane(DrawingHandleScreen handle, int edge, Color color, float radius)
    {
        var a = SettlersBoard.VertexPos[SettlersBoard.EdgeA[edge]];
        var b = SettlersBoard.VertexPos[SettlersBoard.EdgeB[edge]];
        var from = ToPixel(Vector2.Lerp(a, b, 0.14f));
        var to = ToPixel(Vector2.Lerp(a, b, 0.86f));
        DrawThickLine(handle, from, to, 0.2f * radius, SettlersArt.Outline);
        DrawThickLine(handle, from, to, 0.12f * radius, color);
    }

    private static void DrawBuilding(DrawingHandleScreen handle, Vector2 at, Color color, bool colony, float radius)
    {
        if (!colony)
        {
            handle.DrawCircle(at, 0.2f * radius, SettlersArt.Outline);
            handle.DrawCircle(at, 0.15f * radius, color);
            return;
        }

        var outer = 0.27f * radius;
        var inner = 0.21f * radius;
        handle.DrawRect(new UIBox2(at - new Vector2(outer), at + new Vector2(outer)), SettlersArt.Outline);
        handle.DrawRect(new UIBox2(at - new Vector2(inner), at + new Vector2(inner)), color);
        handle.DrawCircle(at, 0.08f * radius, SettlersArt.Outline);
    }

    private void DrawPirates(DrawingHandleScreen handle, Vector2 at, float radius)
    {
        handle.DrawCircle(at, 0.36f * radius, Color.Black.WithAlpha(0.8f));
        handle.DrawCircle(at, 0.36f * radius, SettlersArt.TokenHot, false);
        var size = 0.52f * radius;
        handle.DrawTextureRect(_pirate, UIBox2.FromDimensions(at - new Vector2(size * 0.5f), new Vector2(size)));
    }

    /// <summary>Rings every legal target; the one under the mouse is filled in the player's colour.</summary>
    private void DrawTargets(DrawingHandleScreen handle, float radius)
    {
        if (Pick == SettlersPick.None || Seat is not { } seat)
            return;

        var color = SettlersArt.SeatColors[seat];
        for (var target = 0; target < TargetCount; target++)
        {
            if (!Legal(target))
                continue;

            var hovered = target == _hover;
            switch (Pick)
            {
                case SettlersPick.Lane:
                    var a = SettlersBoard.VertexPos[SettlersBoard.EdgeA[target]];
                    var b = SettlersBoard.VertexPos[SettlersBoard.EdgeB[target]];
                    DrawThickLine(handle, ToPixel(Vector2.Lerp(a, b, 0.2f)), ToPixel(Vector2.Lerp(a, b, 0.8f)),
                        (hovered ? 0.14f : 0.06f) * radius, hovered ? color : Color.White.WithAlpha(0.55f));
                    break;

                case SettlersPick.Pirates:
                    var center = ToPixel(SettlersBoard.TileCenter[target]);
                    handle.DrawCircle(center, 0.5f * radius, hovered ? SettlersArt.TokenHot.WithAlpha(0.45f) : Color.White.WithAlpha(0.12f));
                    break;

                default:
                    var at = ToPixel(SettlersBoard.VertexPos[target]);
                    if (hovered)
                        DrawBuilding(handle, at, color.WithAlpha(0.85f), Pick == SettlersPick.Colony, radius);
                    else
                        handle.DrawCircle(at, 0.13f * radius, Color.White.WithAlpha(0.6f), false);
                    break;
            }
        }
    }

    /// <summary>The last roll, as two dice in the top left corner.</summary>
    private void DrawDice(DrawingHandleScreen handle, SettlersState state, float radius)
    {
        if (state.RollCount == 0)
            return;

        var size = 0.5f * radius;
        var origin = new Vector2(0.25f * radius);
        DrawDie(handle, origin, size, state.Die1);
        DrawDie(handle, origin + new Vector2(size * 1.25f, 0f), size, state.Die2);
    }

    private static void DrawDie(DrawingHandleScreen handle, Vector2 at, float size, int value)
    {
        handle.DrawRect(UIBox2.FromDimensions(at, new Vector2(size)), SettlersArt.Token);
        handle.DrawRect(UIBox2.FromDimensions(at, new Vector2(size)), SettlersArt.Outline, false);

        var center = at + new Vector2(size * 0.5f);
        var step = size * 0.27f;
        var pip = size * 0.08f;
        var color = SettlersArt.TokenText;
        if (value % 2 == 1)
            handle.DrawCircle(center, pip, color);
        if (value >= 2)
        {
            handle.DrawCircle(center + new Vector2(-step, -step), pip, color);
            handle.DrawCircle(center + new Vector2(step, step), pip, color);
        }

        if (value >= 4)
        {
            handle.DrawCircle(center + new Vector2(step, -step), pip, color);
            handle.DrawCircle(center + new Vector2(-step, step), pip, color);
        }

        if (value == 6)
        {
            handle.DrawCircle(center + new Vector2(-step, 0f), pip, color);
            handle.DrawCircle(center + new Vector2(step, 0f), pip, color);
        }
    }

    private void DrawThickLine(DrawingHandleScreen handle, Vector2 from, Vector2 to, float width, Color color)
    {
        var along = to - from;
        if (along.LengthSquared() < 0.01f)
            return;

        var side = Vector2.Normalize(new Vector2(-along.Y, along.X)) * (width * 0.5f);
        _quad[0] = from + side;
        _quad[1] = to + side;
        _quad[2] = to - side;
        _quad[3] = from - side;
        handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _quad, color);
    }
}
