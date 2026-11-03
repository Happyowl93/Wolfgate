using System.IO;
using Content.Client._WF.MameArcade.Engine;
using Content.Client._WF.MameArcade.NeoGeo;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WF.MameArcade;

/// <summary>
/// Drives MAME.NET's emulator, which keeps all its state in statics: one machine per client, used only from the
/// session's worker thread.
/// </summary>
public static class MameMachine
{
    public const string NeoGeoBoard = "Neo Geo";

    // Neo Geo's visible area in MAME.NET's 384-pixel-wide frame buffer.
    private const int NeoGeoLeft = 30;
    private const int NeoGeoTop = 16;
    private const int NeoGeoWidth = 320;
    private const int NeoGeoHeight = 224;

    private static bool _running;

    public static int Width => Video.width;
    public static int Height => Video.height;

    /// <summary>Seconds of emulated time per frame.</summary>
    public static double FrameSeconds => Video.frame_update_time.attoseconds / 1e18;

    /// <summary>Boots a Neo Geo game from region files built by <see cref="NeoGeoLoader"/>.</summary>
    /// <param name="nvram">Where battery RAM persists; null keeps none, as both players of a 2-player game must.</param>
    /// <param name="audio">Receives 16-bit stereo samples at 48 kHz.</param>
    public static void StartNeoGeo(NeoGeoSet set, NeoGeoImage image, INvramStore? nvram, Action<byte[], int>? audio)
    {
        if (_running)
            Stop();

        ResetStaleState();
        Engine.Timer.lt = new List<Engine.Timer.emu_timer>();
        Machine.rom = new RomInfo
        {
            Name = set.Name,
            Board = NeoGeoBoard,
            Parent = set.Parent,
            Direction = string.Empty,
            Description = set.Title,
            Year = set.Year,
            Manufacturer = set.Manufacturer,
        };
        RomInfo.Rom = Machine.rom;
        Machine.sName = set.Name;
        Machine.sParent = set.Parent;
        Machine.sBoard = NeoGeoBoard;
        Machine.sDirection = string.Empty;
        Machine.sDescription = set.Title;
        Machine.sManufacturer = set.Manufacturer;
        Machine.lsParents = string.IsNullOrEmpty(set.Parent) ? new List<string> { set.Name } : new List<string> { set.Name, set.Parent };
        Machine.RomSource = name => image.Regions.GetValueOrDefault(name);
        Machine.BiosSource = name => image.Bios.GetValueOrDefault(name);
        Machine.Nvram = nvram;
        Engine.Sound.AudioSink = audio;
        Keyboard.Reset();
        Mame.is_foreground = true;
        Mame.paused = false;
        Mame.exit_pending = false;

        Video.iMode = 0;
        Video.offsetx = NeoGeoLeft;
        Video.offsety = NeoGeoTop;
        Video.width = NeoGeoWidth;
        Video.height = NeoGeoHeight;

        Neogeo.NeogeoInit();
        if (!Machine.bRom)
            throw new InvalidOperationException($"{set.Name} is missing a region MAME.NET needs.");

        Mame.init_machine();
        Generic.nvram_load();
        Mame.soft_reset();
        _running = true;
    }

    /// <summary>Emulates one frame with both seats' controls held.</summary>
    public static void RunFrame(MameControls playerOne, MameControls playerTwo)
    {
        MameArcadeKeys.Apply(playerOne, playerTwo, Keyboard.Held);
        Mame.run_frame();
    }

    /// <summary>Copies the visible area of the last frame as RGBA.</summary>
    public static void CopyFrame(Rgba32[] pixels)
    {
        var source = Video.bitmapcolor;
        var stride = Video.fullwidth;
        for (var y = 0; y < Video.height; y++)
        {
            var from = (Video.offsety + y) * stride + Video.offsetx;
            var to = y * Video.width;
            for (var x = 0; x < Video.width; x++)
            {
                var argb = source[from + x];
                pixels[to + x] = new Rgba32((byte) (argb >> 16), (byte) (argb >> 8), (byte) argb, 255);
            }
        }
    }

    /// <summary>A hash of the work RAM, which drifts at once if two 2-player games stop matching.</summary>
    public static uint Checksum()
    {
        var hash = 2166136261u;
        hash = Hash(hash, Memory.mainram);
        hash = Hash(hash, Memory.audioram);
        return hash;
    }

    public static byte[] SaveState()
    {
        using var stream = new MemoryStream();
        State.savestate_callback(new Engine.BinaryWriter(stream));
        return stream.ToArray();
    }

    public static void LoadState(byte[] state)
    {
        using var stream = new MemoryStream(state);
        State.loadstate_callback(new BinaryReader(stream));
        Mame.postload();
    }

    /// <summary>Saves battery RAM and detaches the frontend.</summary>
    public static void Stop()
    {
        if (_running)
            Generic.nvram_save();

        _running = false;
        Engine.Sound.AudioSink = null;
        Machine.RomSource = null;
        Machine.BiosSource = null;
        Machine.Nvram = null;

        // The engine keeps the last game's ROMs in statics; the biggest Neo Geo sets hold hundreds of MB.
        Memory.mainrom = null;
        Memory.audiorom = null;
        Neogeo.spritesrom = null;
        Neogeo.sprite_gfx = null;
        Neogeo.fixedrom = null;
        FM.ymsndrom = null;
        YMDeltat.ymsnddeltatrom = null;
        YM2610.F2610.pcmbuf = null;
    }

    /// <summary>
    /// MAME.NET leaves some state from the last game in place, and video_init schedules the first vblank from it.
    /// Two players' emulators must boot identically, so clear it first.
    /// </summary>
    private static void ResetStaleState()
    {
        Video.screenstate = default;
        Video.new_clip = default;
        Video.frame_completed = false;
        Video.curbitmap = 0;
        Pd4900a.shiftlo = 0;
        Pd4900a.shifthi = 0;
        Pd4900a.retraces = 0;
        Pd4900a.testwaits = 0;
        Pd4900a.testbit = 0;
        Pd4900a.outputbit = 0;
        Pd4900a.bitno = 0;
        Pd4900a.reading = 0;
        Pd4900a.writting = 0;
        Pd4900a.clock_line = 0;
        Pd4900a.command_line = 0;
    }

    private static uint Hash(uint hash, byte[]? data)
    {
        if (data == null)
            return hash;

        foreach (var value in data)
        {
            hash = (hash ^ value) * 16777619u;
        }

        return hash;
    }
}
