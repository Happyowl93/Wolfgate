using Robust.Shared.Serialization;

namespace Content.Shared._WF.NanoChat;

/// <summary>What the NanoChat app shows. Only the open chat's messages are sent.</summary>
[Serializable, NetSerializable]
public sealed class NanoChatUiState : BoundUserInterfaceState
{
    public readonly Dictionary<uint, NanoChatRecipient> Recipients;
    public readonly Dictionary<uint, List<NanoChatMessage>> Messages;
    public readonly HashSet<uint> MutedChats;

    /// <summary>The directory of listed numbers, or null when there is no signal.</summary>
    public readonly List<NanoChatRecipient>? Contacts;

    public readonly uint? CurrentChat;
    public readonly uint OwnNumber;
    public readonly int MaxRecipients;
    public readonly bool NotificationsMuted;
    public readonly bool ListNumber;

    public NanoChatUiState(
        Dictionary<uint, NanoChatRecipient> recipients,
        Dictionary<uint, List<NanoChatMessage>> messages,
        HashSet<uint> mutedChats,
        List<NanoChatRecipient>? contacts,
        uint? currentChat,
        uint ownNumber,
        int maxRecipients,
        bool notificationsMuted,
        bool listNumber)
    {
        Recipients = recipients;
        Messages = messages;
        MutedChats = mutedChats;
        Contacts = contacts;
        CurrentChat = currentChat;
        OwnNumber = ownNumber;
        MaxRecipients = maxRecipients;
        NotificationsMuted = notificationsMuted;
        ListNumber = listNumber;
    }
}
