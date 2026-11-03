using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client._WF.MameArcade.NeoGeo;
using Content.Shared._WF.MameArcade;
using Robust.Client.Audio;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Timing;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.MameArcade;

/// <summary>The player's ROM library, the cabinet's 2-player lobby, and the running game.</summary>
public sealed partial class MameArcadeWindow : DefaultWindow
{
    // How long a 2-player game may wait on the partner before the screen says so.
    private const float StallNotice = 0.5f;

    [Dependency] private IAudioManager _audioManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private IFileDialogManager _dialogs = default!;
    [Dependency] private ILogManager _log = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IResourceManager _resources = default!;

    private readonly MameRomLibrary _library;
    private readonly ISawmill _sawmill;

    private readonly BoxContainer _libraryPanel;
    private readonly Label _lobbyLabel;
    private readonly Button _joinButton;
    private readonly Button _cancelButton;
    private readonly ItemList _games;
    private readonly Label _details;
    private readonly Button _playButton;
    private readonly Button _hostButton;

    private readonly BoxContainer _gamePanel;
    private readonly Label _status;
    private readonly Label _notice;
    private readonly Button _saveButton;
    private readonly Button _loadButton;
    private readonly MameArcadeScreen _screen = new();

    private readonly List<short[]> _audioScratch = new();
    private readonly List<(int Frame, uint Checksum)> _checksumScratch = new();
    private readonly Queue<(int Frame, uint Checksum)> _checksumsToSend = new();

    // Inputs can beat the start message here, which waits for its game tick; kept until the game starts.
    private readonly List<(int First, ushort[] Inputs, int ChecksumFrame, uint Checksum, int Margin, int Delay)> _early = new();
    private const int MaxEarlyMessages = 256;

    private MameArcadeSession? _session;
    private MameArcadeAudio? _audio;
    private MameArcadeBuiState? _lobby;
    private NeoGeoSet? _selected;
    private string _hostedGame = string.Empty;
    private string _partnerName = string.Empty;
    private int _lastFrame;
    private float _stalled;

    public event Action<string, string>? HostRequested;
    public event Action<string>? JoinRequested;
    public event Action? LeaveRequested;
    /// <summary>First frame, inputs, checksum frame, checksum, this side's slack and delay, for the partner.</summary>
    public event Action<int, ushort[], int, uint, int, int>? InputsReady;

    public MameArcadeWindow()
    {
        IoCManager.InjectDependencies(this);
        _sawmill = _log.GetSawmill("mame.arcade");
        _library = new MameRomLibrary(_resources.UserData);

        Title = Loc.GetString("mame-arcade-title");
        SetSize = new Vector2(820, 700);
        MinSize = new Vector2(480, 420);

        var import = new Button { Text = Loc.GetString("mame-arcade-import") };
        import.OnPressed += _ => Import();
        var folder = new Button { Text = Loc.GetString("mame-arcade-open-folder") };
        folder.OnPressed += _ => _library.OpenFolder();
        var refresh = new Button { Text = Loc.GetString("mame-arcade-refresh") };
        refresh.OnPressed += _ => RefreshLibrary();

        _lobbyLabel = new Label { HorizontalExpand = true };
        _joinButton = new Button { Text = Loc.GetString("mame-arcade-lobby-join") };
        _joinButton.OnPressed += _ => Join();
        _cancelButton = new Button { Text = Loc.GetString("mame-arcade-lobby-cancel") };
        _cancelButton.OnPressed += _ => LeaveRequested?.Invoke();

        _games = new ItemList { VerticalExpand = true, SelectMode = ItemList.ItemListSelectMode.Single };
        _games.OnItemSelected += args => Select(_games[args.ItemIndex].Metadata as NeoGeoSet);
        _details = new Label { FontColorOverride = Color.Gray };
        _playButton = new Button { Text = Loc.GetString("mame-arcade-play"), Disabled = true };
        _playButton.OnPressed += _ => PlaySolo();
        _hostButton = new Button { Text = Loc.GetString("mame-arcade-host"), Disabled = true };
        _hostButton.OnPressed += _ => Host();

        _libraryPanel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            SeparationOverride = 6,
            Children =
            {
                new Label { Text = Loc.GetString("mame-arcade-library-help") },
                new BoxContainer { Children = { import, folder, refresh } },
                new BoxContainer { Children = { _lobbyLabel, _joinButton, _cancelButton } },
                _games,
                _details,
                new BoxContainer { Children = { _playButton, _hostButton } },
            },
        };

        _status = new Label { HorizontalExpand = true };
        _saveButton = new Button { Text = Loc.GetString("mame-arcade-save-state") };
        _saveButton.OnPressed += _ => _session?.RequestSaveState();
        _loadButton = new Button { Text = Loc.GetString("mame-arcade-load-state") };
        _loadButton.OnPressed += _ => LoadState();
        var quit = new Button { Text = Loc.GetString("mame-arcade-quit") };
        quit.OnPressed += _ => Quit();
        _notice = new Label { FontColorOverride = Color.Gray };

        _gamePanel = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            VerticalExpand = true,
            Visible = false,
            Children =
            {
                new BoxContainer { Children = { _status, _saveButton, _loadButton, quit } },
                _screen,
                _notice,
            },
        };

        Contents.AddChild(new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            Children = { _libraryPanel, _gamePanel },
        });

        RefreshLibrary();
        UpdateLobby();
    }

    /// <summary>The cabinet's lobby changed.</summary>
    public void SetLobby(MameArcadeBuiState state)
    {
        _lobby = state;
        UpdateLobby();
    }

    /// <summary>The server seated both players; start from power-on.</summary>
    public void StartTwoPlayer(MameArcadeStartMessage message)
    {
        var game = message.Seat == 0 ? _hostedGame : _lobby?.Game ?? string.Empty;
        if (NeoGeoSets.Find(game) is not { } set)
        {
            LeaveRequested?.Invoke();
            return;
        }

        _partnerName = message.PartnerName;
        var early = _early.ToList();
        StartSession(set, new MameNetplay(message.Seat, message.Delay));
        foreach (var (first, inputs, checksumFrame, checksum, margin, delay) in early)
        {
            ReceiveInputs(first, inputs, checksumFrame, checksum, margin, delay);
        }
    }

    public void ReceiveInputs(int firstFrame, ushort[] inputs, int checksumFrame, uint checksum, int margin, int delay)
    {
        if (_session?.Netplay is not { } netplay)
        {
            if (_early.Count < MaxEarlyMessages)
                _early.Add((firstFrame, inputs, checksumFrame, checksum, margin, delay));
            return;
        }

        netplay.ReceiveInputs(firstFrame, inputs, margin, delay);
        if (checksumFrame >= 0 && netplay.AddRemoteChecksum(checksumFrame, checksum))
            Desynced();
    }

    public void PartnerLeft()
    {
        if (_session?.Netplay is not { Alone: false } netplay)
            return;

        netplay.PartnerLeft();
        _notice.Text = Loc.GetString("mame-arcade-partner-left", ("seat", 2 - netplay.Seat));
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_session == null)
            return;

        switch (_session.State)
        {
            case MameSessionState.Loading:
                _status.Text = Loc.GetString("mame-arcade-loading", ("game", _session.Set.Title));
                return;
            case MameSessionState.Failed:
                _sawmill.Error($"MAME arcade stopped: {_session.Error}");
                StopSession(Loc.GetString("mame-arcade-failed"));
                return;
            case MameSessionState.Stopped:
                StopSession(null);
                return;
        }

        _session.Update(args.DeltaSeconds, _screen.Held);
        _session.Netplay?.Adapt(args.DeltaSeconds);

        if (_session.TryTakeFrame(out var pixels))
            _screen.SetFrame(pixels, _session.Width, _session.Height);

        _session.TakeAudio(_audioScratch);
        foreach (var samples in _audioScratch)
        {
            _audio?.Feed(samples);
        }

        _audioScratch.Clear();
        _audio?.Update();

        if (_session.TryTakeSavedState(out var state))
        {
            _library.SaveState(_session.Set.Name, state);
            _notice.Text = Loc.GetString("mame-arcade-state-saved");
        }

        if (_session.Netplay is { } netplay)
            UpdateNetplay(netplay, args.DeltaSeconds);
        else
            _status.Text = _session.Set.Title;
    }

    public override void Close()
    {
        StopSession(null);
        base.Close();
    }

    private void UpdateNetplay(MameNetplay netplay, float delta)
    {
        _session!.TakeChecksums(_checksumScratch);
        foreach (var (frame, checksum) in _checksumScratch)
        {
            if (netplay.AddLocalChecksum(frame, checksum))
            {
                Desynced();
                return;
            }

            _checksumsToSend.Enqueue((frame, checksum));
        }

        _checksumScratch.Clear();

        if (!netplay.Alone && netplay.TakeOutbox(out var first, out var inputs))
        {
            for (var start = 0; start < inputs.Length; start += MameArcadeProtocol.MaxInputsPerMessage)
            {
                var chunk = inputs.Skip(start).Take(MameArcadeProtocol.MaxInputsPerMessage).ToArray();
                var (checksumFrame, checksum) = _checksumsToSend.TryDequeue(out var pending)
                    ? pending
                    : (MameArcadeProtocol.NoChecksum, 0u);
                InputsReady?.Invoke(first + start, chunk, checksumFrame, checksum, netplay.Margin, netplay.Delay);
            }
        }

        _stalled = netplay.Frame == _lastFrame ? _stalled + delta : 0f;
        _lastFrame = netplay.Frame;

        _status.Text = !netplay.Alone && _stalled > StallNotice
            ? Loc.GetString("mame-arcade-waiting-partner")
            : Loc.GetString("mame-arcade-seat",
                ("game", _session.Set.Title),
                ("seat", netplay.Seat + 1),
                ("partner", _partnerName),
                ("delay", (int) Math.Round(netplay.Delay * _session.FrameSeconds * 1000)));
    }

    private void RefreshLibrary()
    {
        _library.Refresh();
        var source = _library.Snapshot();

        _games.Clear();
        var entries = NeoGeoSets.All
            .Select(set => (Set: set, Missing: NeoGeoLoader.Missing(set, source)))
            .Where(e => e.Set.Chips.Values.Any(source.Has))
            .OrderBy(e => e.Missing.Count > 0)
            .ThenBy(e => e.Set.Title);

        foreach (var (set, missing) in entries)
        {
            var status = missing.Count == 0
                ? Loc.GetString("mame-arcade-game-ready")
                : Loc.GetString("mame-arcade-game-missing", ("count", missing.Count));
            var text = Loc.GetString("mame-arcade-game-entry",
                ("title", set.Title), ("year", set.Year), ("maker", set.Manufacturer), ("status", status));
            _games.AddItem(text, metadata: set);
        }

        if (_games.Count == 0)
            _details.Text = Loc.GetString("mame-arcade-no-games");

        Select(_selected);
        UpdateLobby();
    }

    private void Select(NeoGeoSet? set)
    {
        _selected = set;
        var missing = set == null ? null : NeoGeoLoader.Missing(set, _library.Snapshot());
        _playButton.Disabled = missing is not { Count: 0 };
        _hostButton.Disabled = missing is not { Count: 0 } || _lobby?.Host != null;

        if (set == null || missing == null)
            return;

        _details.Text = missing.Count == 0
            ? Loc.GetString("mame-arcade-game-ready")
            : Loc.GetString("mame-arcade-missing-list", ("files", string.Join(", ", missing.Select(m => m.Name))));
    }

    private void UpdateLobby()
    {
        var local = _entMan.GetNetEntity(_player.LocalEntity);
        var lobby = _lobby;
        _joinButton.Visible = false;
        _cancelButton.Visible = false;

        if (lobby?.Host is not { } host)
        {
            _lobbyLabel.Text = string.Empty;
            Select(_selected);
            return;
        }

        var title = NeoGeoSets.Find(lobby.Game)?.Title ?? lobby.Game;
        if (lobby.Guest != null)
        {
            _lobbyLabel.Text = Loc.GetString("mame-arcade-lobby-busy", ("host", lobby.HostName), ("guest", lobby.GuestName), ("game", title));
        }
        else if (host == local)
        {
            _lobbyLabel.Text = Loc.GetString("mame-arcade-lobby-waiting", ("game", title));
            _cancelButton.Visible = true;
        }
        else
        {
            var set = NeoGeoSets.Find(lobby.Game);
            var canJoin = set != null && NeoGeoLoader.Fingerprint(set, _library.Snapshot()) == lobby.Fingerprint
                && NeoGeoLoader.Missing(set, _library.Snapshot()).Count == 0;
            _lobbyLabel.Text = canJoin
                ? Loc.GetString("mame-arcade-lobby-hosting", ("host", lobby.HostName), ("game", title))
                : Loc.GetString("mame-arcade-lobby-need-game", ("host", lobby.HostName), ("game", title));
            _joinButton.Visible = canJoin && _session == null;
        }

        Select(_selected);
    }

    private async void Import()
    {
        var (stream, name) = await _dialogs.GetFileAndName(
            new FileDialogFilters(new FileDialogFilters.Group("zip")),
            FileAccess.Read);

        if (stream == null || name == null)
            return;

        await using (stream)
        {
            if (!_library.Import(name, stream))
                _details.Text = Loc.GetString("mame-arcade-import-failed");
        }

        RefreshLibrary();
    }

    private void PlaySolo()
    {
        if (_selected != null)
            StartSession(_selected, null);
    }

    private void Host()
    {
        if (_selected == null)
            return;

        var fingerprint = NeoGeoLoader.Fingerprint(_selected, _library.Snapshot());
        if (fingerprint.Length == 0)
            return;

        _hostedGame = _selected.Name;
        HostRequested?.Invoke(_selected.Name, fingerprint);
    }

    private void Join()
    {
        if (_lobby == null || NeoGeoSets.Find(_lobby.Game) is not { } set)
            return;

        JoinRequested?.Invoke(NeoGeoLoader.Fingerprint(set, _library.Snapshot()));
    }

    private void LoadState()
    {
        if (_session == null)
            return;

        if (_library.TryLoadState(_session.Set.Name, out var state))
            _session.LoadState(state);
        else
            _notice.Text = Loc.GetString("mame-arcade-no-state");
    }

    private void StartSession(NeoGeoSet set, MameNetplay? netplay)
    {
        StopSession(null);

        _session = new MameArcadeSession(set, _library.Snapshot(), _library.Nvram(), netplay);
        _audio = new MameArcadeAudio(_audioManager, _cfg);
        _lastFrame = 0;
        _stalled = 0f;
        _checksumsToSend.Clear();

        var solo = netplay == null;
        _saveButton.Visible = solo;
        _loadButton.Visible = solo;
        _notice.Text = Loc.GetString("mame-arcade-controls-neogeo");
        _libraryPanel.Visible = false;
        _gamePanel.Visible = true;
        _screen.GrabKeyboardFocus();
    }

    private void Quit()
    {
        if (_session?.Netplay is { Alone: false })
            LeaveRequested?.Invoke();

        StopSession(null);
    }

    private void Desynced()
    {
        if (_session?.Netplay is not { } netplay)
            return;

        netplay.PartnerLeft();
        LeaveRequested?.Invoke();
        _notice.Text = Loc.GetString("mame-arcade-desynced");
    }

    private void StopSession(string? message)
    {
        _session?.Dispose();
        _session = null;
        _audio?.Dispose();
        _audio = null;
        _screen.Clear();
        _hostedGame = string.Empty;
        _early.Clear();

        _gamePanel.Visible = false;
        _libraryPanel.Visible = true;
        if (message != null)
            _details.Text = message;

        UpdateLobby();
    }
}
