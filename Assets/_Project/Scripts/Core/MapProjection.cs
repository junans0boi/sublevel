using System;

namespace Game.Core
{
    // World <-> map picture conversion for the square island (ADR-052): the map covers 0..size on X and Z.
    // u runs east (+X) and v runs north (+Z), both 0..1, so uv (0, 0) is the south-west corner and a picture whose
    // row 0 is the bottom (a Texture2D) lines up with it directly. Pixel coordinates are continuous: pixel i spans
    // i..i+1, so a pixel centre is i + 0.5. The editor baker and the runtime map share these functions.
    public static class MapProjection
    {
        public const int PictureSize = 4096;    // baked map picture, pixels per side (0.59 m a pixel on the 2400 m island; was 2048)
        public const float GridStep = 128f;     // metres between grid lines on the full map (8 x 8 over 1 km)

        static readonly string[] Compass = { "북", "북동", "동", "남동", "남", "남서", "서", "북서" };

        // Not clamped: points off the island give u or v outside 0..1.
        public static void WorldToUv(float size, float x, float z, out float u, out float v)
        {
            u = x / size;
            v = z / size;
        }

        public static void UvToWorld(float size, float u, float v, out float x, out float z)
        {
            x = u * size;
            z = v * size;
        }

        public static void WorldToPixel(float size, int resolution, float x, float z, out float px, out float py)
        {
            px = x / size * resolution;
            py = z / size * resolution;
        }

        public static void PixelToWorld(float size, int resolution, float px, float py, out float x, out float z)
        {
            x = px / resolution * size;
            z = py / resolution * size;
        }

        public static bool Contains(float size, float x, float z) => x >= 0f && z >= 0f && x <= size && z <= size;

        // Nearest point of the map to (x, z).
        public static void Clamp(float size, ref float x, ref float z)
        {
            x = Math.Min(Math.Max(x, 0f), size);
            z = Math.Min(Math.Max(z, 0f), size);
        }

        public static float Distance(float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // Compass bearing from a to b in degrees, 0..360: 0 = +Z (north), 90 = +X (east). 0 when the points coincide.
        public static float BearingDeg(float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            if (dx == 0f && dz == 0f) return 0f;
            float deg = (float)(Math.Atan2(dx, dz) * 180.0 / Math.PI);
            if (deg < 0f) deg += 360f;
            return deg >= 360f ? 0f : deg;
        }

        // Eight-point compass name in Korean for a bearing.
        public static string CompassName(float bearingDeg)
        {
            float wrapped = (bearingDeg % 360f + 360f) % 360f;
            return Compass[(int)Math.Floor(wrapped / 45f + 0.5f) % 8];
        }

        // Grid cell of a point, clamped to the map: column 0 is the west edge, row 0 the north edge (reading order).
        public static void GridCell(float size, int cells, float x, float z, out int column, out int row)
        {
            Clamp(size, ref x, ref z);
            column = Math.Min(cells - 1, (int)(x / size * cells));
            row = Math.Min(cells - 1, (int)((size - z) / size * cells));
        }

        // "A1" is the north-west cell; letters run west to east, numbers north to south.
        public static string GridLabel(float size, int cells, float x, float z)
        {
            GridCell(size, cells, x, z, out int column, out int row);
            return ColumnLetter(column) + (row + 1);
        }

        public static string ColumnLetter(int column) => ((char)('A' + column)).ToString();

        // Where grid line i (0..cells) lies on the map, as u or v.
        public static float GridLine(int i, int cells) => (float)i / cells;
    }
}
