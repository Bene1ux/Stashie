using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExileCore.PoEMemory.Elements;
using Stashie.Classes;
using static ExileCore.PoEMemory.MemoryObjects.ServerInventory;

namespace Stashie.Compartments;

internal class Utility
{
    public static void SaveDefaultConfigsToDisk()
    {
        WriteToNonExistentFile($"{StashieCore.Main.ConfigDirectory}\\example filter.txt",
            "https://github.com/DetectiveSquirrel/Stashie/blob/master/Example%20Filter/Example.json");
    }

    public static void WriteToNonExistentFile(string path, string content)
    {
        if (File.Exists(path))
            return;

        if (path == null)
            return;

        using var streamWriter = new StreamWriter(path, true);
        streamWriter.Write(content);
        streamWriter.Close();
    }

    public static List<string> GetStashNames(StashElement stashElement)
    {
        return stashElement?.Inventories?.Select(i => i.TabName).ToList() ?? [];
    }

    public static void SetupOrClose()
    {
        SaveDefaultConfigsToDisk();
        StashieCore.Main.SettingsTargetNodes = new List<StashTarget>(100);

        // Migrate old settings to new format
        MigrateLegacySettings();

        FilterManager.LoadCustomFilters();

        try
        {
            // Try to initialize personal stash names
            var personalStash = StashieCore.Main.PersonalStashElement;
            if (personalStash != null)
            {
                StashieCore.Main.Settings.TabToVisitWhenDone.Max =
                    (int)personalStash.TotalStashes - 1;
                var names = GetStashNames(personalStash);
                if (names != null && names.Count > 0)
                    StashTabNameCoRoutine.UpdateStashNames(names, false);
            }

            // Try to initialize guild stash names if available
            var guildStash = StashieCore.Main.GuildStashElement;
            if (guildStash != null && guildStash.IsVisibleLocal)
            {
                var guildNames = GetStashNames(guildStash);
                if (guildNames != null && guildNames.Count > 0)
                    StashTabNameCoRoutine.UpdateStashNames(guildNames, true);
            }
        }
        catch (Exception e)
        {
            StashieCore.Main.LogError($"Cant get stash names when init. {e}");
        }
    }

    private static void MigrateLegacySettings()
    {
        try
        {
            var settings = StashieCore.Main.Settings;

            // If we have old CustomFilterOptions but no new CustomFilterTargets, migrate
            if (settings.CustomFilterOptions.Count > 0 && settings.CustomFilterTargets.Count == 0)
            {
                foreach (var kvp in settings.CustomFilterOptions)
                {
                    var oldNode = kvp.Value;
                    var newTarget = new StashTarget
                    {
                        IsGuild = false, // All old settings assumed personal stash
                        Index = oldNode.Index,
                        Value = oldNode.Index == -1 ? "Ignore" : $"[Local] {oldNode.Value}"
                    };
                    settings.CustomFilterTargets[kvp.Key] = newTarget;
                }

                StashieCore.Main.LogMessage(
                    $"Migrated {settings.CustomFilterOptions.Count} filter settings to new format.", 3);
            }
        }
        catch (Exception e)
        {
            StashieCore.Main.LogError($"Error migrating legacy settings: {e}");
        }
    }

    public static bool CheckIgnoreCells(InventSlotItem inventItem, (int Width, int Height) containerSize,
        int[,] ignoredCells)
    {
        var inventPosX = inventItem.PosX;
        var inventPosY = inventItem.PosY;

        if (inventPosX < 0 || inventPosX >= containerSize.Width)
            return true;

        if (inventPosY < 0 || inventPosY >= containerSize.Height)
            return true;

        return ignoredCells[inventPosY, inventPosX] != 0; //No need to check all item size
    }
}