using System;
using System.Collections.Generic;

namespace TempRepack.Engine
{
    public class Runtime
    {
        public string Name { get; set; }

    }

    public class RuntimeCollection
    {
        public Dictionary<string, Entry> Entries { get; set; } = new Dictionary<string, Entry>();

        public void Add(Comp comp)
        {
            Entries
                .GetOrAdd(
                    comp.RuntimeName,
                    () => new Entry { Runtime = new Runtime { Name = comp.RuntimeName } })
                .Comps[comp.Name] = comp;
        }

        public class Entry
        {
            public Runtime Runtime { get; set; }
            public Dictionary<string, Comp> Comps { get; set; } = new Dictionary<string, Comp>(StringComparer.OrdinalIgnoreCase);
        }
    }
}