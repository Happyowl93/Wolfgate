using System.Numerics;
using Content.Shared._WF.Augments;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;

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

    /// <summary>
    /// The region on a 32-pixel humanoid sprite facing south, measured from the human parts. The character's left is
    /// the viewer's right.
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
/// The character drawn large, facing south. Hovering a part outlines it; clicking it selects it, and the selection
/// stays outlined.
/// </summary>
public sealed class WFAugmentDoll : Control
{
    /// <summary>Screen pixels per sprite pixel.</summary>
    public const float SpriteScale = 10f;

    private const float SpriteSize = 32f;

    private static readonly Color HoverColor = Color.FromHex("#E0A040");
    private static readonly Color SelectedColor = Color.FromHex("#5AB4F0");

    private readonly SpriteView _view;

    public WFAugmentRegion? Hovered { get; private set; }
    public WFAugmentRegion? Selected { get; set; }

    /// <summary>Raised when a region is clicked.</summary>
    public event Action<WFAugmentRegion>? OnRegionPressed;

    /// <summary>Raised when the hovered region changes, with null when the mouse leaves the body.</summary>
    public event Action<WFAugmentRegion?>? OnRegionHovered;

    public WFAugmentDoll()
    {
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
        _view.SetEntity(uid);
    }

    private static WFAugmentRegion? RegionAt(Vector2 position)
    {
        var pixel = position / SpriteScale;
        foreach (var region in WFAugmentRegions.HitOrder)
        {
            if (WFAugmentRegions.Bounds(region).Contains(pixel))
                return region;
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

    /// <summary>Draws the outlines over the sprite: a solid border and a see-through fill.</summary>
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
            var bounds = WFAugmentRegions.Bounds(region);
            var scale = SpriteScale * UIScale;
            var box = new UIBox2(bounds.TopLeft * scale, bounds.BottomRight * scale);
            handle.DrawRect(box, color.WithAlpha(0.2f));
            handle.DrawRect(box, color, filled: false);
            handle.DrawRect(new UIBox2(box.TopLeft + Vector2.One, box.BottomRight - Vector2.One), color, filled: false);
        }
    }
}
