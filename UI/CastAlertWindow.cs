using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using EurekaSuite.Configuration;
using EurekaSuite.Services;

namespace EurekaSuite.UI;

/// <summary>
/// Floating HUD window showing active cast alerts when dangerous actions are being performed.
/// Supports user dragging, locking, screen position persistence, and live preview mode.
/// </summary>
public class CastAlertWindow
{
    private readonly CastMonitor monitor;
    private readonly PluginConfiguration config;

    /// <summary>
    /// When true, displays a mock alert so the player can test, preview, and reposition the HUD element.
    /// </summary>
    public bool IsPreviewMode { get; set; } = false;

    private readonly ActiveCastAlert previewAlert = new()
    {
        MobName = "Gawper (Preview)",
        ActionName = "Eyes on Me",
        MainMessage = "INTERRUPT / SILENCE AVAILABLE!",
        IsInterruptible = true,
        RequiresSilence = true,
        CastProgress = 0.55f,
        DurationSeconds = 9999f
    };

    public CastAlertWindow(CastMonitor monitor, PluginConfiguration config)
    {
        this.monitor = monitor;
        this.config = config;
    }

    public void ResetPosition()
    {
        var viewport = ImGui.GetMainViewport();
        var center = viewport.GetCenter();
        config.CastAlertPosition = new Vector2(center.X - 220, center.Y - 185);
        config.Save();
    }

    public void Draw()
    {
        if (!config.Enabled || !config.ShowCastAlerts) return;

        var alert = monitor.ActiveAlert;
        if (alert == null || alert.IsExpired)
        {
            if (!IsPreviewMode) return;
            alert = previewAlert;
        }

        // Apply saved position or default to viewport center
        if (config.CastAlertPosition.X >= 0 && config.CastAlertPosition.Y >= 0)
        {
            ImGui.SetNextWindowPos(config.CastAlertPosition, ImGuiCond.FirstUseEver);
        }
        else
        {
            var viewport = ImGui.GetMainViewport();
            var center = viewport.GetCenter();
            var defPos = new Vector2(center.X - 220, center.Y - 185);
            ImGui.SetNextWindowPos(defPos, ImGuiCond.FirstUseEver);
        }

        ImGui.SetNextWindowSize(new Vector2(440, 125), ImGuiCond.Always);

        ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration |
                                 ImGuiWindowFlags.AlwaysAutoResize;

        // When locked and not in preview mode, prevent moving and click-through
        if (config.LockCastAlertPosition && !IsPreviewMode)
        {
            flags |= ImGuiWindowFlags.NoMove;
        }

        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.06f, 0.07f, 0.11f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 0.25f, 0.25f, 0.95f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 2.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8.0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 10));

        if (ImGui.Begin("##EurekaSuite_CastAlert", flags))
        {
            // Persist position whenever user moves the window
            var currentPos = ImGui.GetWindowPos();
            if (currentPos != config.CastAlertPosition && (currentPos.X >= 0 && currentPos.Y >= 0))
            {
                config.CastAlertPosition = currentPos;
                config.Save();
            }

            // Header banner (shows drag hint when unlocked or previewing)
            string headerTitle = (!config.LockCastAlertPosition || IsPreviewMode)
                ? "[!] ENEMY ACTION ALERT (Drag to move)"
                : "[!] ENEMY ACTION ALERT";

            ImGui.TextColored(new Vector4(1.0f, 0.35f, 0.35f, 1.0f), headerTitle);
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

            // Progress bar (20px tall to guarantee percentage text is never clipped vertically)
            if (alert.CastProgress > 0)
            {
                ImGui.Spacing();
                string progressLabel = $"{alert.CastProgress * 100:F0}%";
                ImGui.ProgressBar(alert.CastProgress, new Vector2(-1, 20), progressLabel);
            }
        }
        ImGui.End();

        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }
}
