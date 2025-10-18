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
        TaskRunner.Run(StashTabNamesUpdater_Thread, StashTabsNameChecker);
    }

    public static void UpdateStashNames(ICollection<string> newNames, bool isGuild)
    {
        // Guard against transient empty lists (e.g., stash briefly unavailable)
        if (newNames == null || newNames.Count == 0)
        {
#if DebugMode
            Main.LogMessage($"Stashie: received empty {(isGuild ? "guild" : "local")} stash names list, skipping update.", 3);
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
                Main.LogMessage($"Stashie: fixed same {(isGuild ? "guild" : "local")} stash name to: " + realStashName, 3);
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
        foreach (var target in Main.SettingsTargetNodes)
            try
            {
                if (target.IsGuild != isGuild) continue; // Only update matching type
                
                var targetList = isGuild ? RenamedGuildStashNames : RenamedLocalStashNames;
                var inventoryIndex = targetList.IndexOf(target.Value.Replace("[Local] ", "").Replace("[Guild] ", ""));

                if (inventoryIndex == -1) //If the value doesn't exist in list (renamed)
                {
                    if (target.Index != -1) //If the value doesn't exist in list and the value was not Ignore
                    {
#if DebugMode
                        Main.LogMessage($"Tab renamed: {target.Value}", 5);
#endif
                        if (target.Index >= targetList.Count)
                        {
                            target.Index = -1;
                            target.Value = "Ignore";
                        }
                        else if (target.Index >= 0)
                        {
                            target.Value = isGuild 
                                ? $"[Guild] {targetList[target.Index]}" 
                                : $"[Local] {targetList[target.Index]}";
                        }
                    }
                }
                else //tab just changed index
                {
#if DebugMode
                    if (target.Index != inventoryIndex)
                    {
                        Main.LogMessage($"Tab moved: {target.Index} to {inventoryIndex}", 5);
                    }
#endif
                    target.Index = inventoryIndex;
                    target.Value = isGuild 
                        ? $"[Guild] {targetList[inventoryIndex]}" 
                        : $"[Local] {targetList[inventoryIndex]}";
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
        while (true)
        {
            while (!Main.GameController.Game.IngameState.InGame)
                await Task.Delay(2000);

            // Check both personal and guild stash
            var personalStash = Main.PersonalStashElement;
            var guildStash = Main.GuildStashElement;

            // Wait until at least one stash is visible
            while ((personalStash == null || !personalStash.IsVisibleLocal) &&
                   (guildStash == null || !guildStash.IsVisibleLocal))
            {
                await Task.Delay(1000);
                personalStash = Main.PersonalStashElement;
                guildStash = Main.GuildStashElement;
            }

            _counterStashTabNamesCoroutine++;

            // Update personal stash if visible
            if (personalStash != null && personalStash.IsVisibleLocal)
            {
                var cachedNames = Main.Settings.AllStashNames;
                var realNames = personalStash.AllStashNames;

                if (realNames.Count != cachedNames.Count)
                {
                    UpdateStashNames(realNames, false);
                }
                else
                {
                    for (var index = 0; index < realNames.Count; ++index)
                    {
                        if (cachedNames[index].Equals(realNames[index]))
                            continue;

                        UpdateStashNames(realNames, false);
                        break;
                    }
                }
            }

            // Update guild stash if visible
            if (guildStash != null && guildStash.IsVisibleLocal)
            {
                var cachedGuildNames = Main.Settings.AllGuildStashNames;
                var realGuildNames = guildStash.AllStashNames;

                if (realGuildNames.Count != cachedGuildNames.Count)
                {
                    UpdateStashNames(realGuildNames, true);
                }
                else
                {
                    for (var index = 0; index < realGuildNames.Count; ++index)
                    {
                        if (cachedGuildNames[index].Equals(realGuildNames[index]))
                            continue;

                        UpdateStashNames(realGuildNames, true);
                        break;
                    }
                }
            }

            await Task.Delay(1000);
        }
    }
}
