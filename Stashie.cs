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

    public StashElement StashElement => Settings.UseGuildStash.Value
        ? GameController.Game.IngameState.IngameUi.GuildStashElement
        : GameController.Game.IngameState.IngameUi.StashElement;

    public static List<string> RenamedAllStashNames;
    public readonly Stopwatch DebugTimer = new();
    public Vector2N ClickWindowOffset;

    public List<CustomFilter> currentFilter;
    public List<FilterResult> DropItems;
    public Action FilterTabs;
    public bool IsFilterEditorTab;
    public List<ListIndexNode> SettingsListNodes;
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
        
        Settings.UseGuildStash.OnValueChanged += (sender, b) =>
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
        return GameController.Game.IngameState.IngameUi.InventoryPanel.IsVisible &&
               StashElement.IsVisibleLocal;
    }
}