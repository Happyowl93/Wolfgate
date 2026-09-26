using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared.Symphony;
using Robust.Server.ServerStatus;
using Robust.Shared;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Content.Server.Symphony;

/// <summary>
/// The hub switch for the SSymphony panel: whether this server advertises itself to the hub, flipped while it runs
/// and remembered across restarts. The engine's own advertiser reads hub.advertise once at boot and cannot be
/// stopped after, so this one advertises in its place, the same request at the same interval, and only while
/// hub.advertise is off in the config; with it on, the engine advertises regardless and the switch can only say so.
/// The choice lives in the data directory, which an update leaves alone. Behind the admin API token, like
/// /symphony/roles.
/// </summary>
public sealed partial class SymphonyHubSystem : EntitySystem
{
    public const string HubPath = "/symphony/hub";
    private static readonly ResPath SwitchFile = new("/symphony_hub.json");
    private static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(1);

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IResourceManager _resources = default!;
    [Dependency] private IHttpClientHolder _http = default!;

    // hub.advertise at boot: the engine's advertiser is running, and nothing here can stop it.
    private bool _engineAdvertises;
    private bool _wanted;
    private string? _address;
    private DateTime _nextPing = DateTime.MinValue;
    private bool _pinging;
    private Timer? _timer;
    // Each hub's last answer, for the panel; the log gets a line only when an answer changes.
    private readonly Dictionary<string, Attempt> _hubs = new();

    public override void Initialize()
    {
        base.Initialize();
        _engineAdvertises = _cfg.GetCVar(CVars.HubAdvertise);
        _wanted = ReadSwitch();
        _statusHost.AddHandler(HandleAsync);
        // A timer rather than Update: the server pauses its simulation while nobody is connected, and an empty
        // server is exactly when the switch gets flipped. Each tick hops onto the main thread, which keeps
        // pumping its queue paused or not, so the fields above need no lock.
        // The timer sits in the runtime's global queue, so its callback must not hold this system: a server that
        // stops without a shutdown (the test pool disposes one that way) would stay in memory through it, systems,
        // entities and all. The callback reaches the system weakly and disposes the timer once the system is gone.
        var link = new TimerLink { System = new WeakReference<SymphonyHubSystem>(this) };
        link.Timer = _timer = new Timer(OnTimer, link, TickEvery, TickEvery);
        if (_wanted && _engineAdvertises)
            Log.Info("hub.advertise is on in the config, so the engine advertises on its own; the panel's switch takes over once it is off");
        else if (_wanted)
            Log.Info("Advertising to the hub, as the panel's switch says");
    }

    public override void Shutdown()
    {
        _timer?.Dispose();
        _timer = null;
        base.Shutdown();
    }

    /// <summary>
    /// The timer callback's state: the system it reaches weakly, and the timer itself so the callback can stop it.
    /// </summary>
    private sealed class TimerLink
    {
        public WeakReference<SymphonyHubSystem> System = default!;
        public Timer? Timer;
    }

    /// <summary>
    /// Queues a tick on the main thread while the system is alive, and disposes the timer once it is collected.
    /// </summary>
    private static void OnTimer(object? state)
    {
        var link = (TimerLink) state!;
        if (link.System.TryGetTarget(out var system))
            system._tasks.RunOnMainThread(system.Tick);
        else
            link.Timer?.Dispose();
    }

    /// <summary>
    /// One advertisement per interval while the switch is on. The request's continuations come back to the
    /// main thread too.
    /// </summary>
    private void Tick()
    {
        if (!_wanted || _engineAdvertises || _pinging || DateTime.UtcNow < _nextPing)
            return;
        _nextPing = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Max(10, _cfg.GetCVar(CVars.HubAdvertiseInterval)));
        _ = AdvertiseAsync();
    }

    /// <summary>
    /// The engine's request, to each hub in hub.hub_urls: the address, and the hub reads the rest from /status.
    /// </summary>
    private async Task AdvertiseAsync()
    {
        _pinging = true;
        try
        {
            var address = await ResolveAddressAsync();
            if (address == null)
                return;

            foreach (var hub in Hubs())
            {
                // The switch may have been flipped while a request was out.
                if (!_wanted)
                    return;
                try
                {
                    using var response = await _http.Client.PostAsJsonAsync(hub + "api/servers/advertise", new AdvertiseRequest(address));
                    if (response.IsSuccessStatusCode)
                    {
                        Record(hub, true, null);
                        continue;
                    }

                    var text = (await response.Content.ReadAsStringAsync()).Trim();
                    Record(hub, false, $"answered {(int) response.StatusCode}{(text.Length > 0 ? ": " + Clip(text) : "")}");
                }
                catch (Exception e)
                {
                    Record(hub, false, "could not be reached: " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Advertising failed: {e}");
        }
        finally
        {
            _pinging = false;
        }
    }

    /// <summary>
    /// hub.server_url when set, else the engine's own guess from hub.ipify_url and net.port, written back to the
    /// cvar the way the engine does. Null, and recorded as the hubs' answer, when neither is to be had.
    /// </summary>
    private async Task<string?> ResolveAddressAsync()
    {
        if (_address != null)
            return _address;
        var configured = _cfg.GetCVar(CVars.HubServerUrl).Trim();
        if (configured != "")
            return _address = configured;

        try
        {
            var answer = await _http.Client.GetFromJsonAsync<IpResponse>(_cfg.GetCVar(CVars.HubIpifyUrl));
            if (string.IsNullOrWhiteSpace(answer?.Ip))
                throw new InvalidOperationException("no address in the answer");
            _address = $"ss14://{answer.Ip}:{_cfg.GetCVar(CVars.NetPort)}/";
            _cfg.SetCVar(CVars.HubServerUrl, _address);
            Log.Info($"Guessed this server's address to be {_address}; set hub.server_url to say otherwise");
            return _address;
        }
        catch (Exception e)
        {
            foreach (var hub in Hubs())
                Record(hub, false, $"was not asked: this server's address could not be worked out ({e.Message}); set hub.server_url");
            return null;
        }
    }

    private List<string> Hubs()
    {
        return _cfg.GetCVar(CVars.HubUrls)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(hub => hub.EndsWith('/') ? hub : hub + "/")
            .ToList();
    }

    private void Record(string hub, bool ok, string? error)
    {
        var same = _hubs.TryGetValue(hub, out var before) && before.Ok == ok && before.Error == error;
        _hubs[hub] = new Attempt(DateTime.UtcNow, ok, error);
        if (same)
            return;
        if (ok)
            Log.Info($"{hub} accepted the advertisement of {_address}");
        else
            Log.Error($"{hub} {error}");
    }

    private static string Clip(string text)
    {
        return text.Length > 200 ? text[..200] : text;
    }

    // Runs on the status host's thread; everything it reads or writes hops to the main thread.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        if (context.Url.AbsolutePath != HubPath)
            return false;
        if (!await SymphonyApi.AuthorisedAsync(_cfg, context))
            return true;

        if (context.RequestMethod == HttpMethod.Get)
        {
            await context.RespondJsonAsync(await _tasks.OnMainThread(Snapshot));
            return true;
        }

        if (context.RequestMethod != HttpMethod.Post)
        {
            await context.RespondErrorAsync(HttpStatusCode.MethodNotAllowed);
            return true;
        }

        SwitchBody? body = null;
        try
        {
            body = await context.RequestBodyJsonAsync<SwitchBody>();
        }
        catch (Exception)
        {
            // Not JSON: refused below.
        }

        if (body?.Advertise is not { } wanted)
        {
            await context.RespondAsync("expected {\"advertise\": true} or {\"advertise\": false}", HttpStatusCode.BadRequest);
            return true;
        }

        await context.RespondJsonAsync(await _tasks.OnMainThread(() => Apply(wanted)));
        return true;
    }

    /// <summary>
    /// The switch itself. On sends the first advertisement on the next tick; off sends no more, and the hub drops
    /// the listing once the last one runs out. Written down first, so a restart in between keeps the choice.
    /// </summary>
    private State Apply(bool wanted)
    {
        if (_wanted != wanted)
        {
            _wanted = wanted;
            WriteSwitch(wanted);
            _nextPing = DateTime.MinValue;
            _hubs.Clear();
            if (_engineAdvertises)
                Log.Info($"The panel set the hub switch {(wanted ? "on" : "off")}; it takes effect once hub.advertise is off in the config");
            else
                Log.Info(wanted ? "The panel put the server on the hub" : "The panel took the server off the hub");
        }

        return Snapshot();
    }

    private State Snapshot()
    {
        var address = _address ?? _cfg.GetCVar(CVars.HubServerUrl).Trim();
        return new State
        {
            SymphonyModule = SharedSymphony.ModuleVersion,
            Advertising = _engineAdvertises || _wanted,
            Switchable = !_engineAdvertises,
            Wanted = _wanted,
            Address = address == "" ? null : address,
            IntervalSeconds = _cfg.GetCVar(CVars.HubAdvertiseInterval),
            Hubs = Hubs().Select(hub =>
            {
                var known = _hubs.TryGetValue(hub, out var last);
                return new HubState
                {
                    Url = hub,
                    LastAt = known ? last!.At.ToString("o") : null,
                    Ok = known ? last!.Ok : null,
                    Error = known ? last!.Error : null,
                };
            }).ToList(),
        };
    }

    private bool ReadSwitch()
    {
        try
        {
            if (!_resources.UserData.Exists(SwitchFile))
                return false;
            return JsonSerializer.Deserialize<Switch>(_resources.UserData.ReadAllText(SwitchFile))?.Advertise == true;
        }
        catch (Exception e)
        {
            Log.Error($"Could not read {SwitchFile}, so the hub switch is off: {e.Message}");
            return false;
        }
    }

    private void WriteSwitch(bool wanted)
    {
        try
        {
            _resources.UserData.WriteAllText(SwitchFile, JsonSerializer.Serialize(new Switch { Advertise = wanted }));
        }
        catch (Exception e)
        {
            Log.Error($"Could not write {SwitchFile}, so the hub switch holds only until the next restart: {e.Message}");
        }
    }

    private sealed record Attempt(DateTime At, bool Ok, string? Error);

    // ReSharper disable once NotAccessedPositionalProperty.Local
    private sealed record AdvertiseRequest(string Address);

    private sealed record IpResponse([property: JsonPropertyName("ip")] string? Ip);

    private sealed class Switch
    {
        [JsonPropertyName("advertise")] public bool Advertise { get; set; }
    }

    private sealed class SwitchBody
    {
        [JsonPropertyName("advertise")] public bool? Advertise { get; set; }
    }

    private sealed class State
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("advertising")] public bool Advertising { get; set; }
        [JsonPropertyName("switchable")] public bool Switchable { get; set; }
        [JsonPropertyName("wanted")] public bool Wanted { get; set; }
        [JsonPropertyName("address")] public string? Address { get; set; }
        [JsonPropertyName("interval_seconds")] public int IntervalSeconds { get; set; }
        [JsonPropertyName("hubs")] public List<HubState> Hubs { get; set; } = new();
    }

    private sealed class HubState
    {
        [JsonPropertyName("url")] public string Url { get; set; } = "";
        [JsonPropertyName("last_at")] public string? LastAt { get; set; }
        [JsonPropertyName("ok")] public bool? Ok { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}
