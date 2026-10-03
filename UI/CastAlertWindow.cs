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

        ImGui.SetNextWindowSize(new Vector2(400, 100), ImGuiCond.Always);
        var viewport = ImGui.GetMainViewport();
        var center = viewport.GetCenter();
        ImGui.SetNextWindowPos(new Vector2(center.X - 200, center.Y - 180), ImGuiCond.Always);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration |
                                 ImGuiWindowFlags.NoInputs |
                                 ImGuiWindowFlags.NoMove |
                                 ImGuiWindowFlags.NoSavedSettings |
                                 ImGuiWindowFlags.AlwaysAutoResize;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.08f, 0.08f, 0.12f, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 0.2f, 0.2f, 0.9f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f);

        if (ImGui.Begin("##EurekaAggro_CastAlert", flags))
        {
            ImGui.TextColored(new Vector4(1.0f, 0.3f, 0.3f, 1.0f), "⚠ ENEMY ACTION ALERT");
            ImGui.Separator();

            ImGui.TextColored(new Vector4(1.0f, 0.9f, 0.1f, 1.0f), alert.MainMessage);

            ImGui.TextDisabled($"{alert.MobName} is casting: ");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.8f, 1.0f, 1.0f), alert.ActionName);

            if (alert.CastProgress > 0)
            {
                ImGui.ProgressBar(alert.CastProgress, new Vector2(-1, 8), "");
            }
        }
        ImGui.End();

        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }
}
