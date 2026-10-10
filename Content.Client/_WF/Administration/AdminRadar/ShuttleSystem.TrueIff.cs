using Content.Shared.Shuttles.Components;

namespace Content.Client.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    private const IFFFlags WfHidingFlags = IFFFlags.Hide | IFFFlags.HideLabel | IFFFlags.HideLabelAlways;

    /// <summary>
    /// Clears the hiding flags on every IFF for one radar draw and notes what they were. The radar code reads the
    /// flags straight off the component, so this is how the admin scanner sees past them without changing that code.
    /// Nothing is dirtied; <see cref="WfRestoreIff"/> must get the same list back before the draw returns.
    /// </summary>
    public void WfRevealIff(List<(IFFComponent Iff, IFFFlags Flags)> hidden)
    {
        var query = AllEntityQuery<IFFComponent>();
        while (query.MoveNext(out var iff))
        {
            if ((iff.Flags & WfHidingFlags) == 0x0)
                continue;

            hidden.Add((iff, iff.Flags));
            iff.Flags &= ~WfHidingFlags;
        }
    }

    /// <summary>
    /// Puts back the flags <see cref="WfRevealIff"/> cleared.
    /// </summary>
    public void WfRestoreIff(List<(IFFComponent Iff, IFFFlags Flags)> hidden)
    {
        foreach (var (iff, flags) in hidden)
        {
            iff.Flags = flags;
        }

        hidden.Clear();
    }
}
