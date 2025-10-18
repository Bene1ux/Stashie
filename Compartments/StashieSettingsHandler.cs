using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ImGuiNET;
using Stashie.Classes;
using static Stashie.StashieCore;
using Vector2N = System.Numerics.Vector2;
using Vector4N = System.Numerics.Vector4;

namespace Stashie.Compartments;

public class StashieSettingsHandler
{
    public static void SaveIgnoredSlotsFromInventoryTemplate()
    {
        Main.Settings.IgnoredCells = new int[5, 12];
        Main.Settings.IgnoredExpandedCells = new int[5, 4];

        try
        {
            // Player Inventory
            var inventory_server =
                Main.GameController.IngameState.Data.ServerData.PlayerInventories[(int)InventorySlotE.MainInventory1];
            UpdateIgnoredCells(inventory_server, Main.Settings.IgnoredCells);
        }
        catch (Exception e)
        {
            Main.LogError($"{e}", 5);
        }
    }

    private static void UpdateIgnoredCells(InventoryHolder server_items, int[,] ignoredCells)
    {
        foreach (var item in server_items.Inventory.InventorySlotItems)
        {
            var baseC = item.Item.GetComponent<Base>();
            var itemSizeX = baseC.ItemCellsSizeX;
            var itemSizeY = baseC.ItemCellsSizeY;
            var inventPosX = item.PosX;
            var inventPosY = item.PosY;
            for (var y = 0; y < itemSizeY; y++)
            for (var x = 0; x < itemSizeX; x++)
                ignoredCells[y + inventPosY, x + inventPosX] = 1;
        }
    }

    public static void GenerateTabMenu()
    {
        // Build combined list: Ignore, [Local] tabs, [Guild] tabs
        var combinedList = new List<string> { "Ignore" };
        
        if (RenamedLocalStashNames != null)
        {
            foreach (var tabName in RenamedLocalStashNames)
                combinedList.Add($"[Local] {tabName}");
        }
        
        if (RenamedGuildStashNames != null)
        {
            foreach (var tabName in RenamedGuildStashNames)
                combinedList.Add($"[Guild] {tabName}");
        }
        
        Main.StashTabNamesByIndex = [.. combinedList];

        Main.FilterTabs = null;

        foreach (var parent in Main.currentFilter)
            Main.FilterTabs += () =>
            {
                ImGui.TextColored(new Vector4N(0f, 1f, 0.022f, 1f), parent.ParentMenuName);

                foreach (var filter in parent.Filters)
                {
                    var target = filter.Target;
                    if (target == null)
                    {
                        target = StashTarget.CreateIgnore();
                        filter.Target = target;
                    }

                    var strId = $"{filter.FilterName}##{parent.ParentMenuName + filter.FilterName}";

                    ImGui.Columns(2, strId, true);
                    ImGui.SetColumnWidth(0, 320);

                    if (ImGui.Button(strId, new Vector2N(300, 20)))
                        ImGui.OpenPopup(strId);

                    ImGui.SameLine();
                    ImGui.NextColumn();

                    // Find current selection in combined list
                    var currentIndex = Main.StashTabNamesByIndex.ToList().IndexOf(target.Value);
                    if (currentIndex < 0) currentIndex = 0; // Default to Ignore

                    var filterName = filter.FilterName;
                    if (string.IsNullOrWhiteSpace(filterName))
                        filterName = "Null";

                    if (ImGui.Combo($"##{parent.ParentMenuName + filter.FilterName}", ref currentIndex,
                            Main.StashTabNamesByIndex, Main.StashTabNamesByIndex.Length))
                    {
                        var newValue = Main.StashTabNamesByIndex[currentIndex];
                        StashTabNameCoRoutine.OnSettingsStashTargetChanged(target, newValue);
                    }

                    var specialTag = "";

                    if (filter.Shifting != null && (bool)filter.Shifting) specialTag += "Holds Shift";

                    if (filter.Affinity != null && (bool)filter.Affinity)
                        specialTag += !string.IsNullOrEmpty(specialTag) ? ", Expects Affinity" : "Expects Affinity";

                    ImGui.SameLine();
                    ImGui.Text($"{specialTag}");

                    ImGui.NextColumn();
                    ImGui.Columns(1, "", false);
                    var pop = true;

                    if (!ImGui.BeginPopupModal(strId, ref pop,
                            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
                        continue;

                    var x = 0;

                    // Show Ignore button
                    if (ImGui.Button("Ignore", new Vector2N(100, 20)))
                    {
                        StashTabNameCoRoutine.OnSettingsStashTargetChanged(target, "Ignore");
                        ImGui.CloseCurrentPopup();
                    }
                    x++;

                    // Show [Local] section
                    if (RenamedLocalStashNames != null && RenamedLocalStashNames.Count > 0)
                    {
                        if (x % 10 != 0) ImGui.SameLine();
                        ImGui.TextColored(new Vector4N(0.5f, 0.8f, 1f, 1f), "[Local]");
                        x++;

                        foreach (var name in RenamedLocalStashNames)
                        {
                            if (x % 10 != 0)
                                ImGui.SameLine();
                            x++;

                            if (ImGui.Button($"{name}##{name}_local", new Vector2N(100, 20)))
                            {
                                StashTabNameCoRoutine.OnSettingsStashTargetChanged(target, $"[Local] {name}");
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }

                    // Show [Guild] section
                    if (RenamedGuildStashNames != null && RenamedGuildStashNames.Count > 0)
                    {
                        if (x % 10 != 0) ImGui.SameLine();
                        ImGui.TextColored(new Vector4N(1f, 0.8f, 0.3f, 1f), "[Guild]");
                        x++;

                        foreach (var name in RenamedGuildStashNames)
                        {
                            if (x % 10 != 0)
                                ImGui.SameLine();
                            x++;

                            if (ImGui.Button($"{name}##{name}_guild", new Vector2N(100, 20)))
                            {
                                StashTabNameCoRoutine.OnSettingsStashTargetChanged(target, $"[Guild] {name}");
                                ImGui.CloseCurrentPopup();
                            }
                        }
                    }

                    ImGui.Spacing();
                    ImGuiNative.igIndent(350);
                    if (ImGui.Button("Close", new Vector2N(100, 20)))
                        ImGui.CloseCurrentPopup();

                    ImGui.EndPopup();
                }
            };
    }

    public static void DrawReloadConfigButton()
    {
        if (!ImGui.Button("Reload config"))
            return;

        FilterManager.LoadCustomFilters();
        GenerateTabMenu();
        DebugWindow.LogMsg("Reloaded Stashie config", 2, SharpDX.Color.LimeGreen);
    }

    public static void DrawIgnoredCellsSettings()
    {
        try
        {
            if (ImGui.Button("Copy Inventory"))
                SaveIgnoredSlotsFromInventoryTemplate();

            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(
                    $"Checked = Item will be ignored{Environment.NewLine}UnChecked = Item will be processed");
        }
        catch (Exception e)
        {
            DebugWindow.LogError(e.ToString(), 10);
        }

        ImGui.Columns(2, "", true);
        ImGui.SetColumnWidth(0, 120);

        var numb = 1;
        for (var i = 0; i < 5; i++)
        for (var j = 0; j < 4; j++)
        {
            var toggled = Convert.ToBoolean(Main.Settings.IgnoredExpandedCells[i, j]);
            if (ImGui.Checkbox($"##{numb}IgnoredBackpackInventoryCells", ref toggled))
                Main.Settings.IgnoredExpandedCells[i, j] ^= 1;

            if ((numb - 1) % 4 < 3)
                ImGui.SameLine();

            numb += 1;
        }

        ImGui.NextColumn();
        numb = 1;
        for (var i = 0; i < 5; i++)
        for (var j = 0; j < 12; j++)
        {
            var toggled = Convert.ToBoolean(Main.Settings.IgnoredCells[i, j]);
            if (ImGui.Checkbox($"##{numb}IgnoredMainInventoryCells", ref toggled))
                Main.Settings.IgnoredCells[i, j] ^= 1;

            if ((numb - 1) % 12 < 11)
                ImGui.SameLine();

            numb += 1;
        }

        // Settings to 0 breaks normal settings draws, core has 1 column for sliders?
        ImGui.Columns(1);
    }

    public static void FilePicker()
    {
        DrawReloadConfigButton();
        DrawIgnoredCellsSettings();
        if (ImGui.Button("Open Filter Folder"))
        {
            var configDir = Main.ConfigDirectory;
            var directoryToOpen = Directory.Exists(configDir);

            if (!directoryToOpen)
            {
                // Log error when the config directory doesn't exist
            }

            if (configDir != null) Process.Start("explorer.exe", configDir);
        }
    }
}