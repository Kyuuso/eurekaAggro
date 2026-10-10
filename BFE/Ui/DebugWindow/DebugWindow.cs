using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using BFE.Windows;

namespace BFE.Ui.DebugWindow;

/// <summary>
/// Ventana de depuración técnica para inspección de estadísticas y variables internas.
/// </summary>
internal class DebugWindow : PositionedWindow
{
    public DebugWindow() : base($"{PluginInfo.DisplayName} Debug ###BFEDebugWindow")
    {
        SizeConstraints = new()
        {
            MinimumSize = new(150, 150),
            MaximumSize = new(9999, 9999)
        };
        Flags |= ImGuiWindowFlags.HorizontalScrollbar;

        TitleBarButtons.Add(new()
        {
            Click = (m) => { if (m == ImGuiMouseButton.Left) P.settingsWindow.IsOpen = !P.settingsWindow.IsOpen; },
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new(2, 2),
            ShowTooltip = () => ImGui.SetTooltip(UiText.T("Open settings window"))
        });

        P.windowSystem.AddWindow(this);
    }

    public void Dispose() { }

    public override void Draw()
    {
        UiGui.Title($"{PluginInfo.DisplayName} Debug", UiText.T("BFE Debug"));
        bool debug = C.enableDebug;
        if (UiGui.Checkbox("Debug Stats", ref debug))
        {
            C.UpdatePyrosStats(pyrosStats => { pyrosStats.gilEarned = C.stats.gilEarned; });
            C.UpdatePyrosStats(pyrosStats => { pyrosStats.goldCoffer = C.stats.goldCoffer; });
            C.UpdatePyrosStats(pyrosStats => { pyrosStats.silverCoffer = C.stats.silverCoffer; });
            C.UpdatePyrosStats(pyrosStats => { pyrosStats.bronzeCoffer = C.stats.bronzeCoffer; });
            C.UpdatePyrosStats(pyrosStats => { pyrosStats.eldthursCounter = C.stats.eldthursCounter; });
            C.UpdatePyrosStats(pyrosStats => { pyrosStats.pyrosHairStyleCounter = C.stats.pyrosHairStyleCounter; });
            C.enableDebug = debug;
        }
        FinalizePendingWindowPlacement();
    }
}
