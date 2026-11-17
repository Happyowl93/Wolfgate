using Content.Shared._WG.CCVar;
using Robust.Shared.Configuration;

namespace Content.Client.Shuttles.UI;

// Every radar screen starts with the display choices the player last made on any of them; see Content.Server/_WG/Shuttles.
public partial class ShuttleNavControl
{
    [Dependency] private IConfigurationManager _wgSettingsCfg = default!;

    private bool _wgSavedIff;
    private bool _wgSavedIffDetailed;
    private bool _wgSavedDocks;
    private RadarAzimuthMode _wgSavedAzimuth;
    private bool _wgDocksPinned;

    /// <summary>Starts the radar with the player's saved contact labels, dock markers and bearing ring.</summary>
    private void WgLoadDisplaySettings()
    {
        ShowIFF = _wgSavedIff = _wgSettingsCfg.GetCVar(WolfgateCVars.RadarShowIff);
        ShowIFFDetailed = _wgSavedIffDetailed = _wgSettingsCfg.GetCVar(WolfgateCVars.RadarShowIffDetailed);
        ShowDocks = _wgSavedDocks = _wgSettingsCfg.GetCVar(WolfgateCVars.RadarShowDocks);
        _azimuthMode = _wgSavedAzimuth = _wgSettingsCfg.GetCVar(WolfgateCVars.RadarAzimuth) switch
        {
            1 => RadarAzimuthMode.Ring,
            2 => RadarAzimuthMode.Off,
            _ => RadarAzimuthMode.Edge,
        };
    }

    /// <summary>For a radar screen with no docks toggle: docks always show and its state is never saved.</summary>
    public void WgPinDocks()
    {
        _wgDocksPinned = true;
        ShowDocks = true;
    }

    /// <summary>Saves a display choice when the player changes it.</summary>
    private void WgSaveDisplaySettings()
    {
        var docks = _wgDocksPinned ? _wgSavedDocks : ShowDocks;
        if (ShowIFF == _wgSavedIff && ShowIFFDetailed == _wgSavedIffDetailed && docks == _wgSavedDocks &&
            _azimuthMode == _wgSavedAzimuth)
            return;

        _wgSettingsCfg.SetCVar(WolfgateCVars.RadarShowIff, _wgSavedIff = ShowIFF);
        _wgSettingsCfg.SetCVar(WolfgateCVars.RadarShowIffDetailed, _wgSavedIffDetailed = ShowIFFDetailed);
        _wgSettingsCfg.SetCVar(WolfgateCVars.RadarShowDocks, _wgSavedDocks = docks);
        _wgSettingsCfg.SetCVar(WolfgateCVars.RadarAzimuth, (int) (_wgSavedAzimuth = _azimuthMode));
        _wgSettingsCfg.SaveToFile();
    }
}
