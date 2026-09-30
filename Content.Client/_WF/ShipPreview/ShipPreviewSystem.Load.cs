using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.DeviceLinking;
using Robust.Client.GameObjects;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Map.Events;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;
using Robust.Shared.Utility;

namespace Content.Client._WF.ShipPreview;

/// <summary>Prepares client artwork while deserializing private preview grids.</summary>
public sealed partial class ShipPreviewSystem
{
    [Dependency] private IDependencyCollection _dependencies = default!;
    [Dependency] private IComponentFactory _components = default!;
    [Dependency] private ISerializationManager _serialization = default!;

    /// <summary>Initializes deserialized sprite artwork before preview entities start their visualizers.</summary>
    private bool TryLoadGrid(ShipPreviewHandle handle, ResPath path,
        [NotNullWhen(true)] out Entity<MapGridComponent>? grid)
    {
        grid = null;
        if (!_loader.TryReadFile(path, out var data))
            return false;

        RemoveServerComponents(data);
        var migrations = new BeforeEntityReadEvent();
        RaiseLocalEvent(migrations);
        var reader = new EntityDeserializer(_dependencies, data, new DeserializationOptions
        {
            InitializeMaps = false,
            PauseMaps = true,
        }, migrations.RenamedPrototypes, migrations.DeletedPrototypes);

        var loaded = false;
        try
        {
            if (!reader.TryProcessData())
                return false;
            if (reader.Result.Category is not (FileCategory.Grid or FileCategory.Unknown))
                return false;

            reader.CreateEntities();
            if (reader.Result.Category != FileCategory.Grid || reader.Result.Grids.Count != 1)
                return false;

            // v291 initializes artwork on ComponentAdd, before the map reader copies sprite data onto it.
            foreach (var uid in reader.Result.Entities)
            {
                // Server-only sink ports do not exist on a static client preview.
                RemComp<DeviceLinkSourceComponent>(uid);

                if (!TryComp<SpriteComponent>(uid, out var sprite))
                    continue;

                var replacement = _serialization.CreateCopy(sprite, notNullableOverride: true);
                RemComp<SpriteComponent>(uid);
                AddComp(uid, replacement);
            }

            foreach (var uid in reader.Result.Orphans)
                _xform.SetCoordinates(uid, new EntityCoordinates(handle.MapUid, Transform(uid).LocalPosition));
            reader.Result.Orphans.Clear();

            reader.StartEntities();
            grid = reader.Result.Grids.Single();
            foreach (var uid in reader.Result.Entities)
                _meta.SetEntityPaused(uid, true);
            handle.LoadedEntities = reader.Result;
            loaded = true;
            return true;
        }
        finally
        {
            if (!loaded)
                _loader.Delete(reader.Result);
        }
    }

    /// <summary>Skips saved server components that have no client registration.</summary>
    private void RemoveServerComponents(MappingDataNode data)
    {
        foreach (var group in data.Get<SequenceDataNode>("entities").Cast<MappingDataNode>())
        {
            foreach (var entity in group.Get<SequenceDataNode>("entities").Cast<MappingDataNode>())
            {
                if (!entity.TryGet<SequenceDataNode>("components", out var components))
                    continue;

                for (var i = components.Count - 1; i >= 0; i--)
                {
                    var component = (MappingDataNode) components[i];
                    if (!_components.TryGetRegistration(component.Get<ValueDataNode>("type").Value, out _))
                        components.RemoveAt(i);
                }
            }
        }
    }
}
