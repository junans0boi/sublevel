namespace Game.Core
{
    // What a collider is made of, for footstep sounds and bullet impacts alike. WorldBuilder tags colliders with it,
    // so nothing parses object names (docs/research/12_surface_tag_design.md). Eighteen kinds; S5's audio.json
    // fixes the final list, so keep the numbers stable and append. 0-13 came first (#242); 14-17 complete audio.json's table
    // of sixteen (Mud, Deck, MetalStairs, TinRoof). Carpet and MetalGrate are not in that table yet.
    public enum SurfaceId : byte
    {
        Concrete = 0, Metal = 1, Wood = 2, Grass = 3, Dirt = 4, Water = 5,
        Gravel = 6, Asphalt = 7, Tile = 8, Carpet = 9, MetalGrate = 10, Glass = 11, Rock = 12, Sand = 13,
        Mud = 14, Deck = 15, MetalStairs = 16, TinRoof = 17,
    }

    // Conversions to the older per-system enums, which stay as they are (their tests and clip sets use them).
    public static class SurfaceRules
    {
        public static FootSurface ToFoot(SurfaceId id)
        {
            switch (id)
            {
                case SurfaceId.Metal: case SurfaceId.MetalGrate: case SurfaceId.MetalStairs: case SurfaceId.TinRoof: return FootSurface.Metal;
                case SurfaceId.Wood: case SurfaceId.Carpet: case SurfaceId.Deck: return FootSurface.Wood;
                case SurfaceId.Grass: return FootSurface.Grass;
                case SurfaceId.Dirt: case SurfaceId.Gravel: case SurfaceId.Sand: case SurfaceId.Mud: return FootSurface.Dirt;
                case SurfaceId.Water: return FootSurface.Water;
                default: return FootSurface.Concrete; // Concrete, Asphalt, Tile, Rock, Glass and anything unknown
            }
        }

        public static BulletSurface ToBullet(SurfaceId id)
        {
            switch (id)
            {
                case SurfaceId.Metal: case SurfaceId.MetalGrate: case SurfaceId.MetalStairs: case SurfaceId.TinRoof: return BulletSurface.Metal;
                case SurfaceId.Wood: case SurfaceId.Carpet: case SurfaceId.Deck: return BulletSurface.Wood;
                case SurfaceId.Glass: return BulletSurface.Glass;
                case SurfaceId.Grass: case SurfaceId.Dirt: case SurfaceId.Gravel: case SurfaceId.Sand: case SurfaceId.Mud: case SurfaceId.Water: return BulletSurface.Ground;
                default: return BulletSurface.Concrete;
            }
        }

        public static SurfaceId FromFoot(FootSurface foot)
        {
            switch (foot)
            {
                case FootSurface.Metal: return SurfaceId.Metal;
                case FootSurface.Wood: return SurfaceId.Wood;
                case FootSurface.Grass: return SurfaceId.Grass;
                case FootSurface.Dirt: return SurfaceId.Dirt;
                case FootSurface.Water: return SurfaceId.Water;
                default: return SurfaceId.Concrete;
            }
        }

        // The tag WorldBuilder puts on a building piece: today's floors (SoundRules.BuildingSurface). The material to
        // surface table (S2) replaces this once the new materials exist.
        public static SurfaceId ForBuilding(BuildingKind building, SurfaceKind piece) => FromFoot(SoundRules.BuildingSurface(building, piece));
    }
}
