using Robust.Shared.Serialization;

namespace Content.Shared._WF.MameArcade;

[Serializable, NetSerializable]
public enum MameArcadeUiKey : byte
{
    Key,
}

/// <summary>Limits the server enforces on what clients send.</summary>
public static class MameArcadeProtocol
{
    public const int MaxGameName = 32;
    public const int MaxFingerprint = 64;

    /// <summary>Most frames of input one message may carry.</summary>
    public const int MaxInputsPerMessage = 32;

    /// <summary>No checksum in this message.</summary>
    public const int NoChecksum = -1;
}

/// <summary>The cabinet's 2-player lobby, shown to everyone with it open.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadeBuiState(
    NetEntity? host,
    string hostName,
    string game,
    string fingerprint,
    NetEntity? guest,
    string guestName) : BoundUserInterfaceState
{
    public readonly NetEntity? Host = host;
    public readonly string HostName = hostName;
    public readonly string Game = game;
    public readonly string Fingerprint = fingerprint;
    public readonly NetEntity? Guest = guest;
    public readonly string GuestName = guestName;
}

/// <summary>Offers a 2-player game. The fingerprint says which ROMs and BIOS the host's copy uses.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadeHostMessage(string game, string fingerprint) : BoundUserInterfaceMessage
{
    public readonly string Game = game;
    public readonly string Fingerprint = fingerprint;
}

[Serializable, NetSerializable]
public sealed class MameArcadeJoinMessage(string fingerprint) : BoundUserInterfaceMessage
{
    public readonly string Fingerprint = fingerprint;
}

/// <summary>Cancels a lobby or leaves a 2-player game.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadeLeaveMessage : BoundUserInterfaceMessage;

/// <summary>Both players start the game from power-on. Seat 0 is player 1.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadeStartMessage(int seat, int delay, string partnerName) : BoundUserInterfaceMessage
{
    public readonly int Seat = seat;
    public readonly int Delay = delay;
    public readonly string PartnerName = partnerName;
}

/// <summary>The other player left; play on alone.</summary>
[Serializable, NetSerializable]
public sealed class MameArcadePartnerLeftMessage : BoundUserInterfaceMessage;
