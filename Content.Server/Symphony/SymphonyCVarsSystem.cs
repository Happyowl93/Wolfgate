using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Content.Shared.Symphony;
using Robust.Server.ServerStatus;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Reflection;

namespace Content.Server.Symphony;

/// <summary>
/// The server's CVars for the SSymphony panel: every registered one with its type, value, default and flags, and a
/// way to set one while the server runs, the same as the cvar console command. A confidential CVar is listed by
/// name with no value and cannot be set from here, and neither can a client-only one. Nothing is written to the
/// config file: a value set here holds until the next restart, and the panel writes the file itself when it should
/// outlast one. Behind the admin API token, like /symphony/roles.
/// </summary>
public sealed partial class SymphonyCVarsSystem : EntitySystem
{
    /// <summary>Status host path: GET lists the CVars, POST sets one.</summary>
    public const string CVarsPath = "/symphony/cvars";

    [Dependency] private IStatusHost _statusHost = default!;
    [Dependency] private ITaskManager _tasks = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IReflectionManager _reflection = default!;

    // The engine keeps a CVar's default and description to itself, so they are read off the definitions.
    private readonly Dictionary<string, CVarDef> _defs = new();

    public override void Initialize()
    {
        base.Initialize();
        foreach (var type in _reflection.FindTypesWithAttribute<CVarDefsAttribute>())
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (field.GetValue(null) is CVarDef def)
                    _defs[def.Name] = def;
            }
        }

        _statusHost.AddHandler(HandleAsync);
    }

    // Runs on the status host's thread; the configuration is main-thread state, so reading and setting hop across.
    private async Task<bool> HandleAsync(IStatusHandlerContext context)
    {
        if (context.Url.AbsolutePath != CVarsPath)
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

        SetBody? body = null;
        try
        {
            body = await context.RequestBodyJsonAsync<SetBody>();
        }
        catch (Exception)
        {
            // Not JSON: refused below.
        }

        if (body?.Name is not { Length: > 0 } name || body.Value is not { } value)
        {
            await context.RespondAsync("expected {\"name\": \"a.cvar\", \"value\": \"as text\"}", HttpStatusCode.BadRequest);
            return true;
        }

        var result = await _tasks.OnMainThread(() => Apply(name, value, body.Actor));
        await context.RespondJsonAsync(result, result.Ok ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        return true;
    }

    /// <summary>
    /// Sets one CVar from its text form, as the cvar command does. The value is parsed as the CVar's own type, so a
    /// word where a number belongs is refused rather than stored.
    /// </summary>
    private SetResponse Apply(string name, string value, string? actor)
    {
        var refused = new SetResponse { SymphonyModule = SharedSymphony.ModuleVersion, Name = name };
        if (!_cfg.IsCVarRegistered(name))
        {
            refused.Error = "no CVar by that name";
            return refused;
        }

        var flags = _cfg.GetCVarFlags(name);
        if ((flags & CVar.CONFIDENTIAL) != 0)
        {
            refused.Error = "that CVar is confidential and is not set from the panel";
            return refused;
        }

        if ((flags & CVar.CLIENTONLY) != 0)
        {
            refused.Error = "that CVar belongs to the client";
            return refused;
        }

        var type = _cfg.GetCVarType(name);
        object parsed;
        try
        {
            parsed = CVarCommandUtil.ParseObject(type, value);
        }
        catch (Exception)
        {
            refused.Error = $"\"{value}\" is not a valid {TypeName(type)}";
            return refused;
        }

        var old = _cfg.GetCVar(name);
        try
        {
            _cfg.SetCVar(name, parsed);
        }
        catch (Exception e)
        {
            refused.Error = $"the game refused the value: {e.Message}";
            return refused;
        }

        Log.Info($"The panel set {name} to {Text(parsed)} (was {Text(old)}){(string.IsNullOrEmpty(actor) ? "" : $", for {actor}")}");
        return new SetResponse
        {
            SymphonyModule = SharedSymphony.ModuleVersion,
            Ok = true,
            Name = name,
            Old = old,
            Value = _cfg.GetCVar(name),
        };
    }

    private CVarsResponse Snapshot()
    {
        var list = new List<CVarEntry>();
        foreach (var name in _cfg.GetRegisteredCVars().OrderBy(n => n, StringComparer.Ordinal))
        {
            var flags = _cfg.GetCVarFlags(name);
            // The client's own settings are registered on the server too, and mean nothing here.
            if ((flags & CVar.CLIENTONLY) != 0)
                continue;

            var confidential = (flags & CVar.CONFIDENTIAL) != 0;
            _defs.TryGetValue(name, out var def);
            list.Add(new CVarEntry
            {
                Name = name,
                Type = TypeName(_cfg.GetCVarType(name)),
                Value = confidential ? null : _cfg.GetCVar(name),
                Default = confidential ? null : def?.DefaultValue,
                Description = def?.Desc,
                Flags = FlagNames(flags),
                Confidential = confidential,
            });
        }

        return new CVarsResponse { SymphonyModule = SharedSymphony.ModuleVersion, CVars = list };
    }

    private static string TypeName(Type type)
    {
        if (type == typeof(bool))
            return "bool";
        if (type == typeof(string))
            return "string";
        if (type == typeof(int))
            return "int";
        if (type == typeof(float))
            return "float";
        if (type == typeof(long))
            return "long";
        if (type == typeof(ushort))
            return "ushort";
        return type.Name;
    }

    private static List<string> FlagNames(CVar flags)
    {
        var names = new List<string>();
        foreach (var flag in Enum.GetValues<CVar>())
        {
            if (flag != CVar.NONE && (flags & flag) == flag)
                names.Add(flag.ToString());
        }

        return names;
    }

    private static string Text(object? value)
    {
        return value is IFormattable f ? f.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
    }

    private sealed class SetBody
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("value")] public string? Value { get; set; }
        [JsonPropertyName("actor")] public string? Actor { get; set; }
    }

    private sealed class SetResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("ok")] public bool Ok { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("old")] public object? Old { get; set; }
        [JsonPropertyName("value")] public object? Value { get; set; }
        [JsonPropertyName("error")] public string? Error { get; set; }
    }

    private sealed class CVarsResponse
    {
        [JsonPropertyName("symphony_module")] public int SymphonyModule { get; set; }
        [JsonPropertyName("cvars")] public List<CVarEntry> CVars { get; set; } = new();
    }

    private sealed class CVarEntry
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("type")] public string Type { get; set; } = "";
        [JsonPropertyName("value")] public object? Value { get; set; }
        [JsonPropertyName("default")] public object? Default { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("flags")] public List<string> Flags { get; set; } = new();
        [JsonPropertyName("confidential")] public bool Confidential { get; set; }
    }
}
