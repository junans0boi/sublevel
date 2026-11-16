using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Where a region stands for the screens (the pollution's Safe / Warning / Contaminated, ADR-169): safe ->
    // warning (announced) -> closed (confirmed). The old Lockdown and Bombardment stages are gone (T10).
    public enum ZoneState : byte { Safe = 0, Warning = 1, Closed = 4 }

    // The shape of the map the screens and the bots need at run time: where the regions, passes and sub-zones are. Built from
    // the generated plan (editor, tests) or from WorldInfo (the running game) and handed to the pollution director.
    public sealed class ZoneGeometry
    {
        public float Size = 1024f, SeaLevel = 10f;
        public string[] RegionId = new string[0], RegionName = new string[0];
        public float[] CentreX = new float[0], CentreZ = new float[0], CentreY = new float[0];
        public int[] PassA = new int[0], PassB = new int[0];
        public float[] PassX = new float[0], PassY = new float[0], PassZ = new float[0], PassYawDeg = new float[0], PassWidth = new float[0];
        public int[] SubRegion = new int[0];
        public float[] SubX = new float[0], SubY = new float[0], SubZ = new float[0], SubRadius = new float[0];

        public int RegionCount => RegionId.Length;
        public int PassCount => PassA.Length;
        public int SubZoneTotal => SubRegion.Length;

        // Index of the region whose centre is nearest (regions are the Voronoi cells of their centres), -1 off the map.
        public int RegionAt(float x, float z)
        {
            if (x < 0f || z < 0f || x > Size || z > Size) return -1;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < CentreX.Length; i++)
            {
                float dx = x - CentreX[i], dz = z - CentreZ[i];
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public int RegionIndexOf(string id)
        {
            for (int i = 0; i < RegionId.Length; i++) if (RegionId[i] == id) return i;
            return -1;
        }

        // Sub-zones per region, for planning.
        public int[] SubZoneCounts()
        {
            var counts = new int[RegionCount];
            for (int i = 0; i < SubRegion.Length; i++) if (SubRegion[i] >= 0 && SubRegion[i] < counts.Length) counts[SubRegion[i]]++;
            return counts;
        }

        // Global index of the local-th sub-zone of a region (in array order), or -1.
        public int GlobalSubZone(int region, int local)
        {
            int n = 0;
            for (int i = 0; i < SubRegion.Length; i++)
            {
                if (SubRegion[i] != region) continue;
                if (n == local) return i;
                n++;
            }
            return -1;
        }

        // Local index (within its region) of a global sub-zone.
        public int LocalSubZone(int global)
        {
            if (global < 0 || global >= SubRegion.Length) return -1;
            int n = 0;
            for (int i = 0; i < global; i++) if (SubRegion[i] == SubRegion[global]) n++;
            return n;
        }

        // Global index of the nearest sub-zone of the region the point is in, or -1.
        public int SubZoneAt(float x, float z)
        {
            int region = RegionAt(x, z);
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; region >= 0 && i < SubRegion.Length; i++)
            {
                if (SubRegion[i] != region) continue;
                float dx = x - SubX[i], dz = z - SubZ[i];
                float d = dx * dx + dz * dz;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public static ZoneGeometry FromPlan(WorldPlan plan)
        {
            var g = new ZoneGeometry { Size = plan.Size, SeaLevel = plan.SeaLevel };
            int n = plan.Regions.Count;
            g.RegionId = new string[n]; g.RegionName = new string[n];
            g.CentreX = new float[n]; g.CentreZ = new float[n]; g.CentreY = new float[n];
            for (int i = 0; i < n; i++)
            {
                var r = plan.Regions[i];
                g.RegionId[i] = r.Id; g.RegionName[i] = r.Name;
                g.CentreX[i] = r.CentreX; g.CentreZ[i] = r.CentreZ; g.CentreY[i] = r.HubY;
            }
            int p = plan.Passes.Count;
            g.PassA = new int[p]; g.PassB = new int[p];
            g.PassX = new float[p]; g.PassY = new float[p]; g.PassZ = new float[p]; g.PassYawDeg = new float[p]; g.PassWidth = new float[p];
            for (int i = 0; i < p; i++)
            {
                var pass = plan.Passes[i];
                g.PassA[i] = pass.RegionA; g.PassB[i] = pass.RegionB;
                g.PassX[i] = pass.X; g.PassY[i] = pass.Y; g.PassZ[i] = pass.Z; g.PassYawDeg[i] = pass.YawDeg; g.PassWidth[i] = pass.Width;
            }
            int s = plan.SubZones.Count;
            g.SubRegion = new int[s];
            g.SubX = new float[s]; g.SubY = new float[s]; g.SubZ = new float[s]; g.SubRadius = new float[s];
            for (int i = 0; i < s; i++)
            {
                var z = plan.SubZones[i];
                g.SubRegion[i] = z.Region; g.SubX[i] = z.X; g.SubY[i] = z.Y; g.SubZ[i] = z.Z; g.SubRadius[i] = z.Radius;
            }
            return g;
        }
    }
}
