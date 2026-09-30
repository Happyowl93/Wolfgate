using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.Avali.Feathers;

/// <summary>Completes a feather preening do-after.</summary>
[Serializable, NetSerializable]
public sealed partial class PreeningEvent : SimpleDoAfterEvent;
