using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.NanoChat;

/// <summary>An action from the NanoChat app.</summary>
[Serializable, NetSerializable]
public sealed class NanoChatUiMessageEvent : CartridgeMessageEvent
{
    public readonly NanoChatUiMessageType Type;

    /// <summary>The other party's number, where the action has one.</summary>
    public readonly uint? RecipientNumber;

    /// <summary>The message text, or the contact name for a new or edited chat.</summary>
    public readonly string? Content;

    /// <summary>The contact's job title for a new or edited chat.</summary>
    public readonly string? RecipientJob;

    /// <summary>A message ghosts don't see.</summary>
    public readonly bool Subtle;

    public NanoChatUiMessageEvent(NanoChatUiMessageType type,
        uint? recipientNumber = null,
        string? content = null,
        string? recipientJob = null,
        bool subtle = false)
    {
        Type = type;
        RecipientNumber = recipientNumber;
        Content = content;
        RecipientJob = recipientJob;
        Subtle = subtle;
    }
}

[Serializable, NetSerializable]
public enum NanoChatUiMessageType : byte
{
    NewChat,
    SelectChat,
    EditChat,
    CloseChat,
    SendMessage,
    DeleteChat,
    ToggleMute,
    ToggleMuteChat,
    ToggleListNumber,
}

/// <summary>A contact on a card.</summary>
[Serializable, NetSerializable, DataRecord]
public partial struct NanoChatRecipient
{
    public uint Number;

    public string Name;

    public string? JobTitle;

    /// <summary>Whether the chat holds messages not yet read.</summary>
    public bool HasUnread;

    public NanoChatRecipient(uint number, string name, string? jobTitle = null, bool hasUnread = false)
    {
        Number = number;
        Name = name;
        JobTitle = jobTitle;
        HasUnread = hasUnread;
    }
}

/// <summary>One message in a chat.</summary>
[Serializable, NetSerializable, DataRecord]
public partial struct NanoChatMessage
{
    public const int MaxContentLength = 256;

    public TimeSpan Timestamp;

    public string Content;

    /// <summary>The sender's number.</summary>
    public uint SenderId;

    /// <summary>Whether it reached nobody: no signal at either end, or no PDA holds the number.</summary>
    public bool DeliveryFailed;

    public NanoChatMessage(TimeSpan timestamp, string content, uint senderId, bool deliveryFailed = false)
    {
        Timestamp = timestamp;
        Content = content;
        SenderId = senderId;
        DeliveryFailed = deliveryFailed;
    }
}
