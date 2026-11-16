using System;

namespace Game.Core
{
    // The C2 island 'Cheongnam-do' as data (ADR-168, ADR-204): six regions, 17 towns, a road graph, ridges, peaks, two rivers.
    // Polylines are flat float arrays (x0, z0, x1, z1, ...) because JsonUtility cannot read arrays of arrays. Coordinates are
    // design metres: x east, z south on the design sheet, the same axes as the world (z 0..2000 inside the 2400 m box).
    [Serializable] public class IslandRegion { public string Id, Name, Kind; public float[] Seeds; public int West, Elevators; public float RocksPerHectare, TreesPerHectare; }
    [Serializable] public class IslandTown { public string Id, Name; public int Tier, Region; public float X, Z, RotDeg, Width, Depth; public int Seed; public string Composition; }
    [Serializable] public class IslandNode { public string Id, Name; public float X, Z; }
    [Serializable] public class IslandRoad { public string A, B, Class; public float[] Via; public bool Switchback; public int EndA, EndB; }   // EndA/EndB: -1 west avenue end, +1 east, 0 the end nearer to the next waypoint
    [Serializable] public class IslandRidge { public string Name; public float[] Points; public float Amp, Width; }
    [Serializable] public class IslandPeak { public string Name; public float X, Z, H, S; }
    [Serializable] public class IslandPass { public string Id, Name; public float X, Z; public bool Road; }
    [Serializable] public class IslandQuarry { public string Name; public float X, Z, Radius; }
    [Serializable] public class IslandRiver { public string Name; public float[] Points; public float Hw0, Hw1, HwPow, B0, B1, BedPow, Sl0, Sl1; }
    [Serializable] public class IslandStation { public string Town, Side; public float Frac; public int Region; }

    [Serializable]
    public class IslandSpec
    {
        public IslandRegion[] Regions = new IslandRegion[0];
        public IslandTown[] Towns = new IslandTown[0];
        public IslandNode[] Nodes = new IslandNode[0];
        public IslandRoad[] Roads = new IslandRoad[0];
        public IslandRidge[] Ridges = new IslandRidge[0];
        public IslandPeak[] Peaks = new IslandPeak[0];
        public IslandPass[] Passes = new IslandPass[0];
        public IslandQuarry[] Quarries = new IslandQuarry[0];
        public IslandRiver[] Rivers = new IslandRiver[0];
        public IslandStation[] Stations = new IslandStation[0];
        // Mean of the island: the base relief is an ellipse around it.
        public float CentreX = 1200f, CentreZ = 1000f, RadiusX = 1150f, RadiusZ = 960f;
        public float ReliefScale = 1f;           // ridge and peak heights times this (calibrates the port of the design sheet's noise to its stated 203 m summit and walkable share)
        public float RoadDesignSlopeDeg = 10f;   // roads are solved to this grade: the 12 degree rule holds with margin after smoothing
        public float TownBlend = 60f;            // town plateau melts into the hills over this distance
        public float SwitchbackAmplitude = 58f, SwitchbackWavelength = 150f;
    }
}
