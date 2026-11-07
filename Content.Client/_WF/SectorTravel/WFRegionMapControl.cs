using System.Linq;
using System.Numerics;
using Content.Shared._WF.SectorControl;
using Content.Shared._WF.SectorTravel;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.SectorTravel;

/// <summary>The region from above: each sector at its galaxy position, its beacon links, who holds each beacon, and where the player is.</summary>
public sealed class WFRegionMapControl : Control
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    private const float Margin = 70f;
    private const float NodeRadius = 10f;
    private const float HolderRadius = 5f;
    private const float HolderInset = 22f;

    private static readonly Color BackingColor = Color.FromHex("#0b0e12");
    private static readonly Color LinkColor = Color.FromHex("#7fd4ff");
    private static readonly Color ClosedColor = Color.FromHex("#e06c5a");
    private static readonly Color NodeColor = Color.FromHex("#8a96a3");
    private static readonly Color HereColor = Color.FromHex("#f0d060");
    private static readonly Color TextColor = Color.FromHex("#c8d4e0");

    private readonly Font _font;

    public WFRegionMapControl()
    {
        IoCManager.InjectDependencies(this);
        RectClipContent = true;
        var cache = IoCManager.Resolve<IResourceCache>();
        _font = new VectorFont(cache.GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 12);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        handle.DrawRect(PixelSizeBox, BackingColor);

        var sectors = _proto.EnumeratePrototypes<WFSectorPrototype>().OrderBy(sector => sector.ID).ToList();
        if (sectors.Count == 0)
            return;

        var region = _entManager.System<WFRegionClientSystem>();
        var here = region.CurrentSector();
        var min = new Vector2(sectors.Min(s => s.GalaxyPosition.X), sectors.Min(s => s.GalaxyPosition.Y));
        var max = new Vector2(sectors.Max(s => s.GalaxyPosition.X), sectors.Max(s => s.GalaxyPosition.Y));
        var span = Vector2.Max(max - min, new Vector2(0.001f));
        var area = PixelSize - new Vector2(Margin * 2f) * UIScale;
        var scale = MathF.Min(area.X / span.X, area.Y / span.Y);
        var offset = PixelSize / 2f - (min + max) / 2f * scale * new Vector2(1, -1);

        Vector2 Place(WFSectorPrototype sector) => offset + sector.GalaxyPosition * scale * new Vector2(1, -1);

        var positions = sectors.ToDictionary(sector => sector.ID, Place);
        foreach (var beacon in region.Beacons)
        {
            if (!positions.TryGetValue(beacon.Sector, out var from) || !positions.TryGetValue(beacon.Link, out var to))
                continue;

            var closed = beacon.Closed || region.Beacons.Any(other => other.Sector == beacon.Link && other.Link == beacon.Sector && other.Closed);
            handle.DrawLine(from, to, closed ? ClosedColor : LinkColor);
            if (beacon.Holder is not { } holder || !_proto.TryIndex<WFSectorFactionPrototype>(holder, out var faction))
                continue;

            var direction = to - from;
            var end = from + (direction.Length() > 0 ? Vector2.Normalize(direction) : Vector2.Zero) * HolderInset * UIScale;
            handle.DrawCircle(end, HolderRadius * UIScale, faction.Color);
        }

        foreach (var sector in sectors)
        {
            var position = positions[sector.ID];
            var cut = region.CutOff.Contains(sector.ID);
            handle.DrawCircle(position, NodeRadius * UIScale, sector.ID == here ? HereColor : NodeColor);
            if (cut)
                handle.DrawCircle(position, (NodeRadius + 3f) * UIScale, ClosedColor, false);

            var name = Loc.GetString(sector.Name);
            if (cut)
                name = Loc.GetString("wf-region-map-cut-off", ("sector", name));

            var size = handle.GetDimensions(_font, name, UIScale);
            handle.DrawString(_font, position + new Vector2(-size.X / 2f, (NodeRadius + 6f) * UIScale), name, UIScale, TextColor);
        }
    }
}
