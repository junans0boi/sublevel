namespace Game.Core
{
    // Per-kind facts of the twelve building kinds plus the elevator station (#244, G1-G2).
    public static class BuildingKinds
    {
        // Which of the five generated shapes builds this kind today. Every per-kind rule table (footprint, piece budgets,
        // doors, windows, materials, footsteps) is keyed by the family, so a new kind behaves like its family until T8.
        public static BuildingKind Family(BuildingKind kind)
        {
            switch (kind)
            {
                case BuildingKind.Shop: case BuildingKind.Villa: case BuildingKind.Apartment: return BuildingKind.House;
                case BuildingKind.School: case BuildingKind.Barracks: case BuildingKind.Market: return BuildingKind.Warehouse;
                case BuildingKind.Stall: case BuildingKind.WatchTower: return BuildingKind.Shed;
                default: return kind;
            }
        }

        // Storeys the generator will build (G2). Not used by the current shapes; T8 reads it.
        public static void Storeys(BuildingKind kind, out int min, out int max)
        {
            switch (kind)
            {
                case BuildingKind.House: case BuildingKind.Barracks: case BuildingKind.WatchTower: min = 1; max = 2; break;
                case BuildingKind.Shop: case BuildingKind.School: min = 2; max = 3; break;
                case BuildingKind.Villa: min = 3; max = 4; break;
                case BuildingKind.Apartment: min = 5; max = 5; break;
                default: min = 1; max = 1; break;
            }
        }

        // A stair-house exit and a parapet: shops, villas and apartments only. Every other roof is closed and flat.
        public static bool RoofAccess(BuildingKind kind) =>
            kind == BuildingKind.Shop || kind == BuildingKind.Villa || kind == BuildingKind.Apartment;
    }
}
