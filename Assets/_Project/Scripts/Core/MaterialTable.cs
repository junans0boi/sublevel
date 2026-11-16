using System;
using System.Collections.Generic;

namespace Game.Core
{
    // The twelve surface categories of ADR-174 (ADR-184 fixes the pick per category, ADR-185 the table).
    public enum MaterialCategory { Asphalt, Pavement, DirtRoad, Wall, Roof, Floor, Metal, Farmland, Vegetation, Water, Solar, School }

    // One row of materials.json: which source texture set a material uses and which surface it sounds like.
    // Change `source` to swap a pick; nothing else has to move.
    [Serializable]
    public class MaterialEntry
    {
        public string id;
        public string category;      // MaterialCategory as text
        public string vendor;        // PolyHaven | AmbientCG | Custom
        public string source;        // texture set id, or the shader name for Custom
        public string surface;       // SurfaceId as text (footstep and bullet impact sounds follow it)
        public float[] tile;         // metres one repeat covers: x, y
        public float[] tint;         // linear rgb
        public int priority;         // 0 = slice (R1 + R0), 1 = next, 2 = later
        public string status;        // temp (assumed pick) | confirmed | keep
        public bool needsDownload;   // not among the sets already fetched (docs/ASSET_LEDGER T-0001/T-0002)
    }

    [Serializable]
    public class MaterialTable
    {
        public int maxResolution = 2048;
        public MaterialEntry[] materials = Array.Empty<MaterialEntry>();
    }

    public static class MaterialRules
    {
        static readonly string[] Vendors = { "PolyHaven", "AmbientCG", "Custom" };

        // Problems with the table, empty when it is sound: duplicate ids, unknown category/surface/vendor, bad tile,
        // a category with no material, a resolution above the 2K cap (4K is banned, PERF_BUDGET).
        public static List<string> Validate(MaterialTable table)
        {
            var problems = new List<string>();
            var ids = new HashSet<string>();
            var seen = new HashSet<MaterialCategory>();
            if (table.maxResolution > 2048) problems.Add("maxResolution " + table.maxResolution + " exceeds the 2K cap");
            foreach (var m in table.materials)
            {
                if (string.IsNullOrEmpty(m.id) || !ids.Add(m.id)) problems.Add("missing or duplicate id: " + m.id);
                if (!Enum.TryParse(m.category, out MaterialCategory category)) problems.Add(m.id + ": unknown category " + m.category);
                else seen.Add(category);
                if (!Enum.TryParse(m.surface, out SurfaceId _)) problems.Add(m.id + ": unknown surface " + m.surface);
                if (Array.IndexOf(Vendors, m.vendor) < 0) problems.Add(m.id + ": unknown vendor " + m.vendor);
                if (string.IsNullOrEmpty(m.source)) problems.Add(m.id + ": no source");
                if (m.tile == null || m.tile.Length != 2 || m.tile[0] <= 0f || m.tile[1] <= 0f) problems.Add(m.id + ": tile must be two positive sizes");
                if (m.tint == null || m.tint.Length != 3) problems.Add(m.id + ": tint must be rgb");
            }
            foreach (MaterialCategory category in Enum.GetValues(typeof(MaterialCategory)))
                if (!seen.Contains(category)) problems.Add("no material for category " + category);
            return problems;
        }

        public static SurfaceId SurfaceOf(MaterialEntry m) => (SurfaceId)Enum.Parse(typeof(SurfaceId), m.surface);
    }
}
