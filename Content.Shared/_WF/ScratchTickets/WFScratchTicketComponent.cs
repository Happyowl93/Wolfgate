using Robust.Shared.Audio;
using Robust.Shared.Utility;

namespace Content.Shared._WF.ScratchTickets;

/// <summary>A scratch ticket: nine hidden symbols, three alike win that symbol's prize.</summary>
[RegisterComponent]
public sealed partial class WFScratchTicketComponent : Component
{
    /// <summary>Every symbol the ticket can show. One with no prize only fills cells.</summary>
    [DataField(required: true)]
    public List<WFScratchSymbol> Symbols = new();

    /// <summary>Weight of a ticket that wins nothing, against the symbols' weights.</summary>
    [DataField]
    public float LoseWeight = 1f;

    /// <summary>The symbol under each cell, row by row, rolled on the server at map init.</summary>
    [DataField]
    public List<int> Cells = new();

    /// <summary>What the ticket pays, known from the roll.</summary>
    [DataField]
    public int Prize;

    [DataField]
    public bool Scratched;

    [DataField]
    public SoundSpecifier? WinSound = new SoundPathSpecifier("/Audio/Effects/Arcade/win.ogg");
}

/// <summary>One symbol of a scratch ticket.</summary>
[DataDefinition]
public sealed partial class WFScratchSymbol
{
    [DataField(required: true)]
    public SpriteSpecifier Icon = SpriteSpecifier.Invalid;

    /// <summary>Thalers paid for three of this symbol.</summary>
    [DataField]
    public int Prize;

    /// <summary>Weight of a ticket that wins on this symbol.</summary>
    [DataField]
    public float Weight;
}
