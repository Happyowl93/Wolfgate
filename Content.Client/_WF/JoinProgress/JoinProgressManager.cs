using Content.Shared._WF.JoinProgress;
using Robust.Shared.Network;

namespace Content.Client._WF.JoinProgress;

/// <summary>Holds the server's download size for this connection, which can arrive before the join screen asks.</summary>
public sealed partial class JoinProgressManager
{
    [Dependency] private IClientNetManager _net = default!;

    /// <summary>Null until the server sends it, and for good when the server has no uploaded files.</summary>
    public MsgJoinDownloadInfo? DownloadInfo { get; private set; }

    public event Action<MsgJoinDownloadInfo>? DownloadInfoReceived;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgJoinDownloadInfo>(msg =>
        {
            DownloadInfo = msg;
            DownloadInfoReceived?.Invoke(msg);
        });
        _net.Disconnect += (_, _) => DownloadInfo = null;
        _net.ConnectFailed += (_, _) => DownloadInfo = null;
    }
}
