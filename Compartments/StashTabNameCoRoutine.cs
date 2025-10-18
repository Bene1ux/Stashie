#define DebugMode
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.Shared;
using Stashie.Classes;
using static Stashie.StashieCore;

namespace Stashie.Compartments;

internal class StashTabNameCoRoutine
{
    public static long _counterStashTabNamesCoroutine;

    public static void InitStashTabNameCoRoutine()
    {
        // Initialize renamed lists from saved settings if they exist and aren't corrupted
        if (Main.Settings.AllStashNames != null && Main.Settings.AllStashNames.Count > 0 &&
            RenamedLocalStashNames == null)
        {
            // Only init if names look valid (not all the same/empty)
            var uniqueNames = Main.Settings.AllStashNames.Distinct().Count();
            if (uniqueNames > 1 || (uniqueNames == 1 && !string.IsNullOrWhiteSpace(Main.Settings.AllStashNames[0])))
            {
                UpdateStashNames(Main.Settings.AllStashNames, false);
            }
        }

        if (Main.Settings.AllGuildStashNames != null && Main.Settings.AllGuildStashNames.Count > 0 &&
            RenamedGuildStashNames == null)
        {
            // Only init if names look valid (not all the same/empty)
            var uniqueNames = Main.Settings.AllGuildStashNames.Distinct().Count();
            if (uniqueNames > 1 ||
                (uniqueNames == 1 && !string.IsNullOrWhiteSpace(Main.Settings.AllGuildStashNames[0])))
            {
                UpdateStashNames(Main.Settings.AllGuildStashNames, true);
            }
        }

        TaskRunner.Run(StashTabNamesUpdater_Thread, StashTabsNameChecker);
    }

    public static void UpdateStashNames(ICollection<string> newNames, bool isGuild)
    {
        // Guard against transient empty lists (e.g., stash briefly unavailable)
        if (newNames == null || newNames.Count == 0)
        {
#if DebugMode
            Main.LogMessage(
                $"Stashie: received empty {(isGuild ? "guild" : "local")} stash names list, skipping update.", 3);
#endif
            return;
        }

        var renamedList = new List<string>();
        var existingNames = new HashSet<string>();

        for (var i = 0; i < newNames.Count; i++)
        {
            var realStashName = newNames.ElementAt(i);

            if (existingNames.Contains(realStashName))
            {
                realStashName += " (" + i + ")";
#if DebugMode
                Main.LogMessage($"Stashie: fixed same {(isGuild ? "guild" : "local")} stash name to: " + realStashName,
                    3);
#endif
            }

            existingNames.Add(realStashName);
            renamedList.Add(realStashName ?? "%NULL%");
        }

        if (isGuild)
        {
            Main.Settings.AllGuildStashNames = [.. newNames];
            RenamedGuildStashNames = renamedList;
        }
        else
        {
            Main.Settings.AllStashNames = [.. newNames];
            RenamedLocalStashNames = renamedList;
        }

        // Update all existing targets to match renamed tabs
        if (Main.SettingsTargetNodes != null)
            foreach (var target in Main.SettingsTargetNodes)
                try
                {
                    if (target.IsGuild != isGuild) continue; // Only update matching type

                    var targetList = isGuild ? RenamedGuildStashNames : RenamedLocalStashNames;

                    // Trust the saved Index, just rebuild the display string
                    if (target.Index == -1)
                    {
                        target.Value = "Ignore";
                    }
                    else if (target.Index >= 0 && target.Index < targetList.Count)
                    {
                        // Valid index, update display value
                        target.Value = isGuild
                            ? $"[Guild] {targetList[target.Index]}"
                            : $"[Local] {targetList[target.Index]}";
#if DebugMode
                        Main.LogMessage($"Updated target: Index {target.Index} → {target.Value}", 5);
#endif
                    }
                    else
                    {
                        // Index out of bounds (tab was removed or not loaded yet)
#if DebugMode
                        Main.LogMessage(
                            $"Target Index {target.Index} out of bounds for {(isGuild ? "guild" : "local")} stash (count: {targetList.Count})",
                            5);
#endif
                        // Don't reset to Ignore! Keep the index, it might become valid when stash loads
                        target.Value = isGuild
                            ? $"[Guild] Tab {target.Index} (Not Loaded)"
                            : $"[Local] Tab {target.Index} (Not Loaded)";
                    }
                }
                catch (Exception e)
                {
                    DebugWindow.LogError($"UpdateStashNames SettingsTargetNodes {e}");
                }

        StashieSettingsHandler.GenerateTabMenu();
    }

    public static void OnSettingsStashTargetChanged(StashTarget target, string newValue)
    {
        // Parse newValue to determine type and index
        if (newValue == "Ignore")
        {
            target.IsGuild = false;
            target.Index = -1;
            target.Value = "Ignore";
            return;
        }

        if (newValue.StartsWith("[Guild] "))
        {
            var tabName = newValue.Substring(8);
            target.IsGuild = true;
            target.Index = RenamedGuildStashNames?.IndexOf(tabName) ?? -1;
            target.Value = newValue;
        }
        else if (newValue.StartsWith("[Local] "))
        {
            var tabName = newValue.Substring(8);
            target.IsGuild = false;
            target.Index = RenamedLocalStashNames?.IndexOf(tabName) ?? -1;
            target.Value = newValue;
        }
        else
        {
            // Fallback: assume local
            target.IsGuild = false;
            target.Index = RenamedLocalStashNames?.IndexOf(newValue) ?? -1;
            target.Value = target.Index >= 0 ? $"[Local] {newValue}" : "Ignore";
        }
    }

    public static async SyncTask<bool> StashTabNamesUpdater_Thread()
    {
        const int InitialLoadRetries = 30; // Try for 30 seconds on startup
        var personalLoadAttempts = 0;
        var guildLoadAttempts = 0;
        var personalLoaded = RenamedLocalStashNames != null && RenamedLocalStashNames.Count > 0;
        var guildLoaded = RenamedGuildStashNames != null && RenamedGuildStashNames.Count > 0;

        while (true)
        {
            while (!Main.GameController.Game.IngameState.InGame)
                await Task.Delay(2000);

            // Check both personal and guild stash
            var personalStash = Main.PersonalStashElement;
            var guildStash = Main.GuildStashElement;

            // Initial load phase: try to load stash names even if not visible
            // This allows loading from memory if available
            if (!personalLoaded && personalLoadAttempts < InitialLoadRetries)
            {
                personalLoadAttempts++;
#if DebugMode
                Main.LogMessage($"Stashie: Initial personal stash load attempt {personalLoadAttempts}/{InitialLoadRetries}", 5);
#endif
                if (personalStash != null)
                {
                    var realNames = Utility.GetStashNames(personalStash);
                    if (realNames != null && realNames.Count > 0 && !realNames.All(string.IsNullOrWhiteSpace))
                    {
                        UpdateStashNames(realNames, false);
                        personalLoaded = true;
#if DebugMode
                        Main.LogMessage($"Stashie: Successfully loaded personal stash names on attempt {personalLoadAttempts}", 3);
#endif
                    }
                }
            }

            if (!guildLoaded && guildLoadAttempts < InitialLoadRetries)
            {
                guildLoadAttempts++;
#if DebugMode
                Main.LogMessage($"Stashie: Initial guild stash load attempt {guildLoadAttempts}/{InitialLoadRetries}", 5);
#endif
                if (guildStash != null)
                {
                    var realGuildNames = Utility.GetStashNames(guildStash);
                    if (realGuildNames != null && realGuildNames.Count > 0 && !realGuildNames.All(string.IsNullOrWhiteSpace))
                    {
                        UpdateStashNames(realGuildNames, true);
                        guildLoaded = true;
#if DebugMode
                        Main.LogMessage($"Stashie: Successfully loaded guild stash names on attempt {guildLoadAttempts}", 3);
#endif
                    }
                }
            }

            // If we've tried enough times and stash data still isn't available,
            // wait until at least one stash is visible
            if ((!personalLoaded || !guildLoaded) &&
                personalLoadAttempts >= InitialLoadRetries &&
                guildLoadAttempts >= InitialLoadRetries)
            {
                while ((personalStash == null || !personalStash.IsVisibleLocal) &&
                       (guildStash == null || !guildStash.IsVisibleLocal))
                {
                    await Task.Delay(1000);
                    personalStash = Main.PersonalStashElement;
                    guildStash = Main.GuildStashElement;
                }
            }

            _counterStashTabNamesCoroutine++;

            // Update personal stash if visible or during initial load attempts
            if (personalStash != null && (personalStash.IsVisibleLocal || !personalLoaded))
            {
                var cachedNames = Main.Settings.AllStashNames;
                var realNames = Utility.GetStashNames(personalStash);

#if DebugMode
                Main.LogMessage($"Stashie: Read {realNames?.Count ?? 0} personal stash names. First 3: [{string.Join(", ", realNames?.Take(3) ?? [])}]", 5);
#endif

                // Skip if we got empty/invalid data (stash not fully loaded)
                if (realNames == null || realNames.Count == 0)
                {
                    await Task.Delay(1000);
                    continue;
                }
                
                // Skip if data looks corrupted (all empty/whitespace names)
                if (realNames.All(string.IsNullOrWhiteSpace))
                {
#if DebugMode
                    Main.LogMessage("Stashie: personal stash names all empty, waiting for data to load...", 5);
#endif
                    await Task.Delay(1000);
                    continue;
                }

                // Always update if the renamed list is null/empty (first time or after reset)
                if (RenamedLocalStashNames == null || RenamedLocalStashNames.Count == 0)
                {
                    UpdateStashNames(realNames, false);
                    personalLoaded = true;
                }
                else if (realNames.Count != cachedNames.Count)
                {
                    UpdateStashNames(realNames, false);
                    personalLoaded = true;
                }
                else
                {
                    for (var index = 0; index < realNames.Count; ++index)
                    {
                        if (cachedNames[index].Equals(realNames[index]))
                            continue;

                        UpdateStashNames(realNames, false);
                        personalLoaded = true;
                        break;
                    }
                }
            }

            // Update guild stash if visible or during initial load attempts
            if (guildStash != null && (guildStash.IsVisibleLocal || !guildLoaded))
            {
                var cachedGuildNames = Main.Settings.AllGuildStashNames;
                var realGuildNames = Utility.GetStashNames(guildStash);

#if DebugMode
                Main.LogMessage($"Stashie: Read {realGuildNames?.Count ?? 0} guild stash names. First 3: [{string.Join(", ", realGuildNames?.Take(3) ?? [])}]", 5);
#endif

                // Skip if we got empty/invalid data (stash not fully loaded)
                if (realGuildNames == null || realGuildNames.Count == 0)
                {
                    await Task.Delay(1000);
                    continue;
                }
                
                // Skip if data looks corrupted (all empty/whitespace names)
                if (realGuildNames.All(string.IsNullOrWhiteSpace))
                {
#if DebugMode
                    Main.LogMessage("Stashie: guild stash names all empty, waiting for data to load...", 5);
#endif
                    await Task.Delay(1000);
                    continue;
                }

                // Always update if the renamed list is null/empty (first time or after reset)
                if (RenamedGuildStashNames == null || RenamedGuildStashNames.Count == 0)
                {
                    UpdateStashNames(realGuildNames, true);
                    guildLoaded = true;
                }
                else if (realGuildNames.Count != cachedGuildNames.Count)
                {
                    UpdateStashNames(realGuildNames, true);
                    guildLoaded = true;
                }
                else
                {
                    for (var index = 0; index < realGuildNames.Count; ++index)
                    {
                        if (cachedGuildNames[index].Equals(realGuildNames[index]))
                            continue;

                        UpdateStashNames(realGuildNames, true);
                        guildLoaded = true;
                        break;
                    }
                }
            }

            await Task.Delay(1000);
        }
    }
}