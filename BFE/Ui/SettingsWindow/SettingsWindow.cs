using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using BFE.Ui.SettingsWindow;
using BFE.Windows;

namespace BFE.Ui.SettingWindow;

/// <summary>
/// Eureka bunny automation settings window (Bunny Fate Engine).
/// Configures gear repair thresholds, teleport triggers, AutoRetainer integration, and window appearance.
/// </summary>
internal class SettingsWindow : PositionedWindow
{
    public SettingsWindow() : base($"{PluginInfo.DisplayName} Settings ###BFESettingsWindow")
    {
        Flags |= ImGuiWindowFlags.HorizontalScrollbar;
        SizeConstraints = new()
        {
            MinimumSize = new(620, 500),
            MaximumSize = new(1180, 980)
        };
        P.windowSystem.AddWindow(this);
    }

    public void Dispose() { }

    public override void Draw()
    {
        UiGui.Title($"{PluginInfo.DisplayName} Settings", UiText.T("BFE Settings"));

        if (ImGui.BeginTabBar("Bunnies Settings Tabs", ImGuiTabBarFlags.FittingPolicyScroll))
        {
            if (UiGui.TabItem("General Settings"))
            {
                GeneralSettings.Draw();
                ImGui.EndTabItem();
            }

            if (UiGui.TabItem("AutoRetainer Settings"))
            {
                AutoReatinerSettings.Draw();
                ImGui.EndTabItem();
            }

            if (UiGui.TabItem("Window appearance"))
            {
                P.appearance.DrawWindowAppearanceSettings();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        FinalizePendingWindowPlacement();
    }
}
