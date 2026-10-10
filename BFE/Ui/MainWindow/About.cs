using Dalamud.Bindings.ImGui;
using System;
using System.Diagnostics;

namespace BFE.Ui.MainWindow
{
    internal class About
    {
        public static void Draw() 
        {
            UiGui.TextWrapped(PluginInfo.Summary);
            ImGui.Spacing();
            UiGui.BulletText($"{PluginInfo.Command}");
            UiGui.BulletText($"{PluginInfo.Command} settings");
            UiGui.BulletText($"{PluginInfo.Command} pyros");
            UiGui.BulletText($"{PluginInfo.Command} stop");
            UiGui.BulletText($"{PluginInfo.Command} ws");
            UiGui.BulletText($"{PluginInfo.Command} j");
            UiGui.BulletText($"{PluginInfo.LegacyCommand} (legacy alias)");
            ImGui.Spacing();

            if (UiGui.SmallButton("Ko-fi"))
                Process.Start(new ProcessStartInfo { FileName = PluginInfo.SupportUrl, UseShellExecute = true });

            UiGui.SameLineIfFits("Discord");
            if (UiGui.SmallButton("Discord"))
                Process.Start(new ProcessStartInfo { FileName = PluginInfo.DiscordUrl, UseShellExecute = true });

            UiGui.SameLineIfFits("Copy Icon Guide Link");
            if (UiGui.SmallButton("Copy Icon Guide Link"))
                ImGui.SetClipboardText(PluginInfo.IconGuideUrl);

            ImGui.Spacing();
            UiGui.TextDisabled(PluginInfo.DiscordFeedbackNote);
        }
    }
}
