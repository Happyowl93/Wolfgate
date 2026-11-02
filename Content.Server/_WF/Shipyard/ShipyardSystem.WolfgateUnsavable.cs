using Robust.Shared.EntitySerialization;
using Robust.Shared.IoC;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private IDependencyCollection _wfDependencies = default!;

    /// <summary>
    /// Takes off any component aboard a sold hull that won't save, so one bad component doesn't lose the whole copy. The
    /// hull is about to be deleted either way. Skipping the entity instead leaves its children pointing at nothing, and
    /// the copy then fails to load.
    /// </summary>
    private void WFStripUnsavable(EntityUid grid, SerializationOptions options)
    {
        var probeOptions = options with { ErrorOnOrphan = false };
        var pending = new Queue<EntityUid>();
        pending.Enqueue(grid);
        var serializer = new EntitySerializer(_wfDependencies, probeOptions);

        while (pending.TryDequeue(out var uid))
        {
            var children = Transform(uid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                pending.Enqueue(child);
            }

            // A few bad components on one entity at most; past that, leave it to the final save to fail.
            for (var attempt = 0; attempt < 8; attempt++)
            {
                try
                {
                    if (serializer.IsSerializable(uid))
                        serializer.SerializeEntity(uid);
                    break;
                }
                catch (Exception e)
                {
                    var name = serializer.CurrentComponent;
                    serializer = new EntitySerializer(_wfDependencies, probeOptions);
                    if (name == null || !_wfComponentFactory.TryGetRegistration(name, out var registration) ||
                        !TryComp(uid, registration.Type, out var bad))
                    {
                        Log.Warning($"Could not tell what stops {ToPrettyString(uid)} saving: {e.Message}");
                        break;
                    }

                    Log.Warning($"Copying a sold ship without {name} on {ToPrettyString(uid)}, which won't save: {e.Message}");
                    RemComp(uid, bad);
                }
            }
        }
    }
}
