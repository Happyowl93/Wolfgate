using Robust.Client.UserInterface.Controls;

namespace Content.Client.Examine;

public sealed partial class ExamineSystem
{
    /// <summary>Raises <see cref="ExamineServerInfoShownEvent"/> for the open tooltip.</summary>
    private void RaiseServerInfoShown(EntityUid target)
    {
        if (_examineTooltipOpen?.GetChild(0).GetChild(0) is BoxContainer box)
            RaiseLocalEvent(new ExamineServerInfoShownEvent(target, box));
    }
}

/// <summary>The server's examine info has filled the tooltip. Broadcast, so other systems can add rows to it.</summary>
public sealed class ExamineServerInfoShownEvent(EntityUid target, BoxContainer box) : EntityEventArgs
{
    public readonly EntityUid Target = target;

    /// <summary>The tooltip's rows: the name header first, then the examine text and the verb buttons.</summary>
    public readonly BoxContainer Box = box;
}
