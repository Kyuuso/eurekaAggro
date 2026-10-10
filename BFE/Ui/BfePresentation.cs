using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

namespace BFE.Ui;

/// <summary>
/// Provides visual styling constants and native graphical drawing without external dependencies.
/// </summary>
internal static class BfePresentation
{
    internal static IDalamudTextureWrap? OriginalIcon => null;

    internal static void DrawPluginIcon(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        // When no texture is present, render a rounded fallback background
        drawList.AddRectFilled(min, max, 0x44FFFFFF, 4f);
    }

    internal const uint ReferenceAccent = 0xFFD05A;
    internal static bool Compact => false;
    internal static float HeaderHeight => 90f;
    internal static float Gap => 20f;
    internal static float ControlHeight => 40f;
    internal static float FooterHeight => 50f;

    internal static Vector4 Rgb(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);

    internal static Vector4 ActionFill => new(0.95f, 0.73f, 0.32f, 1f);

    internal static void Discord(Vector2 position, float size, Vector4 foreground)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddText(position, ImGui.ColorConvertFloat4ToU32(foreground), "Discord");
    }

    internal static void Surface(Vector2 min, Vector2 max)
    {
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(min, max, 0x22FFFFFF, 6f);
    }
}
