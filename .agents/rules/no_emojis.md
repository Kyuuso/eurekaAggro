# Rule: No Unicode Emojis in UI, Chat, or Logs

1. **NEVER use Unicode emojis** (e.g., `🔑`, `🐰`, `🧬`, `✔`, `⚠`, `💥`, `🛑`, `📦`, `⚔`, etc.) in any user-facing strings:
   - ImGui text, buttons, labels, tooltips, or table cells (`ImGui.Text`, `ImGui.Button`, etc.)
   - Chat messages (`ChatGui.Print`, `IChatGui`)
   - Toast notifications (`ToastGui`)
   - Screen overlay drawlists (`ImDrawList`)
2. **Why?**
   - The default font atlas provided by Dalamud's ImGui runtime does NOT rasterize standard emoji ranges.
   - Any emoji character renders as an ugly broken glyph (such as `=`, `?`, or empty squares), degrading the plugin's visual polish.
3. **Alternative**:
   - Use Dalamud's built-in FontAwesome icon font via `FontAwesomeIcon` (e.g., `ImGuiComponents.IconButton(FontAwesomeIcon.Key)`, `FontAwesomeIcon.ShareAlt.ToIconString()`).
   - Use clean, unambiguous plain-text tags (e.g., `[OK]`, `[WARN]`, `[Combat]`, `[Key]`).
