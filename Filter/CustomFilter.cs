using System.Collections.Generic;
using ItemFilterLibrary;
using Stashie.Classes;

namespace Stashie.Filter;

public class CustomFilter : BaseFilter
{
    public string ParentMenuName { get; set; }
    public List<Filter> Filters { get; set; } = [];

    public class Filter
    {
        public string FilterName { get; set; }
        public bool? Shifting { get; set; }
        public bool? Affinity { get; set; }
        public string RawQuery { get; set; }
        public ItemQuery CompiledQuery { get; set; }
        public StashTarget Target { get; set; }
        public bool AllowProcess => Target?.Index != -1;
    }
}
