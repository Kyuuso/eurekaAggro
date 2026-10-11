using System.Collections.Concurrent;
using System.Globalization;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Utility;
using Lumina.Excel.Sheets;

namespace EurekaSuite.Data;

/// <summary>
/// Resolves the English name of a battle NPC regardless of the client language.
/// Aggro classification, dragon detection and mutation rules all match English keywords,
/// so on German, French or Japanese clients the localized object name would never match.
/// </summary>
public static class MobNameResolver
{
    private static readonly ConcurrentDictionary<uint, string> EnglishNames = new();

    /// <summary>
    /// Returns the English display name for the character. Cached per BNpcName ID, so after the first
    /// lookup this returns the same string instance without allocating.
    /// </summary>
    public static string GetEnglishName(ICharacter character)
    {
        uint nameId = character.NameId;
        if (nameId == 0)
        {
            return character.Name.TextValue;
        }

        if (EnglishNames.TryGetValue(nameId, out var cached))
        {
            return cached;
        }

        // A failed lookup is not cached, so it is retried once the sheet is available
        var name = ResolveEnglishName(nameId, character);
        if (name == null)
        {
            return character.Name.TextValue;
        }

        EnglishNames[nameId] = name;
        return name;
    }

    private static string? ResolveEnglishName(uint nameId, ICharacter character)
    {
        // The English client already shows the properly capitalized English name
        if (EurekaSuitePlugin.ClientState.ClientLanguage == ClientLanguage.English)
        {
            return character.Name.TextValue;
        }

        try
        {
            var sheet = EurekaSuitePlugin.DataManager.GetExcelSheet<BNpcName>(ClientLanguage.English);
            if (sheet.TryGetRow(nameId, out var row))
            {
                var singular = row.Singular.ExtractText();
                if (!string.IsNullOrWhiteSpace(singular))
                {
                    // BNpcName stores lowercase singular forms ("pyros crab"); match the in-game English casing
                    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(singular);
                }
            }

            // No English row for this ID: the localized name is the best available answer
            return character.Name.TextValue;
        }
        catch
        {
            // Sheet unavailable during startup or zone transitions
        }

        return null;
    }
}
