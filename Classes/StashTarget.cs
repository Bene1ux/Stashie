using ExileCore.Shared.Nodes;

namespace Stashie.Classes;

public class StashTarget : ListNode
{
    public bool IsGuild { get; set; }
    public int Index { get; set; } = -1; // -1 = Ignore
    
    public StashTarget()
    {
        IsGuild = false;
        Index = -1;
        Value = "Ignore";
    }

    public StashTarget(bool isGuild, int index, string displayValue)
    {
        IsGuild = isGuild;
        Index = index;
        Value = displayValue;
    }

    public bool IsIgnore => Index == -1;
    
    public static StashTarget CreateIgnore() => new() { IsGuild = false, Index = -1, Value = "Ignore" };
    
    public static StashTarget FromLocalIndex(int index, string tabName) => 
        new(false, index, index == -1 ? "Ignore" : $"[Local] {tabName}");
    
    public static StashTarget FromGuildIndex(int index, string tabName) => 
        new(true, index, $"[Guild] {tabName}");
}
