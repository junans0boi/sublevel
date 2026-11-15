using System;
using System.Collections.Generic;

namespace Game.Core
{
    public enum BuildingKind { House, Warehouse, FactoryHall, Shed, ElevatorStation }

    // What a box is, so the builder can pick a material per surface kind and per building kind.
    public enum SurfaceKind { Floor, WallOuter, WallInner, Roof, Trim, Stairs, Prop }

    // The first six keep their numbers (saved plans, tests). Table is a dining table with chairs, Desk a desk with its chair.
    public enum PropKind
    {
        None, Crate, Shelf, Table, Counter, Bed, Machine,
        Sofa, Wardrobe, Locker, Kitchen, BathSet, Bathtub, Desk, Bookcase, TvStand, Barrel, Pallet, Workbench,
        Armchair, Plant, Dresser,
    }

    // What a room is for: it picks the furniture set (ADR-147).
    public enum RoomRole { None, Living, Kitchen, Bedroom, Bath, Office, Storage, Workshop }

    // Material of one furniture part; the builder maps these to surfaces. Lamp is emissive.
    public enum DecorMat { Wood, WoodDark, Fabric, Fabric2, Metal, Paint, Warn, Ceramic, Screen, Crate, Lamp }

    // One axis-aligned box in the building's local frame (origin at the centre of the footprint on the ground,
    // +Z = front). There are no rotated pieces: roofs are flat and stairs are stacks of boxes.
    [Serializable]
    public struct BuildingPiece
    {
        public string Name;
        public SurfaceKind Kind;
        public PropKind Prop;             // only for Kind == Prop
        public int Facing;                // Prop only: 0 = front toward +Z, 1 = +X, 2 = -Z, 3 = -X
        public float X, Y, Z;             // centre
        public float SizeX, SizeY, SizeZ;

        public float MinX => X - SizeX * 0.5f;
        public float MaxX => X + SizeX * 0.5f;
        public float MinY => Y - SizeY * 0.5f;
        public float MaxY => Y + SizeY * 0.5f;
        public float MinZ => Z - SizeZ * 0.5f;
        public float MaxZ => Z + SizeZ * 0.5f;

        public bool FootprintContains(float x, float z, float margin = 0f) =>
            Math.Abs(x - X) <= SizeX * 0.5f + margin && Math.Abs(z - Z) <= SizeZ * 0.5f + margin;

        // Distance in the ground plane from a point to this box's footprint (0 when inside).
        public float FootprintDistance(float x, float z)
        {
            float dx = Math.Max(0f, Math.Abs(x - X) - SizeX * 0.5f);
            float dz = Math.Max(0f, Math.Abs(z - Z) - SizeZ * 0.5f);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }
    }

    // A hole in a wall: a door (Y = the floor it stands on) or a window (Y = the sill).
    // X, Z is the centre of the hole on the wall's centre line. FacingDeg is the yaw of the wall's normal
    // (0 = +Z, 90 = +X): outward for exterior openings, from Room to OtherRoom for interior doors.
    [Serializable]
    public struct BuildingOpening
    {
        public string Name;
        public float X, Y, Z;
        public float Width, Height;
        public float Depth;               // thickness of the wall it goes through
        public float FacingDeg;
        public bool Exterior;
        public int Storey;
        public string Room;               // the room it serves
        public string OtherRoom;          // interior doors only

        public float ForwardX => (float)Math.Round(Math.Sin(FacingDeg * Math.PI / 180.0), 6);
        public float ForwardZ => (float)Math.Round(Math.Cos(FacingDeg * Math.PI / 180.0), 6);
    }

    // The clear rectangle of a room between its walls. A mezzanine is a room with a raised FloorY.
    [Serializable]
    public struct BuildingRoom
    {
        public string Name;
        public int Storey;
        public float MinX, MaxX, MinZ, MaxZ;
        public float FloorY, CeilingY;
        public RoomRole Role;

        public float Area => (MaxX - MinX) * (MaxZ - MinZ);
        public bool Contains(float x, float z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
    }

    // One visual box of a piece of furniture or a lamp fixture (no collider: the prop's own box is the collider).
    [Serializable]
    public struct BuildingDecor
    {
        public string Name;
        public DecorMat Mat;
        public float X, Y, Z;             // centre
        public float SizeX, SizeY, SizeZ;
    }

    // A ceiling lamp: where the light sits. The fixture itself is in Decor. Flicker = a tired fluorescent tube.
    [Serializable]
    public struct BuildingLamp
    {
        public string Name;
        public float X, Y, Z;
        public string Room;
        public float Range, Intensity;
        public bool Flicker;
    }

    // A straight flight of box steps named Name + "Step" + n. X, Z is the middle of the foot edge;
    // FacingDeg is the direction of the climb.
    [Serializable]
    public struct BuildingStair
    {
        public string Name;
        public float X, Z;
        public float BaseY, TopY;
        public float Rise, Run, Width, Length;
        public int Steps;
        public float FacingDeg;
    }

    // A named place. Loot points: Y is the top of the surface the item lies on (the floor, or a piece of furniture
    // when OnProp), so the builder adds its own pickup offset. Nav hints: door thresholds and stair ends.
    [Serializable]
    public struct BuildingPoint
    {
        public string Name;
        public float X, Y, Z;
        public string Room;
        public bool OnProp;
    }

    // Everything an editor builder needs to instantiate one enterable building.
    [Serializable]
    public class BuildingPlan
    {
        public BuildingKind Kind;
        public int Seed;
        public float Width, Depth, Height;    // bounding size actually used (the request is clamped per kind)
        public float FloorY;                  // top of the ground floor slab
        public int Storeys;
        public List<BuildingPiece> Pieces = new List<BuildingPiece>();
        public List<BuildingOpening> Doors = new List<BuildingOpening>();
        public List<BuildingOpening> Windows = new List<BuildingOpening>();
        public List<BuildingRoom> Rooms = new List<BuildingRoom>();
        public List<BuildingDecor> Decor = new List<BuildingDecor>();
        public List<BuildingLamp> Lamps = new List<BuildingLamp>();
        public List<BuildingStair> Stairs = new List<BuildingStair>();
        public List<BuildingPoint> LootPoints = new List<BuildingPoint>();
        public List<BuildingPoint> NavHints = new List<BuildingPoint>();

        // ElevatorStation only: where a player appears (feet) and the yaw of the way out (0 = +Z).
        public bool HasSpawn;
        public float SpawnX, SpawnY, SpawnZ, SpawnFacingDeg;
    }
}
