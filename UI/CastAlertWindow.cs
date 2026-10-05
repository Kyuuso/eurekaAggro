using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EurekaAggro.Configuration;
using EurekaAggro.Services;

namespace EurekaAggro.UI;

/// <summary>
/// Floating HUD window showing active cast alerts when dangerous actions are being performed.
/// </summary>
public class CastAlertWindow
{
    private readonly CastMonitor monitor;
    private readonly PluginConfiguration config;

    public CastAlertWindow(CastMonitor monitor, PluginConfiguration config)
    {
        this.monitor = monitor;
        this.config = config;
    }

    public void Draw()
    {
        if (!config.Enabled || !config.ShowCastAlerts) return;

        var alert = monitor.ActiveAlert;
        if (alert == null || alert.IsExpired) return;

        ImGui.SetNextWindowSize(new Vector2(440, 115), ImGuiCond.Always);
        var viewport = ImGui.GetMainViewport();
        var center = viewport.GetCenter();
        ImGui.SetNextWindowPos(new Vector2(center.X - 220, center.Y - 185), ImGuiCond.Always);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration |
                                 ImGuiWindowFlags.NoInputs |
                                 ImGuiWindowFlags.NoMove |
                                 ImGuiWindowFlags.NoSavedSettings |
                                 ImGuiWindowFlags.AlwaysAutoResize;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.06f, 0.07f, 0.11f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 0.25f, 0.25f, 0.95f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 10));

        if (ImGui.Begin("##EurekaAggro_CastAlert", flags))
        {
            // ASCII alert banner to ensure crisp rendering on all client fonts
            ImGui.TextColored(new Vector4(1.0f, 0.35f, 0.35f, 1.0f), "[!] ENEMY ACTION ALERT");
            ImGui.Separator();
            ImGui.Spacing();

            // Main Tactical Instruction Banner
            Vector4 bannerColor;
            string bannerText = alert.MainMessage;

            if (alert.RequiresStun)
            {
                bannerColor = new Vector4(1.0f, 0.65f, 0.15f, 1.0f);
                if (string.IsNullOrWhiteSpace(bannerText)) bannerText = "STUN REQUIRED!";
            }
            else if (alert.RequiresLineOfSight)
            {
                bannerColor = new Vector4(0.35f, 0.85f, 1.0f, 1.0f);
                if (string.IsNullOrWhiteSpace(bannerText)) bannerText = "BREAK LINE OF SIGHT (HIDE)!";
            }
            else
            {
                bannerColor = new Vector4(1.0f, 0.95f, 0.15f, 1.0f);
                if (string.IsNullOrWhiteSpace(bannerText)) bannerText = "INTERRUPT / SILENCE AVAILABLE!";
            }

            ImGui.TextColored(bannerColor, bannerText);

            // Mob & Action line
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.80f, 1.0f), $"{alert.MobName} is casting: ");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.40f, 0.85f, 1.0f, 1.0f), alert.ActionName);

            if (alert.CastProgress > 0)
            {
                ImGui.Spacing();
                string progressLabel = $"{alert.CastProgress * 100:F0}%";
                ImGui.ProgressBar(alert.CastProgress, new Vector2(-1, 10), progressLabel);
            }
        }
        ImGui.End();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }
}
