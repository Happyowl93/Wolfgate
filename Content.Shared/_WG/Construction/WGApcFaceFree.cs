using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.Construction;

/// <summary>Construction condition: no APC or APC frame on the tile already faces the build direction.</summary>
[DataDefinition]
public sealed partial class WGApcFaceFree : IConstructionCondition
{
    private static readonly ProtoId<TagPrototype> ApcTag = "WGApc";

    public ConstructionGuideEntry GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = "wg-construction-condition-apc-face-free",
        };
    }

    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entMan = IoCManager.Resolve<IEntityManager>();
        if (entMan.System<SharedTransformSystem>().GetGrid(location) is not { } gridUid
            || !entMan.TryGetComponent<MapGridComponent>(gridUid, out var grid))
            return true;

        var maps = entMan.System<SharedMapSystem>();
        var tags = entMan.System<TagSystem>();
        var anchored = maps.GetAnchoredEntities(gridUid, grid,maps.TileIndicesFor(gridUid, grid, location));
        while (anchored.MoveNext(out var uid))
        {
            if (tags.HasTag(uid.Value, ApcTag)
                && entMan.GetComponent<TransformComponent>(uid.Value).LocalRotation.GetCardinalDir() == direction)
                return false;
        }

        return true;
    }
}
