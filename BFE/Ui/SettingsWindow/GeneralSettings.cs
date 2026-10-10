using Dalamud.Interface.Components;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BFE.Ui.SettingsWindow
{
    internal class GeneralSettings
    {
        private static bool TeleportToHouse = C.teleportToHouse;
        private static bool TeleportToFC = C.teleportToFC;
        private static bool TeleportToPersonal = C.teleportToPersonal;
        private static bool LogoutAfter = C.logoutAfter;
        private static bool EnableRepair = C.enableRepair;
        private static bool SelfRepair = C.selfRepair;
        private static float RepairSlider = C.repairSlider;
        private static string[] Options = { "Infinite", "Run for X time" };
        private static int SelectedOption = C.runInfinite ? 0 : 1;
        private static int Hours = C.hours;
        private static int Minutes = C.minutes;
        public static void Draw()
        {
            if (Helpers.CheckboxWithTooltip("Enable Repair", ref EnableRepair, 
            "Once gear conditions are met, will automatically repair at the vendor after next Bunny Fate."))
            {
                C.enableRepair = EnableRepair;
                C.Save();
            }

            if (EnableRepair)
            {
                ImGui.Indent(20f);
                ImGui.PushItemWidth(150f);
                if (UiGui.SliderFloat("Repair Value##RepairSlider", ref RepairSlider, 0f, 100f, "%.1f"))
                {
                    if (RepairSlider >= 100)
                        C.repairSlider = 99.9f;
                    else
                        C.repairSlider = RepairSlider;
                    C.Save();
                }
                ImGui.PopItemWidth();
                ImGuiComponents.HelpMarker(UiText.T("Threshold to repair gear"));
                if (Helpers.CheckboxWithTooltip("Self Repair", ref SelfRepair, "If crafters are leveled, uses dark matter for self repair."))
                {
                    C.selfRepair = SelfRepair;
                    C.Save();
                }
                ImGui.Unindent(20f);
            }

            if (Helpers.CheckboxWithTooltip("Go To Personal/FC House After Completion", ref TeleportToHouse,
            "Will teleport to Personal or FC house after looping is complete."))
            {
                C.teleportToHouse = TeleportToHouse;
                C.Save();
            }

            ImGui.SameLine();
            FancyPluginUiString(PluginInstalled("Lifestream"), "Lifestream", IPC.Lifestream.LifestreamIPC.Repo);
            ImGui.NewLine();

            if (TeleportToHouse)
            {
                ImGui.Indent(20f);
                if (UiGui.RadioButton("Teleport To Personal", TeleportToPersonal))
                {
                    TeleportToPersonal = true;
                    TeleportToFC = false;
                    C.teleportToPersonal = TeleportToPersonal;
                    C.teleportToFC = TeleportToFC;
                    C.Save();
                }

                if (UiGui.RadioButton("Teleport to Free Company", TeleportToFC))
                {
                    TeleportToFC = true;
                    TeleportToPersonal = false;
                    C.teleportToFC = TeleportToFC;
                    C.teleportToPersonal = TeleportToPersonal;
                    C.Save();
                }
                ImGui.Unindent(20f);
            }

            if (Helpers.CheckboxWithTooltip("Enable Logout After Completion", ref LogoutAfter, 
            "Will logout after looping is complete."))
            {
                C.logoutAfter = LogoutAfter;
                C.Save();
            }

            if (UiGui.Combo("##timer settings", ref SelectedOption, Options, Options.Length))
            {
                C.runInfinite = SelectedOption == 0;
                C.Save();
            }

            ImGuiComponents.HelpMarker(UiText.T("Set to Run Infinitely or set a timer up to 24 hours"));
            if (!C.runInfinite)
            {
                ImGui.Indent(20f);
                if (UiGui.InputInt("Hours ##hours", ref Hours))
                {
                    if (Hours < 0)
                        Hours = 0;
                    else if (Hours >= 24)
                    {
                        Hours = 24;
                        Minutes = 0;
                    }
                    C.hours = Hours;
                    C.Save();
                }
                UiGui.Text("Minutes");
                if (UiGui.InputInt("##minutes", ref Minutes))
                {
                    if (Hours >= 24)
                        Minutes = 0;
                    else if (Minutes < 0)
                        Minutes = 0;
                    else if (Minutes >= 60)
                        Minutes = 60;
                    C.minutes = Minutes;

                }
                ImGui.Unindent();
            }
        }
    }
}
