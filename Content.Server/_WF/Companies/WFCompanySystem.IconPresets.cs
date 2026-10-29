using Content.Shared._WF.Companies;
using Content.Shared.StatusIcon;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Server._WF.Companies;

// Job icons to start a rank icon from. Packaged clients pack RSIs into one file, so the server reads the images.
public sealed partial class WFCompanySystem
{
    [Dependency] private IResourceManager _resources = default!;

    private readonly Dictionary<string, byte[]?> _iconPresets = new();

    private void SendIconPreset(ICommonSession session, string? icon)
    {
        if (icon == null || !_prototypes.TryIndex<JobIconPrototype>(icon, out var proto) || proto.Icon is not SpriteSpecifier.Rsi rsi)
            return;
        if (!_iconPresets.TryGetValue(icon, out var pixels))
            _iconPresets[icon] = pixels = ReadIconPreset(rsi);
        RaiseNetworkEvent(new WFCompanyIconPresetEvent(icon, pixels), session.Channel);
    }

    /// <summary>The top-left icon-sized corner of the state's first frame as raw RGBA, or null if it can't be read.</summary>
    private byte[]? ReadIconPreset(SpriteSpecifier.Rsi rsi)
    {
        try
        {
            using var stream = _resources.ContentFileRead(rsi.RsiPath / $"{rsi.RsiState}.png");
            using var image = Image.Load<Rgba32>(stream);
            var size = WFCompanyPolicy.IconSize;
            if (image.Width < size || image.Height < size)
                return null;
            var pixels = new byte[WFCompanyPolicy.IconBytes];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var pixel = image[x, y];
                    var i = (y * size + x) * 4;
                    pixels[i] = pixel.R;
                    pixels[i + 1] = pixel.G;
                    pixels[i + 2] = pixel.B;
                    pixels[i + 3] = pixel.A;
                }
            }
            return pixels;
        }
        catch (Exception e)
        {
            _log.Warning($"Couldn't read job icon {rsi.RsiPath}/{rsi.RsiState}: {e.Message}");
            return null;
        }
    }
}
