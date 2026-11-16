using System.Numerics;
using Content.Shared._WF.Augments;
using Content.Shared.Humanoid;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.Utility;
using Robust.Shared.ContentPack;
using Robust.Shared.Input;
using Robust.Shared.Utility;
using Image = SixLabors.ImageSharp.Image;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.Augments.UI;

/// <summary>A part of the body the creator's doll picks: a limb, a hand or foot, the head or the torso.</summary>
public enum WFAugmentRegion : byte
{
    Head,
    Torso,
    LeftArm,
    LeftHand,
    RightArm,
    RightHand,
    LeftLeg,
    LeftFoot,
    RightLeg,
    RightFoot,
}

/// <summary>What each region of the doll holds and where it is drawn.</summary>
public static class WFAugmentRegions
{
    /// <summary>Regions in hit-test order: small parts before the torso they overlap.</summary>
    public static readonly WFAugmentRegion[] HitOrder =
    [
        WFAugmentRegion.LeftHand, WFAugmentRegion.RightHand, WFAugmentRegion.LeftFoot, WFAugmentRegion.RightFoot,
        WFAugmentRegion.LeftArm, WFAugmentRegion.RightArm, WFAugmentRegion.LeftLeg, WFAugmentRegion.RightLeg,
        WFAugmentRegion.Head, WFAugmentRegion.Torso,
    ];

    /// <summary>The slot whose part holds the region's implants.</summary>
    public static WFAugmentSlot Host(WFAugmentRegion region) => region switch
    {
        WFAugmentRegion.Head => WFAugmentSlot.Head,
        WFAugmentRegion.Torso => WFAugmentSlot.Chest,
        WFAugmentRegion.LeftArm => WFAugmentSlot.LeftArm,
        WFAugmentRegion.LeftHand => WFAugmentSlot.LeftHand,
        WFAugmentRegion.RightArm => WFAugmentSlot.RightArm,
        WFAugmentRegion.RightHand => WFAugmentSlot.RightHand,
        WFAugmentRegion.LeftLeg => WFAugmentSlot.LeftLeg,
        WFAugmentRegion.LeftFoot => WFAugmentSlot.LeftFoot,
        WFAugmentRegion.RightLeg => WFAugmentSlot.RightLeg,
        _ => WFAugmentSlot.RightFoot,
    };

    /// <summary>The slots picked in the region: its part, and the organs in the head or torso.</summary>
    public static WFAugmentSlot[] Slots(WFAugmentRegion region) => region switch
    {
        WFAugmentRegion.Head => [WFAugmentSlot.Head, WFAugmentSlot.Eyes],
        WFAugmentRegion.Torso =>
            [WFAugmentSlot.Chest, WFAugmentSlot.Heart, WFAugmentSlot.Lungs, WFAugmentSlot.Liver, WFAugmentSlot.Stomach],
        _ => [Host(region)],
    };

    /// <summary>The region holding a slot.</summary>
    public static WFAugmentRegion Of(WFAugmentSlot slot) => slot switch
    {
        WFAugmentSlot.Head or WFAugmentSlot.Eyes => WFAugmentRegion.Head,
        WFAugmentSlot.LeftArm => WFAugmentRegion.LeftArm,
        WFAugmentSlot.LeftHand => WFAugmentRegion.LeftHand,
        WFAugmentSlot.RightArm => WFAugmentRegion.RightArm,
        WFAugmentSlot.RightHand => WFAugmentRegion.RightHand,
        WFAugmentSlot.LeftLeg => WFAugmentRegion.LeftLeg,
        WFAugmentSlot.LeftFoot => WFAugmentRegion.LeftFoot,
        WFAugmentSlot.RightLeg => WFAugmentRegion.RightLeg,
        WFAugmentSlot.RightFoot => WFAugmentRegion.RightFoot,
        _ => WFAugmentRegion.Torso,
    };

    /// <summary>The humanoid layer whose sprite is the region's shape.</summary>
    public static HumanoidVisualLayers Layer(WFAugmentRegion region) => region switch
    {
        WFAugmentRegion.Head => HumanoidVisualLayers.Head,
        WFAugmentRegion.Torso => HumanoidVisualLayers.Chest,
        WFAugmentRegion.LeftArm => HumanoidVisualLayers.LArm,
        WFAugmentRegion.LeftHand => HumanoidVisualLayers.LHand,
        WFAugmentRegion.RightArm => HumanoidVisualLayers.RArm,
        WFAugmentRegion.RightHand => HumanoidVisualLayers.RHand,
        WFAugmentRegion.LeftLeg => HumanoidVisualLayers.LLeg,
        WFAugmentRegion.LeftFoot => HumanoidVisualLayers.LFoot,
        WFAugmentRegion.RightLeg => HumanoidVisualLayers.RLeg,
        _ => HumanoidVisualLayers.RFoot,
    };

    /// <summary>
    /// The region on a 32-pixel humanoid sprite facing south, measured from the human parts, for a part with no sprite
    /// to trace (a missing limb). The character's left is the viewer's right.
    /// </summary>
    public static UIBox2 Bounds(WFAugmentRegion region) => region switch
    {
        WFAugmentRegion.Head => new UIBox2(10, 2, 21, 10),
        WFAugmentRegion.Torso => new UIBox2(11, 10, 20, 23),
        WFAugmentRegion.LeftArm => new UIBox2(20, 11, 24, 18),
        WFAugmentRegion.LeftHand => new UIBox2(20, 18, 24, 22),
        WFAugmentRegion.RightArm => new UIBox2(7, 11, 11, 18),
        WFAugmentRegion.RightHand => new UIBox2(7, 18, 11, 22),
        WFAugmentRegion.LeftLeg => new UIBox2(16, 23, 21, 29),
        WFAugmentRegion.LeftFoot => new UIBox2(16, 29, 22, 32),
        WFAugmentRegion.RightLeg => new UIBox2(11, 23, 16, 29),
        _ => new UIBox2(10, 29, 16, 32),
    };
}

/// <summary>
/// The character drawn large, facing south. Hovering a part outlines its sprite's contour; clicking it selects it, and
/// the selection stays outlined. A part with no sprite (a missing limb) falls back to a box where it would be.
/// </summary>
public sealed class WFAugmentDoll : Control
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IResourceManager _resources = default!;

    /// <summary>Screen pixels per sprite pixel.</summary>
    public const float SpriteScale = 10f;

    private const float SpriteSize = 32f;
    private const float BorderWidth = 2f;

    private static readonly Color HoverColor = Color.FromHex("#E0A040");
    private static readonly Color SelectedColor = Color.FromHex("#5AB4F0");

    /// <summary>Opaque pixels of each sprite state's south-facing first frame, by RSI path and state; null if unreadable.</summary>
    private static readonly Dictionary<(ResPath, string), Mask?> Masks = new();

    private readonly SpriteView _view;
    private EntityUid? _entity;

    public WFAugmentRegion? Hovered { get; private set; }
    public WFAugmentRegion? Selected { get; set; }

    /// <summary>Raised when a region is clicked.</summary>
    public event Action<WFAugmentRegion>? OnRegionPressed;

    /// <summary>Raised when the hovered region changes, with null when the mouse leaves the body.</summary>
    public event Action<WFAugmentRegion?>? OnRegionHovered;

    public WFAugmentDoll()
    {
        IoCManager.InjectDependencies(this);
        var size = new Vector2(SpriteSize * SpriteScale);
        SetSize = size;
        MouseFilter = MouseFilterMode.Stop;

        _view = new SpriteView
        {
            SetSize = size,
            Stretch = SpriteView.StretchMode.None,
            Scale = new Vector2(SpriteScale),
            OverrideDirection = Direction.South,
            MouseFilter = MouseFilterMode.Ignore,
        };
        AddChild(_view);
        AddChild(new Outlines(this) { SetSize = size, MouseFilter = MouseFilterMode.Ignore });
    }

    /// <summary>Shows an entity on the doll.</summary>
    public void SetEntity(EntityUid? uid)
    {
        _entity = uid;
        _view.SetEntity(uid);
    }

    /// <summary>The opaque pixels of a sprite frame, and their edges for the outline.</summary>
    private sealed record Mask(int Width, int Height, bool[] Opaque, List<(int X, int Y)> Pixels, List<UIBox2> Edges)
    {
        public bool Contains(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && Opaque[y * Width + x];
    }

    /// <summary>Where a region's sprite sits: its mask, its top-left corner in doll pixels, and its scale.</summary>
    private readonly record struct Placed(Mask Mask, Vector2 Origin, Vector2 Scale);

    /// <summary>The region's sprite as the doll draws it now, or null if the part has none showing.</summary>
    private Placed? Shape(WFAugmentRegion region)
    {
        if (_entity is not { } uid
            || !_entities.TryGetComponent(uid, out SpriteComponent? sprite)
            || !_entities.System<SpriteSystem>().LayerMapTryGet((uid, sprite), WFAugmentRegions.Layer(region), out var index, false)
            || sprite[index] is not SpriteComponent.Layer { Visible: true, ActualRsi: { } rsi } layer
            || MaskOf(rsi.Path, layer.State.Name, rsi.Size) is not { } mask)
            return null;

        // The sprite is drawn centered on the doll, the layer shifted by its offset (in meters, y up).
        var offset = new Vector2(layer.Offset.X, -layer.Offset.Y) * EyeManager.PixelsPerMeter;
        var scale = sprite.Scale;
        var origin = new Vector2(SpriteSize / 2) + (offset - new Vector2(mask.Width, mask.Height) / 2) * scale;
        return new Placed(mask, origin, scale);
    }

    private Mask? MaskOf(ResPath rsi, string? state, Vector2i size)
    {
        if (state == null)
            return null;
        if (Masks.TryGetValue((rsi, state), out var cached))
            return cached;

        Mask? mask = null;
        try
        {
            using var stream = _resources.ContentFileRead(rsi / $"{state}.png");
            using var image = Image.Load<Rgba32>(stream);
            var width = Math.Min(size.X, image.Width);
            var height = Math.Min(size.Y, image.Height);
            var pixels = image.GetPixelSpan();
            var opaque = new bool[width * height];
            var set = new List<(int, int)>();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (pixels[y * image.Width + x].A == 0)
                        continue;
                    opaque[y * width + x] = true;
                    set.Add((x, y));
                }
            }

            mask = new Mask(width, height, opaque, set, new List<UIBox2>());
            foreach (var (x, y) in set)
            {
                if (!mask.Contains(x - 1, y))
                    mask.Edges.Add(new UIBox2(x, y, x, y + 1));
                if (!mask.Contains(x + 1, y))
                    mask.Edges.Add(new UIBox2(x + 1, y, x + 1, y + 1));
                if (!mask.Contains(x, y - 1))
                    mask.Edges.Add(new UIBox2(x, y, x + 1, y));
                if (!mask.Contains(x, y + 1))
                    mask.Edges.Add(new UIBox2(x, y + 1, x + 1, y + 1));
            }
        }
        catch (Exception e)
        {
            Logger.GetSawmill("wf.augments").Warning($"Couldn't read {rsi}/{state}.png for the augment doll: {e.Message}");
        }

        Masks[(rsi, state)] = mask;
        return mask;
    }

    private WFAugmentRegion? RegionAt(Vector2 position)
    {
        var pixel = position / SpriteScale;
        foreach (var region in WFAugmentRegions.HitOrder)
        {
            if (Shape(region) is { } shape)
            {
                var local = (pixel - shape.Origin) / shape.Scale;
                if (shape.Mask.Contains((int) MathF.Floor(local.X), (int) MathF.Floor(local.Y)))
                    return region;
            }
            else if (WFAugmentRegions.Bounds(region).Contains(pixel))
            {
                return region;
            }
        }

        return null;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        SetHovered(RegionAt(args.RelativePosition));
    }

    protected override void MouseExited()
    {
        base.MouseExited();
        SetHovered(null);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Function != EngineKeyFunctions.UIClick || RegionAt(args.RelativePosition) is not { } region)
            return;

        Selected = region;
        OnRegionPressed?.Invoke(region);
        args.Handle();
    }

    private void SetHovered(WFAugmentRegion? region)
    {
        if (Hovered == region)
            return;

        Hovered = region;
        OnRegionHovered?.Invoke(region);
    }

    /// <summary>Draws the outlines over the sprite: a solid border along the part's contour and a see-through fill.</summary>
    private sealed class Outlines(WFAugmentDoll doll) : Control
    {
        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);
            if (doll.Selected is { } selected)
                Outline(handle, selected, SelectedColor);
            if (doll.Hovered is { } hovered && hovered != doll.Selected)
                Outline(handle, hovered, HoverColor);
        }

        private void Outline(DrawingHandleScreen handle, WFAugmentRegion region, Color color)
        {
            var toScreen = SpriteScale * UIScale;
            var fill = color.WithAlpha(0.25f);
            var border = BorderWidth * UIScale;

            if (doll.Shape(region) is not { } shape)
            {
                var bounds = WFAugmentRegions.Bounds(region);
                var box = new UIBox2(bounds.TopLeft * toScreen, bounds.BottomRight * toScreen);
                handle.DrawRect(box, fill);
                handle.DrawRect(box, color, filled: false);
                handle.DrawRect(new UIBox2(box.TopLeft + Vector2.One, box.BottomRight - Vector2.One), color, filled: false);
                return;
            }

            Vector2 Screen(float x, float y) => (shape.Origin + new Vector2(x, y) * shape.Scale) * toScreen;

            foreach (var (x, y) in shape.Mask.Pixels)
                handle.DrawRect(new UIBox2(Screen(x, y), Screen(x + 1, y + 1)), fill);

            // Each edge is a line along a pixel side; widen it inward-and-outward into a bar.
            foreach (var edge in shape.Mask.Edges)
            {
                var from = Screen(edge.Left, edge.Top);
                var to = Screen(edge.Right, edge.Bottom);
                var half = new Vector2(border / 2);
                handle.DrawRect(new UIBox2(Vector2.Min(from, to) - half, Vector2.Max(from, to) + half), color);
            }
        }
    }
}
