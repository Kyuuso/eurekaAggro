using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using ECommons.ImGuiMethods;
using ECommons.Logging;

namespace BFE.Ui.SettingsWindow;

/// <summary>
/// AutoRetainer integration settings for retainer automation during bunny sessions.
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
            UiGui.TextColored(ImGuiColors.DalamudRed, "AutoRetainer is currently not installed or enabled. Click to copy Repo URL.");
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
                    DuoLog.Information("AutoRetainer Repo URL copied.");
                    Notify.Info("Repo URL Copied");
                }
                ImGui.GetWindowDrawList().AddRectFilled(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.ColorConvertFloat4ToU32(bg));
            }
            C.Save();
        }

        if (PluginInstalled("AutoRetainer"))
        {
            if (Helpers.CheckboxWithTooltip("Enable Retainer Support", ref EnableRetainers,
                "Enables retainer processing and dispatching for this character."))
            {
                C.enableRetainers = EnableRetainers;
                C.Save();
            }

            if (Helpers.CheckboxWithTooltip("Enable Submarine Support", ref EnableSubs,
                "Currently not supported."))
            {
                C.enableSubs = EnableSubs;
                C.Save();
            }

            if (Helpers.CheckboxWithTooltip("Enable Multi Support", ref EnableMulti,
                "Currently not supported."))
            {
                C.enableMulti = EnableMulti;
                C.Save();
            }
        }
    }
}
