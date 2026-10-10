using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using ECommons.ImGuiMethods;
using ECommons.Logging;

namespace BFE.Ui.SettingsWindow;

/// <summary>
/// Configuración de integración con AutoRetainer para procesamiento automático de retainers.
/// </summary>
internal class AutoReatinerSettings
{
    private static bool EnableRetainers = C.enableRetainers;
    private static bool EnableSubs = C.enableSubs;
    private static bool EnableMulti = C.enableMulti;

    public static void Draw()
    {
        if (!PluginInstalled("AutoRetainer"))
        {
            ImGui.PushTextWrapPos(0);
            UiGui.TextColored(ImGuiColors.DalamudRed, "AutoRetainer no está instalado o activado. Haz clic para copiar el repositorio.");
            ImGui.PopTextWrapPos();

            C.enableRetainers = false;
            C.enableSubs = false;
            C.enableMulti = false;

            if (ImGui.IsItemHovered())
            {
                var bg = new Vector4(1f, 1f, 1f, 0.15f);
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText(IPC.AutoRetainerIPC.Repo);
                    DuoLog.Information("URL del repositorio de AutoRetainer copiada.");
                    Notify.Info(UiText.T("Repo URL Copied"));
                }
                ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.ColorConvertFloat4ToU32(bg));
            }
            C.Save();
        }

        if (PluginInstalled("AutoRetainer"))
        {
            if (Helpers.CheckboxWithTooltip("Activar soporte de Retainers", ref EnableRetainers,
                "Activa el envío y recolección automática de retainers para este personaje."))
            {
                C.enableRetainers = EnableRetainers;
                C.Save();
            }

            if (Helpers.CheckboxWithTooltip("Activar soporte de Submarinos", ref EnableSubs,
                "Actualmente no soportado."))
            {
                C.enableSubs = EnableSubs;
                C.Save();
            }

            if (Helpers.CheckboxWithTooltip("Activar soporte de Multi-personaje", ref EnableMulti,
                "Actualmente no soportado."))
            {
                C.enableMulti = EnableMulti;
                C.Save();
            }
        }
    }
}
