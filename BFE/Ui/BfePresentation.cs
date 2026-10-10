using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace BFE.Ui;

internal enum UiFontRole { Body, BodyStrong, Title, PluginName, Counter, Action, CompactTitle }

internal static class BfePresentation
{
    // Dalamud owns the shared texture through render submission; callers borrow its wrapper.
    internal static Dalamud.Interface.Textures.TextureWraps.IDalamudTextureWrap? OriginalIcon
        => Plugin.P.TextureProvider.GetFromManifestResource(typeof(Plugin).Assembly, "BFE.images.icon.png").GetWrapOrDefault();

    internal static void DrawPluginIcon(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        var texture = OriginalIcon;
        if (texture is not null)
            MaterialCanvas.DrawImage(drawList, texture.Handle, new Vector2(texture.Width, texture.Height), min, max);
    }

    internal const uint ReferenceAccent = 0xFFD05A;
    internal static readonly float[] FontSizes = [20, 20, 56, 22, 22, 20, 44];
    internal static readonly string[] FontFiles = ["segoeui.ttf", "seguisb.ttf", "segoeuib.ttf", "seguisb.ttf", "seguisb.ttf", "seguisb.ttf", "segoeuib.ttf"];
    internal static float AtlasHeight(UiFontRole role) => FontSizes[(int)role] * 4 / 3;
    internal static bool Compact => MaterialTheme.Current.Density == MaterialDensity.Compact;
    internal static float HeaderHeight => Compact ? 91 : 106;
    internal static float Gap => Compact ? 23 : 30;
    internal static float ControlHeight => Compact ? 40 : 48;
    internal static float FooterHeight => Compact ? 56 : 64;
    internal static Vector4 Rgb(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
    internal static Vector4 ActionFill
    {
        get
        {
            var referenceFill = Rgb(Compact ? 0xF8C054u : 0xF1BA52u);
            var primary = MaterialTheme.Current.Colors.Primary;
            if (primary == Rgb(ReferenceAccent)) return referenceFill;
            var basis = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(Rgb(ReferenceAccent).X, Rgb(ReferenceAccent).Y, Rgb(ReferenceAccent).Z)));
            var selected = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(primary.X, primary.Y, primary.Z)));
            var fill = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(referenceFill.X, referenceFill.Y, referenceFill.Z)));
            return new(MaterialColor.GamutMap(fill.X, selected.Y < .001f ? 0 : fill.Y * selected.Y / basis.Y, fill.Z + selected.Z - basis.Z), 1);
        }
    }

    internal static MaterialTheme Theme(uint accent)
    {
        accent &= 0xFFFFFF;
        var selected = Rgb(accent);
        var reference = Rgb(ReferenceAccent);
        var seed = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(selected.X, selected.Y, selected.Z)));
        var original = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(reference.X, reference.Y, reference.Z)));
        var hue = seed.Y < .001f ? 0 : seed.Z - original.Z;
        var chroma = seed.Y < .001f ? 0 : seed.Y / original.Y;
        Vector4 Relative(uint rgb)
        {
            var color = Rgb(rgb);
            if (accent == ReferenceAccent) return color;
            var lch = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(color.X, color.Y, color.Z)));
            return new(MaterialColor.GamutMap(lch.X, lch.Y * chroma, lch.Z + hue), 1);
        }
        var background = Relative(0x11171B);
        var foreground = Relative(0xF0F1F4);
        var primary = Relative(ReferenceAccent);
        var palette = new OklchPaletteGenerator().Generate(new(selected.X, selected.Y, selected.Z));
        var colors = new MaterialColorScheme(palette)
        {
            Background = background, OnBackground = foreground,
            Surface = Relative(0x151B1F), OnSurface = foreground,
            SurfaceContainerLowest = Relative(0x13191D), SurfaceContainerLow = Relative(0x151B1F),
            SurfaceContainer = Relative(0x192024), SurfaceContainerHigh = Relative(0x1D252A), SurfaceContainerHighest = Relative(0x222A2F),
            SurfaceVariant = Relative(0x343F46), OnSurfaceVariant = Relative(0xB8BCC5),
            Outline = Relative(0x49535B), OutlineVariant = Relative(0x35424A),
            Primary = primary, OnPrimary = MaterialColor.Contrast(primary, background) >= MaterialColor.Contrast(primary, foreground) ? background : foreground,
            PrimaryContainer = Relative(0x433A25), OnPrimaryContainer = foreground,
            Secondary = Relative(0xC4BCA8), OnSecondary = background, SecondaryContainer = Relative(0x1D252A), OnSecondaryContainer = foreground,
            Tertiary = Relative(0x75BFFF), OnTertiary = background, TertiaryContainer = Relative(0x363127), OnTertiaryContainer = foreground,
            InverseSurface = foreground, InverseOnSurface = background, InversePrimary = Relative(0x7E6123),
        };
        return new(colors, MaterialDensity.Standard) { SurfaceOpacity = 1 };
    }

    internal static MaterialControlMetrics Controls(float height = 0)
    {
        if (height <= 0) height = ControlHeight;
        var s = MaterialTheme.Metrics.Scale;
        return new() { Height = height * s, Padding = new(12 * s, Math.Max(0, (height * s - ImGui.GetTextLineHeight()) * .5f)),
            Gap = 8 * s, IconSize = 22 * s, Rounding = 4 * s, ItemSpacing = new(10 * s, 6 * s), CellPadding = new(12 * s, 6 * s) };
    }

    internal static void Surface(Vector2 min, Vector2 max)
    {
        var c = MaterialTheme.Current.Colors;
        MaterialCanvas.Surface(min, max, c.SurfaceContainerHigh, c.Surface, 4 * MaterialTheme.Metrics.Scale);
        ImGui.GetWindowDrawList().AddRect(min, max, MaterialCanvas.Color(c.OutlineVariant), 4 * MaterialTheme.Metrics.Scale);
    }

    internal static void Rabbit(Vector2 origin, float size)
    {
        DrawPluginIcon(ImGui.GetWindowDrawList(), origin, origin + new Vector2(size));
    }

    internal static void Discord(Vector2 origin, float size, Vector4 color)
    {
        var dl = ImGui.GetWindowDrawList(); var ink = MaterialCanvas.Color(color);
        dl.PathLineTo(origin + new Vector2(.20f, .22f) * size);
        dl.PathLineTo(origin + new Vector2(.80f, .22f) * size);
        dl.PathLineTo(origin + new Vector2(.96f, .72f) * size);
        dl.PathLineTo(origin + new Vector2(.78f, .84f) * size);
        dl.PathLineTo(origin + new Vector2(.22f, .84f) * size);
        dl.PathLineTo(origin + new Vector2(.04f, .72f) * size);
        dl.PathFillConvex(ink);
        var cutout = MaterialCanvas.Color(MaterialTheme.Current.Colors.Surface);
        dl.AddCircleFilled(origin + new Vector2(.34f, .48f) * size, .07f * size, cutout, 16);
        dl.AddCircleFilled(origin + new Vector2(.66f, .48f) * size, .07f * size, cutout, 16);
    }
}
