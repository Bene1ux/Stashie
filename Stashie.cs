using System;
using System.Collections.Generic;
using System.Diagnostics;
using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Elements;
using ImGuiNET;
using Stashie.Classes;
using Stashie.Compartments;
using Stashie.Filter;
using Vector2N = System.Numerics.Vector2;

namespace Stashie;

public class StashieCore : BaseSettingsPlugin<StashieSettings>
{
    public const string CoroutineName = "Drop To Stash";
    public const string StashTabsNameChecker = "Stash Tabs Name Checker";
    public static StashieCore Main;

    public StashElement PersonalStashElement => GameController.Game.IngameState.IngameUi.StashElement;
    public StashElement GuildStashElement => GameController.Game.IngameState.IngameUi.GuildStashElement;
    
    // Returns the currently visible stash panel (personal or guild)
    public StashElement GetOpenStashElement()
    {
        var guildStash = GuildStashElement;
        if (guildStash != null && guildStash.IsVisibleLocal)
            return guildStash;
        return PersonalStashElement;
    }
    
    public bool IsGuildStashOpen()
    {
        var guildStash = GuildStashElement;
        return guildStash != null && guildStash.IsVisibleLocal;
    }

    public static List<string> RenamedLocalStashNames;
    public static List<string> RenamedGuildStashNames;
    public readonly Stopwatch DebugTimer = new();
    public Vector2N ClickWindowOffset;

    public List<CustomFilter> currentFilter;
    public List<FilterResult> DropItems;
    public Action FilterTabs;
    public bool IsFilterEditorTab;
    public List<StashTarget> SettingsTargetNodes;
    public string[] StashTabNamesByIndex;
    public int VisibleStashIndex = -1;

    public StashieCore()
    {
        Name = "Stashie With Linq";
    }

    public override bool Initialise()
    {
        Main = this;
        Settings.Enable.OnValueChanged += (sender, b) =>
        {
            if (b)
                StashTabNameCoRoutine.InitStashTabNameCoRoutine();
            else
                TaskRunner.Stop(StashTabsNameChecker);

            Utility.SetupOrClose();
        };

        StashieEditorHandler.FileSaveName = Settings.ConfigLastSaved;
        StashieEditorHandler.SelectedFileName = Settings.ConfigLastSaved;

        StashTabNameCoRoutine.InitStashTabNameCoRoutine();
        Utility.SetupOrClose();

        Input.RegisterKey(Settings.DropHotkey);

        Settings.DropHotkey.OnValueChanged += () => { Input.RegisterKey(Settings.DropHotkey); };
        Settings.FilterFile.OnValueSelected = _ => FilterManager.LoadCustomFilters();
        
        
        GameController.PluginBridge.SaveMethod("Stashie.StartDropItemsToStash", () => ActionCoRoutine.StartDropItemsToStashCoroutine());
        GameController.PluginBridge.SaveMethod("Stashie.StopDropItemsToStash", () => ActionCoRoutine.StopCoroutine("Stashie_DropItemsToStash"));
        GameController.PluginBridge.SaveMethod("Stashie.IsStashieActive", () => TaskRunner.Has("Stashie_DropItemsToStash"));
        GameController.PluginBridge.SaveMethod("Stashie.CellsToStash", (Func<bool, int>)CellsToStash);
        GameController.PluginBridge.SaveMethod("Stashie.CellsToStashForTab", (Func<bool, int, int>)CellsToStashForTab);
        

        return true;
    }

    public override void Render()
    {
        try
        {
            if (Settings.InspectInventoryItems)
                GameController.InspectObject(FilterManager.GetInventoryItems(), "Stashie item data");
        }
        catch
        {
            // Dont actually care what happens, if you leave it on I guess dont.
        }
    }

    public override void DrawSettings()
    {
        ImGui.BeginTabBar("TabBar");
        if (ImGui.TabItemButton("Main Settings")) IsFilterEditorTab = false;

        if (ImGui.TabItemButton("Filter Editor")) IsFilterEditorTab = true;

        ImGui.EndTabBar();

        if (IsFilterEditorTab)
        {
            StashieEditorHandler.ConverterMenu();
            StashieEditorHandler.SaveLoadMenu();
            StashieEditorHandler.DrawEditorMenu();
        }
        else
        {
            StashieSettingsHandler.FilePicker();
            base.DrawSettings();
            FilterTabs?.Invoke();
        }

        Settings.ConfigLastSaved = StashieEditorHandler.FileSaveName;
        Settings.ConfigLastSelected = StashieEditorHandler.SelectedFileName;
    }

    public override void ReceiveEvent(string eventId, object args)
    {
        if (!Settings.Enable.Value) return;

        switch (eventId)
        {
            case "switch_to_tab":
                ActionsHandler.HandleSwitchToTabEvent(args);
                break;

            case "start_stashie":
                if (TaskRunner.Has(CoroutineName)) ActionCoRoutine.StartDropItemsToStashCoroutine();

                break;
        }
    }

    public override void AreaChange(AreaInstance area)
    {
        if (area.IsHideout || area.IsTown)
            StashTabNameCoRoutine.InitStashTabNameCoRoutine();
        else
            TaskRunner.Stop(StashTabsNameChecker);
    }

    public override Job Tick()
    {
        if (!StashingRequirementsMet())
        {
            TaskRunner.Stop("Stashie_DropItemsToStash");
            return null;
        }

        if (!Settings.DropHotkey.PressedOnce())
            return null;

        if (TaskRunner.Has("Stashie_DropItemsToStash"))
            ActionCoRoutine.StopCoroutine("Stashie_DropItemsToStash");
        else
            ActionCoRoutine.StartDropItemsToStashCoroutine();
        
        return null;
    }

    public bool StashingRequirementsMet()
    {
        var openStash = GetOpenStashElement();
        return GameController.Game.IngameState.IngameUi.InventoryPanel.IsVisible &&
               openStash != null && openStash.IsVisibleLocal;
    }
    
    /// <summary>
    /// Returns the total number of cells occupied by items that would go to the specified stash type.
    /// </summary>
    /// <param name="isGuild">True for guild stash, false for personal stash</param>
    /// <returns>Total cell count</returns>
    public int CellsToStash(bool isGuild)
    {
        return CellsToStashForTab(isGuild, -1);
    }
    
    /// <summary>
    /// Returns the number of cells occupied by items going to a specific tab of the specified stash type.
    /// </summary>
    /// <param name="isGuild">True for guild stash, false for personal stash</param>
    /// <param name="tabIndex">Target tab index, or -1 for all tabs</param>
    /// <returns>Total cell count</returns>
    public int CellsToStashForTab(bool isGuild, int tabIndex)
    {
        try
        {
            var serverData = GameController.Game.IngameState.Data.ServerData;
            var invItems = serverData.PlayerInventories[0].Inventory.InventorySlotItems;
            
            if (invItems == null || currentFilter == null)
                return 0;

            var cells = 0;
            var processedItems = new HashSet<long>();

            foreach (var invItem in invItems)
            {
                if (invItem.Item == null || invItem.Address == 0)
                    continue;
                    
                // Avoid double-counting
                if (processedItems.Contains(invItem.Address))
                    continue;
                    
                processedItems.Add(invItem.Address);

                // Check if this slot is ignored
                if (Utility.CheckIgnoreCells(invItem, (12, 5), Settings.IgnoredCells))
                    continue;

                var itemData = new ItemFilterLibrary.ItemData(invItem.Item, GameController);
                
                // Find matching filter
                StashTarget matchedTarget = null;
                foreach (var filter in currentFilter)
                {
                    foreach (var subFilter in filter.Filters)
                    {
                        if (!subFilter.AllowProcess)
                            continue;

                        if (filter.CompareItem(itemData, subFilter.CompiledQuery))
                        {
                            matchedTarget = subFilter.Target;
                            break;
                        }
                    }
                    
                    if (matchedTarget != null)
                        break;
                }

                // Skip if no target or wrong stash type
                if (matchedTarget == null || matchedTarget.IsGuild != isGuild || matchedTarget.Index < 0)
                    continue;

                // Skip if filtering by tab and this doesn't match
                if (tabIndex >= 0 && matchedTarget.Index != tabIndex)
                    continue;

                // Add cell count
                var baseC = invItem.Item.GetComponent<ExileCore.PoEMemory.Components.Base>();
                if (baseC != null)
                    cells += baseC.ItemCellsSizeX * baseC.ItemCellsSizeY;
            }
            
            // Optionally subtract kept stacks (same logic as in FilterManager.ParseItems)
            // This would require more complex logic to track which items are kept
            
            return cells;
        }
        catch (Exception e)
        {
            LogError($"Error calculating cells to stash: {e}");
            return 0;
        }
    }
}
