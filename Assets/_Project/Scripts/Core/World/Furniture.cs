using System;
using System.Collections.Generic;

namespace Game.Core
{
    // Furniture built from several small boxes (ADR-147): the prop's own box stays the collider and the walkability
    // obstacle, these parts are only what you see. Pure and deterministic. Parts are written in a local frame
    // (u across the front, v from the back to the front, y up from the floor) and mapped to the plan by facing.
    public static class Furniture
    {
        // Kinds a loot item may lie on: the top of the box is a real, flat surface.
        public static bool HoldsLoot(PropKind prop)
        {
            switch (prop)
            {
                case PropKind.Crate: case PropKind.Table: case PropKind.Counter: case PropKind.Bed: case PropKind.Desk:
                case PropKind.Workbench: case PropKind.Kitchen: case PropKind.Barrel: return true;
                default: return false;
            }
        }

        // The decor boxes of one prop in the world rect (x0..x1, z0..z1) standing at y0 with box height h, appended to
        // `into`. lootX/lootZ: where on the box the free, flat part of the top is.
        public static void Build(PropKind prop, string name, float x0, float x1, float z0, float z1, float y0, float h,
            int facing, int variant, bool industrial, List<BuildingDecor> into, out float lootX, out float lootZ)
        {
            var f = new Frame(name, x0, x1, z0, z1, y0, facing, into);
            float w = f.W, d = f.D;
            float lu = w * 0.5f, lv = d * 0.5f;
            switch (prop)
            {
                case PropKind.Table: Table(f, w, d, h); break;
                case PropKind.Counter: CounterRun(f, 0f, w, d, h, true); break;
                case PropKind.Shelf: Shelf(f, w, d, h, variant, industrial); break;
                case PropKind.Bookcase: Bookcase(f, w, d, h, variant); break;
                case PropKind.Bed: Bed(f, w, d, variant); lu = 0.52f; lv = 1.3f; break;
                case PropKind.Machine: Machine(f, w, d, h, variant); break;
                case PropKind.Sofa: Sofa(f, w, d, variant); break;
                case PropKind.Wardrobe: Wardrobe(f, w, d, h); break;
                case PropKind.Locker: Locker(f, w, d, h); break;
                case PropKind.Kitchen: Kitchen(f, w, d, h); lu = (1.4f + w) * 0.5f; break;
                case PropKind.BathSet: BathSet(f, w, d); break;
                case PropKind.Bathtub: Bathtub(f, w, d, h); break;
                case PropKind.Desk: Desk(f, w, d, h); lu = w * 0.8f; lv = 0.35f; break;
                case PropKind.TvStand: TvStand(f, w, d, h); break;
                case PropKind.Barrel: Barrel(f, w, d, h, variant); break;
                case PropKind.Pallet: Pallet(f, w, d, variant); break;
                case PropKind.Workbench: Workbench(f, w, d, h); break;
                default: Crate(f, w, d, h); break;
            }
            f.Map(lu, lv, out lootX, out lootZ);
        }

        // A thin rug on the floor of a room: decor only, nothing stands on it and nothing collides with it.
        public static void Rug(string name, float x0, float x1, float z0, float z1, float y0, DecorMat mat, List<BuildingDecor> into) =>
            Add(into, name, mat, x0, x1, y0 + 0.004f, y0 + 0.016f, z0, z1);

        public static void Add(List<BuildingDecor> into, string name, DecorMat mat, float x0, float x1, float y0, float y1, float z0, float z1) =>
            into.Add(new BuildingDecor
            {
                Name = name, Mat = mat, X = (x0 + x1) * 0.5f, Y = (y0 + y1) * 0.5f, Z = (z0 + z1) * 0.5f,
                SizeX = x1 - x0, SizeY = y1 - y0, SizeZ = z1 - z0,
            });

        // A ceiling lamp: a glowing shade (house) or a tube (hall) on a canopy and rods, hung down from `ceiling`.
        public static void LampFixture(string name, float x, float z, float ceiling, float hang, bool tube, bool alongX, List<BuildingDecor> into)
        {
            float bottom = ceiling - hang;
            if (hang > 0.15f)
            {
                float r = tube ? 0.5f : 0f;
                if (tube)
                {
                    Add(into, name + "Rod0", DecorMat.Metal, x - (alongX ? r : 0.05f) - 0.01f, x - (alongX ? r : 0.05f) + 0.01f, bottom, ceiling, z - (alongX ? 0.05f : r) - 0.01f, z - (alongX ? 0.05f : r) + 0.01f);
                    Add(into, name + "Rod1", DecorMat.Metal, x + (alongX ? r : 0.05f) - 0.01f, x + (alongX ? r : 0.05f) + 0.01f, bottom, ceiling, z + (alongX ? 0.05f : r) - 0.01f, z + (alongX ? 0.05f : r) + 0.01f);
                }
                else Add(into, name + "Rod", DecorMat.Metal, x - 0.012f, x + 0.012f, bottom, ceiling, z - 0.012f, z + 0.012f);
            }
            if (tube)
            {
                float lx = alongX ? 0.7f : 0.09f, lz = alongX ? 0.09f : 0.7f;
                Add(into, name + "Housing", DecorMat.Metal, x - lx - 0.02f, x + lx + 0.02f, bottom, bottom + 0.05f, z - lz - 0.02f, z + lz + 0.02f);
                Add(into, name + "Tube", DecorMat.Lamp, x - lx, x + lx, bottom - 0.03f, bottom, z - lz, z + lz);
            }
            else
            {
                Add(into, name + "Canopy", DecorMat.Metal, x - 0.07f, x + 0.07f, bottom + 0.08f, bottom + 0.1f, z - 0.07f, z + 0.07f);
                Add(into, name + "Shade", DecorMat.Lamp, x - 0.22f, x + 0.22f, bottom, bottom + 0.1f, z - 0.22f, z + 0.22f);
            }
        }

        // ---- the local frame ----

        sealed class Frame
        {
            readonly string _name;
            readonly float _x0, _x1, _z0, _z1, _y0;
            readonly int _facing;
            readonly List<BuildingDecor> _into;
            int _n;
            public readonly float W, D;

            public Frame(string name, float x0, float x1, float z0, float z1, float y0, int facing, List<BuildingDecor> into)
            {
                _name = name; _x0 = x0; _x1 = x1; _z0 = z0; _z1 = z1; _y0 = y0; _facing = ((facing % 4) + 4) % 4; _into = into;
                bool alongX = (_facing & 1) == 0;
                W = alongX ? x1 - x0 : z1 - z0;
                D = alongX ? z1 - z0 : x1 - x0;
            }

            public void Map(float u, float v, out float x, out float z)
            {
                switch (_facing)
                {
                    case 0: x = _x0 + u; z = _z0 + v; break;
                    case 2: x = _x1 - u; z = _z1 - v; break;
                    case 1: x = _x0 + v; z = _z0 + u; break;
                    default: x = _x1 - v; z = _z1 - u; break;
                }
            }

            // A box from (u0, v0) to (u1, v1) and y0..y1 above the floor of the prop.
            public void B(DecorMat mat, float u0, float u1, float v0, float v1, float y0, float y1)
            {
                if (u1 - u0 < 0.004f || v1 - v0 < 0.004f || y1 - y0 < 0.004f) return;
                Map(u0, v0, out float xa, out float za);
                Map(u1, v1, out float xb, out float zb);
                Add(_into, _name + "." + _n++, mat, Math.Min(xa, xb), Math.Max(xa, xb), _y0 + y0, _y0 + y1, Math.Min(za, zb), Math.Max(za, zb));
            }

            // Four legs at the corners of a rectangle.
            public void Legs(DecorMat mat, float u0, float u1, float v0, float v1, float size, float y1)
            {
                B(mat, u0, u0 + size, v0, v0 + size, 0f, y1);
                B(mat, u1 - size, u1, v0, v0 + size, 0f, y1);
                B(mat, u0, u0 + size, v1 - size, v1, 0f, y1);
                B(mat, u1 - size, u1, v1 - size, v1, 0f, y1);
            }
        }

        // A hash for small variations that is the same on every machine.
        static float Hash(int a, int b)
        {
            unchecked
            {
                uint x = (uint)a * 2654435761u + (uint)b * 40503u + 0x9E3779B9u;
                x ^= x >> 15; x *= 0x85EBCA6Bu; x ^= x >> 13;
                return (x & 0xFFFFu) / 65536f;
            }
        }

        // ---- pieces ----

        // seat at v from vBack, dir = +1 when it faces +v (the back board stands at vBack).
        static void Chair(Frame f, float uc, float vBack, int dir)
        {
            float a = Math.Min(vBack, vBack + dir * 0.44f), b = Math.Max(vBack, vBack + dir * 0.44f);
            f.B(DecorMat.Wood, uc - 0.22f, uc + 0.22f, a, b, 0.42f, 0.46f);
            f.Legs(DecorMat.Wood, uc - 0.22f, uc + 0.22f, a, b, 0.04f, 0.42f);
            float ba = dir > 0 ? vBack : vBack - 0.04f, bb = dir > 0 ? vBack + 0.04f : vBack;
            f.B(DecorMat.Wood, uc - 0.22f, uc + 0.22f, ba, bb, 0.46f, 0.86f);
            f.B(DecorMat.WoodDark, uc - 0.18f, uc + 0.18f, ba, bb, 0.62f, 0.7f);
        }

        static void Table(Frame f, float w, float d, float h)
        {
            float v0 = 0.3f, v1 = d - 0.3f;
            f.B(DecorMat.WoodDark, 0f, w, v0, v1, h - 0.05f, h);
            f.B(DecorMat.Wood, 0.06f, w - 0.06f, v0 + 0.04f, v1 - 0.04f, h - 0.12f, h - 0.05f);
            f.Legs(DecorMat.Wood, 0.04f, w - 0.04f, v0 + 0.04f, v1 - 0.04f, 0.07f, h - 0.05f);
            Chair(f, w * 0.5f, 0f, 1);
            Chair(f, w * 0.5f, d, -1);
        }

        // A run of base cabinets from u0 to u1 with doors, handles and a worktop.
        static void CounterRun(Frame f, float u0, float u1, float d, float h, bool wood)
        {
            f.B(DecorMat.Metal, u0 + 0.02f, u1 - 0.02f, 0.04f, d - 0.04f, 0f, 0.1f);
            f.B(wood ? DecorMat.Wood : DecorMat.Paint, u0, u1, 0f, d - 0.03f, 0.1f, h - 0.05f);
            int n = Math.Max(1, (int)Math.Round((u1 - u0) / 0.6f));
            float step = (u1 - u0) / n;
            for (int i = 0; i < n; i++)
            {
                float a = u0 + i * step + 0.015f, b = u0 + (i + 1) * step - 0.015f;
                f.B(DecorMat.WoodDark, a, b, d - 0.03f, d - 0.01f, 0.14f, h - 0.08f);
                f.B(DecorMat.Metal, (a + b) * 0.5f - 0.07f, (a + b) * 0.5f + 0.07f, d - 0.01f, d, h - 0.2f, h - 0.17f);
            }
            f.B(DecorMat.Ceramic, u0, u1, 0f, d, h - 0.05f, h);
        }

        static void Kitchen(Frame f, float w, float d, float h)
        {
            // fridge, stove, then base and wall cabinets with a sink
            f.B(DecorMat.Ceramic, 0f, 0.7f, 0f, d, 0.04f, 1.78f);
            f.B(DecorMat.Metal, 0.02f, 0.68f, 0.03f, d - 0.03f, 0f, 0.04f);
            f.B(DecorMat.Screen, 0.01f, 0.69f, d - 0.01f, d, 1.14f, 1.16f);
            f.B(DecorMat.Metal, 0.56f, 0.6f, d - 0.03f, d, 1.2f, 1.6f);
            f.B(DecorMat.Metal, 0.56f, 0.6f, d - 0.03f, d, 0.7f, 1.05f);
            f.B(DecorMat.Ceramic, 0.7f, 1.4f, 0f, d, 0f, h - 0.02f);
            f.B(DecorMat.Screen, 0.7f, 1.4f, 0f, d, h - 0.02f, h);
            for (int i = 0; i < 4; i++)
                f.B(DecorMat.Metal, 0.8f + (i % 2) * 0.3f, 0.92f + (i % 2) * 0.3f, 0.1f + (i / 2) * 0.27f, 0.22f + (i / 2) * 0.27f, h, h + 0.015f);
            f.B(DecorMat.Screen, 0.8f, 1.3f, d - 0.012f, d, 0.2f, 0.6f);
            f.B(DecorMat.Metal, 0.78f, 1.32f, d - 0.03f, d - 0.012f, 0.65f, 0.67f);
            for (int i = 0; i < 3; i++) f.B(DecorMat.Metal, 0.85f + i * 0.2f, 0.9f + i * 0.2f, d - 0.02f, d, h - 0.1f, h - 0.04f);
            CounterRun(f, 1.4f, w, d, h, true);
            float sink = Math.Min(w - 0.5f, 1.4f + (w - 1.4f) * 0.5f - 0.25f);
            f.B(DecorMat.Metal, sink, sink + 0.5f, 0.05f, d - 0.07f, h, h + 0.012f);
            f.B(DecorMat.Screen, sink + 0.04f, sink + 0.46f, 0.09f, d - 0.11f, h + 0.012f, h + 0.016f);
            f.B(DecorMat.Metal, sink + 0.23f, sink + 0.27f, 0.02f, 0.06f, h, h + 0.3f);
            f.B(DecorMat.Metal, sink + 0.23f, sink + 0.27f, 0.02f, 0.2f, h + 0.26f, h + 0.3f);
            f.B(DecorMat.Wood, 1.4f, w, 0f, 0.34f, 1.5f, 2.15f);
            int doors = Math.Max(1, (int)Math.Round((w - 1.4f) / 0.5f));
            float ds = (w - 1.4f) / doors;
            for (int i = 0; i < doors; i++)
                f.B(DecorMat.WoodDark, 1.4f + i * ds + 0.015f, 1.4f + (i + 1) * ds - 0.015f, 0.34f, 0.36f, 1.53f, 2.12f);
        }

        static void Shelf(Frame f, float w, float d, float h, int variant, bool industrial)
        {
            int bays = Math.Max(1, (int)Math.Ceiling(w / 1.5f));
            float bay = w / bays;
            float post = industrial ? 0.06f : 0.04f;
            var upright = industrial ? DecorMat.Paint : DecorMat.Wood;
            var deck = industrial ? DecorMat.Warn : DecorMat.WoodDark;
            for (int i = 0; i <= bays; i++)
            {
                float u = Math.Min(w - post, Math.Max(0f, i * bay - post / 2));
                f.B(upright, u, u + post, 0f, post, 0f, h);
                f.B(upright, u, u + post, d - post, d, 0f, h);
            }
            int levels = industrial ? 4 : 5;
            for (int l = 0; l < levels; l++)
            {
                float y = l == 0 ? 0.08f : 0.08f + (h - 0.2f) * l / (levels - 1);
                if (l == levels - 1) y = h - 0.05f;
                f.B(deck, 0f, w, industrial ? 0.0f : 0f, d, y, y + 0.04f);
                if (industrial) f.B(DecorMat.Metal, 0f, w, 0.02f, d - 0.02f, y - 0.01f, y);
                if (l == levels - 1) continue;
                float u = 0.06f;
                int n = 0;
                float room = (h - 0.2f) / (levels - 1) - 0.12f;
                while (u < w - 0.3f)
                {
                    float r = Hash(variant * 31 + l, n++);
                    float cw = 0.25f + r * 0.45f, ch = Math.Min(room, 0.18f + Hash(variant, l * 17 + n) * 0.4f);
                    if (r < 0.2f) { u += 0.25f; continue; }
                    float cd = Math.Min(d - 0.1f, 0.25f + r * 0.5f);
                    f.B(DecorMat.Crate, u, Math.Min(w - 0.05f, u + cw), 0.05f, 0.05f + cd, y + 0.04f, y + 0.04f + ch);
                    u += cw + 0.05f;
                }
            }
        }

        static void Bookcase(Frame f, float w, float d, float h, int variant)
        {
            f.B(DecorMat.Wood, 0f, 0.03f, 0f, d, 0f, h);
            f.B(DecorMat.Wood, w - 0.03f, w, 0f, d, 0f, h);
            f.B(DecorMat.WoodDark, 0.03f, w - 0.03f, 0f, 0.015f, 0f, h);
            var spines = new[] { DecorMat.Fabric, DecorMat.Fabric2, DecorMat.Warn, DecorMat.Paint, DecorMat.Ceramic };
            float[] ys = { 0.04f, 0.5f, 0.95f, 1.4f, h - 0.03f };
            for (int l = 0; l < ys.Length; l++)
            {
                f.B(DecorMat.Wood, 0.03f, w - 0.03f, 0.015f, d, ys[l], ys[l] + 0.03f);
                if (l == ys.Length - 1) continue;
                float u = 0.06f;
                for (int n = 0; u < w - 0.12f; n++)
                {
                    float r = Hash(variant * 7 + l, n);
                    float bw = 0.025f + r * 0.035f, bh = Math.Min(0.36f, 0.2f + Hash(variant, l * 13 + n) * 0.17f);
                    if (r > 0.9f) { u += 0.18f; continue; }
                    f.B(spines[(int)(Hash(n, variant + l) * spines.Length) % spines.Length], u, Math.Min(w - 0.06f, u + bw), 0.04f, d - 0.04f, ys[l] + 0.03f, ys[l] + 0.03f + bh);
                    u += bw + 0.004f;
                }
            }
        }

        static void Bed(Frame f, float w, float d, int variant)
        {
            const float bw = 1.05f;
            var sheet = variant % 2 == 0 ? DecorMat.Fabric : DecorMat.Fabric2;
            f.Legs(DecorMat.Wood, 0f, bw, 0f, d, 0.07f, 0.12f);
            f.B(DecorMat.Wood, 0f, bw, 0f, d, 0.12f, 0.28f);
            f.B(sheet == DecorMat.Fabric ? DecorMat.Fabric2 : DecorMat.Fabric, 0.03f, bw - 0.03f, 0.05f, d - 0.03f, 0.28f, 0.43f);
            f.B(sheet, 0.02f, bw - 0.02f, 0.65f, d - 0.02f, 0.43f, 0.465f);
            f.B(DecorMat.Ceramic, 0.1f, 0.5f, 0.1f, 0.5f, 0.43f, 0.5f);
            f.B(DecorMat.Ceramic, 0.55f, 0.95f, 0.1f, 0.5f, 0.43f, 0.5f);
            f.B(DecorMat.WoodDark, 0f, bw, 0f, 0.06f, 0.12f, 0.55f);
            f.B(DecorMat.WoodDark, 0f, bw, d - 0.05f, d, 0.12f, 0.36f);
            // bedside table
            float u0 = bw + 0.05f;
            f.B(DecorMat.Wood, u0, w, 0.02f, 0.45f, 0.04f, 0.5f);
            f.B(DecorMat.WoodDark, u0 + 0.02f, w - 0.02f, 0.45f - 0.01f, 0.45f, 0.1f, 0.28f);
            f.B(DecorMat.Metal, u0 + 0.13f, w - 0.13f, 0.44f, 0.45f, 0.27f, 0.29f);
            f.B(DecorMat.Metal, u0 + 0.02f, w - 0.02f, 0.04f, 0.41f, 0f, 0.04f);
        }

        static void Sofa(Frame f, float w, float d, int variant)
        {
            var cloth = variant % 2 == 0 ? DecorMat.Fabric2 : DecorMat.Fabric;
            f.Legs(DecorMat.WoodDark, 0.02f, w - 0.02f, 0.02f, d - 0.02f, 0.06f, 0.1f);
            f.B(cloth, 0f, w, 0f, d, 0.1f, 0.4f);
            int seats = w > 1.7f ? 3 : 2;
            float seat = (w - 0.36f) / seats;
            for (int i = 0; i < seats; i++)
            {
                f.B(cloth, 0.18f + i * seat + 0.01f, 0.18f + (i + 1) * seat - 0.01f, 0.25f, d - 0.01f, 0.4f, 0.5f);
                f.B(cloth, 0.18f + i * seat + 0.01f, 0.18f + (i + 1) * seat - 0.01f, 0.1f, 0.28f, 0.5f, 0.82f);
            }
            f.B(cloth, 0f, 0.18f, 0f, d, 0.4f, 0.62f);
            f.B(cloth, w - 0.18f, w, 0f, d, 0.4f, 0.62f);
            f.B(cloth, 0f, w, 0f, 0.1f, 0.4f, 0.85f);
        }

        static void Wardrobe(Frame f, float w, float d, float h)
        {
            f.B(DecorMat.WoodDark, 0.02f, w - 0.02f, 0.02f, d - 0.04f, 0f, 0.07f);
            f.B(DecorMat.Wood, 0f, w, 0f, d - 0.03f, 0.07f, h);
            float half = w / 2;
            f.B(DecorMat.WoodDark, 0.02f, half - 0.01f, d - 0.03f, d - 0.01f, 0.1f, h - 0.06f);
            f.B(DecorMat.WoodDark, half + 0.01f, w - 0.02f, d - 0.03f, d - 0.01f, 0.1f, h - 0.06f);
            f.B(DecorMat.Metal, half - 0.07f, half - 0.04f, d - 0.01f, d, 0.95f, 1.2f);
            f.B(DecorMat.Metal, half + 0.04f, half + 0.07f, d - 0.01f, d, 0.95f, 1.2f);
            f.B(DecorMat.Wood, 0f, w, 0f, d, h - 0.05f, h);
        }

        static void Locker(Frame f, float w, float d, float h)
        {
            int n = Math.Max(2, (int)Math.Round(w / 0.4f));
            float uw = w / n;
            f.B(DecorMat.Metal, 0.02f, w - 0.02f, 0.02f, d - 0.04f, 0f, 0.1f);
            f.B(DecorMat.Metal, 0f, w, 0f, d - 0.025f, 0.1f, h);
            for (int i = 0; i < n; i++)
            {
                float a = i * uw + 0.012f, b = (i + 1) * uw - 0.012f;
                f.B(DecorMat.Paint, a, b, d - 0.025f, d - 0.005f, 0.12f, h - 0.04f);
                for (int s = 0; s < 3; s++)
                    f.B(DecorMat.Screen, a + 0.07f, b - 0.07f, d - 0.005f, d, h - 0.3f + s * 0.06f, h - 0.27f + s * 0.06f);
                f.B(DecorMat.Metal, b - 0.07f, b - 0.045f, d - 0.005f, d, 0.95f, 1.12f);
            }
            f.B(DecorMat.Metal, 0f, w, 0f, d, h - 0.03f, h);
        }

        static void BathSet(Frame f, float w, float d)
        {
            // toilet: pedestal, bowl, seat and tank against the wall
            f.B(DecorMat.Ceramic, 0.26f, 0.54f, 0.3f, 0.58f, 0f, 0.25f);
            f.B(DecorMat.Ceramic, 0.2f, 0.6f, 0.18f, d - 0.05f, 0.25f, 0.4f);
            f.B(DecorMat.Wood, 0.21f, 0.59f, 0.2f, d - 0.07f, 0.4f, 0.43f);
            f.B(DecorMat.Ceramic, 0.2f, 0.6f, 0f, 0.18f, 0.35f, 0.82f);
            f.B(DecorMat.Metal, 0.38f, 0.42f, 0.08f, 0.12f, 0.82f, 0.85f);
            // vanity and basin
            float u0 = Math.Max(1.0f, w - 0.65f);
            f.B(DecorMat.Wood, u0, w - 0.05f, 0f, 0.5f, 0.05f, 0.76f);
            f.B(DecorMat.WoodDark, u0 + 0.03f, w - 0.08f, 0.49f, 0.5f, 0.12f, 0.7f);
            f.B(DecorMat.Ceramic, u0 - 0.02f, w - 0.03f, 0f, 0.53f, 0.76f, 0.8f);
            f.B(DecorMat.Screen, u0 + 0.06f, w - 0.11f, 0.08f, 0.45f, 0.8f, 0.803f);
            f.B(DecorMat.Metal, u0 + 0.22f, u0 + 0.26f, 0.03f, 0.07f, 0.8f, 0.98f);
            f.B(DecorMat.Metal, u0 + 0.22f, u0 + 0.26f, 0.03f, 0.2f, 0.94f, 0.98f);
            f.B(DecorMat.Metal, u0 + 0.03f, w - 0.1f, 0f, 0.025f, 1.3f, 1.9f);
            f.B(DecorMat.Screen, u0 + 0.05f, w - 0.12f, 0.025f, 0.04f, 1.33f, 1.87f);
        }

        static void Bathtub(Frame f, float w, float d, float h)
        {
            const float t = 0.07f;
            f.Legs(DecorMat.Metal, 0.05f, w - 0.05f, 0.05f, d - 0.05f, 0.06f, 0.1f);
            f.B(DecorMat.Ceramic, 0f, w, 0f, d, 0.1f, 0.2f);
            f.B(DecorMat.Ceramic, 0f, t, 0f, d, 0.2f, h);
            f.B(DecorMat.Ceramic, w - t, w, 0f, d, 0.2f, h);
            f.B(DecorMat.Ceramic, t, w - t, 0f, t, 0.2f, h);
            f.B(DecorMat.Ceramic, t, w - t, d - t, d, 0.2f, h);
            f.B(DecorMat.Screen, t, w - t, t, d - t, 0.2f, 0.205f);
            f.B(DecorMat.Metal, 0.12f, 0.16f, 0.1f, 0.14f, h, h + 0.22f);
            f.B(DecorMat.Metal, 0.12f, 0.3f, 0.1f, 0.14f, h + 0.18f, h + 0.22f);
        }

        static void Desk(Frame f, float w, float d, float h)
        {
            const float dd = 0.7f;
            f.B(DecorMat.WoodDark, 0f, w, 0f, dd, h - 0.04f, h);
            f.B(DecorMat.Wood, 0.02f, 0.48f, 0.02f, dd - 0.03f, 0f, h - 0.04f);
            for (int i = 0; i < 3; i++)
            {
                f.B(DecorMat.WoodDark, 0.04f, 0.46f, dd - 0.03f, dd - 0.01f, 0.05f + i * 0.2f, 0.23f + i * 0.2f);
                f.B(DecorMat.Metal, 0.2f, 0.3f, dd - 0.01f, dd, 0.12f + i * 0.2f, 0.15f + i * 0.2f);
            }
            f.B(DecorMat.Wood, w - 0.05f, w - 0.02f, 0.02f, dd - 0.03f, 0f, h - 0.04f);
            f.B(DecorMat.Wood, 0.48f, w - 0.02f, 0.02f, 0.05f, 0.3f, h - 0.04f);
            // monitor and keyboard
            f.B(DecorMat.Metal, 0.62f, 0.78f, 0.15f, 0.31f, h, h + 0.02f);
            f.B(DecorMat.Metal, 0.68f, 0.72f, 0.2f, 0.24f, h + 0.02f, h + 0.14f);
            f.B(DecorMat.Metal, 0.4f, 1.0f, 0.17f, 0.23f, h + 0.14f, h + 0.5f);
            f.B(DecorMat.Screen, 0.42f, 0.98f, 0.23f, 0.235f, h + 0.16f, h + 0.48f);
            f.B(DecorMat.Metal, 0.48f, 0.92f, 0.38f, 0.52f, h, h + 0.02f);
            Chair(f, w * 0.5f, d, -1);
        }

        static void TvStand(Frame f, float w, float d, float h)
        {
            f.Legs(DecorMat.WoodDark, 0.03f, w - 0.03f, 0.03f, d - 0.03f, 0.05f, 0.07f);
            f.B(DecorMat.Wood, 0f, w, 0f, d - 0.02f, 0.07f, h);
            f.B(DecorMat.WoodDark, 0.02f, w / 2 - 0.01f, d - 0.02f, d, 0.1f, h - 0.04f);
            f.B(DecorMat.WoodDark, w / 2 + 0.01f, w - 0.02f, d - 0.02f, d, 0.1f, h - 0.04f);
            f.B(DecorMat.Metal, w / 2 - 0.12f, w / 2 + 0.12f, d * 0.4f, d * 0.65f, h, h + 0.04f);
            f.B(DecorMat.Metal, w / 2 - 0.02f, w / 2 + 0.02f, d * 0.5f, d * 0.55f, h + 0.04f, h + 0.12f);
            f.B(DecorMat.Metal, 0.12f, w - 0.12f, d * 0.45f, d * 0.55f, h + 0.1f, h + 0.65f);
            f.B(DecorMat.Screen, 0.14f, w - 0.14f, d * 0.55f, d * 0.55f + 0.012f, h + 0.12f, h + 0.63f);
        }

        // Eight-sided drum from two crossed boxes, with two bands.
        static void Barrel(Frame f, float w, float d, float h, int variant)
        {
            var body = variant % 3 == 0 ? DecorMat.Warn : variant % 3 == 1 ? DecorMat.Paint : DecorMat.Metal;
            float c = 0.17f * Math.Min(w, d);
            f.B(body, 0.012f, w - 0.012f, c, d - c, 0.02f, h - 0.02f);
            f.B(body, c, w - c, 0.012f, d - 0.012f, 0.02f, h - 0.02f);
            foreach (float y in new[] { 0.06f, h * 0.5f - 0.02f, h - 0.1f })
            {
                f.B(DecorMat.Metal, 0f, w, c + 0.012f, d - c - 0.012f, y, y + 0.04f);
                f.B(DecorMat.Metal, c + 0.012f, w - c - 0.012f, 0f, d, y, y + 0.04f);
            }
            f.B(DecorMat.Metal, c + 0.02f, w - c - 0.02f, c + 0.02f, d - c - 0.02f, h - 0.02f, h);
        }

        static void Pallet(Frame f, float w, float d, int variant)
        {
            for (int i = 0; i < 3; i++) f.B(DecorMat.Wood, 0f, w, i * (d - 0.1f) / 2, i * (d - 0.1f) / 2 + 0.1f, 0f, 0.09f);
            for (int i = 0; i < 5; i++) f.B(DecorMat.Wood, i * (w - 0.17f) / 4, i * (w - 0.17f) / 4 + 0.17f, 0f, d, 0.09f, 0.13f);
            for (int ix = 0; ix < 2; ix++)
                for (int iz = 0; iz < 2; iz++)
                {
                    float r = Hash(variant, ix * 2 + iz);
                    float cu0 = 0.04f + ix * (w - 0.08f) / 2 + 0.01f, cv0 = 0.04f + iz * (d - 0.08f) / 2 + 0.01f;
                    float cu1 = 0.04f + (ix + 1) * (w - 0.08f) / 2 - 0.01f, cv1 = 0.04f + (iz + 1) * (d - 0.08f) / 2 - 0.01f;
                    float top = 0.13f + 0.38f + r * 0.2f;
                    f.B(DecorMat.Crate, cu0, cu1, cv0, cv1, 0.13f, top);
                    f.B(DecorMat.Fabric2, cu0 + 0.01f, cu1 - 0.01f, cv0 + 0.01f, cv1 - 0.01f, top - 0.04f, top);
                    if (r > 0.45f) f.B(DecorMat.Crate, cu0 + 0.03f, cu1 - 0.03f, cv0 + 0.03f, cv1 - 0.03f, top, top + 0.3f + r * 0.2f);
                }
        }

        static void Workbench(Frame f, float w, float d, float h)
        {
            f.B(DecorMat.Wood, 0f, w, 0f, d, h - 0.06f, h);
            f.Legs(DecorMat.Metal, 0.03f, w - 0.03f, 0.03f, d - 0.03f, 0.07f, h - 0.06f);
            f.B(DecorMat.WoodDark, 0.1f, w - 0.1f, 0.1f, d - 0.1f, 0.25f, 0.29f);
            f.B(DecorMat.Wood, 0.03f, w - 0.03f, 0f, 0.03f, h - 0.2f, h - 0.06f);
            f.B(DecorMat.Wood, 0.03f, w - 0.03f, d - 0.03f, d, h - 0.2f, h - 0.06f);
            f.B(DecorMat.Crate, 0.2f, 0.6f, 0.12f, 0.5f, 0.29f, 0.5f);
            f.B(DecorMat.Metal, 0.12f, 0.3f, d - 0.2f, d - 0.03f, h, h + 0.1f);
            f.B(DecorMat.Metal, 0.12f, 0.2f, d - 0.12f, d - 0.03f, h + 0.1f, h + 0.18f);
            f.B(DecorMat.Warn, w - 0.55f, w - 0.1f, 0.12f, 0.34f, h, h + 0.14f);
            f.B(DecorMat.Metal, w - 0.45f, w - 0.2f, 0.16f, 0.3f, h + 0.14f, h + 0.17f);
        }

        static void Machine(Frame f, float w, float d, float h, int variant)
        {
            f.B(DecorMat.Metal, 0.05f, w - 0.05f, 0.05f, d - 0.05f, 0f, 0.22f);
            f.B(DecorMat.Warn, 0.08f, w - 0.08f, 0.08f, d - 0.08f, 0.22f, 0.36f);
            f.B(DecorMat.Paint, 0.1f, w - 0.1f, 0.1f, d - 0.1f, 0.36f, h - 0.3f);
            f.B(DecorMat.Metal, 0.05f, w - 0.05f, 0.05f, d - 0.05f, h - 0.3f, h - 0.15f);
            f.B(DecorMat.Paint, w * 0.2f, w * 0.8f, d * 0.2f, d * 0.8f, h - 0.15f, h);
            // control panel with a screen on the front
            float pu = w * 0.55f;
            f.B(DecorMat.Metal, pu, pu + 0.7f, d - 0.35f, d - 0.1f, 0.95f, 1.5f);
            f.B(DecorMat.Screen, pu + 0.08f, pu + 0.62f, d - 0.12f, d - 0.09f, 1.12f, 1.42f);
            f.B(DecorMat.Metal, pu + 0.1f, pu + 0.2f, d - 0.1f, d - 0.07f, 1.0f, 1.08f);
            f.B(DecorMat.Warn, pu + 0.3f, pu + 0.4f, d - 0.1f, d - 0.07f, 1.0f, 1.08f);
            // vents on one side and pipes up the other
            for (int i = 0; i < 4; i++) f.B(DecorMat.Screen, 0.1f, 0.12f, d * 0.3f, d * 0.7f, 0.6f + i * 0.14f, 0.66f + i * 0.14f);
            f.B(DecorMat.Metal, w - 0.3f, w - 0.18f, 0.2f, 0.32f, 0.36f, h - 0.15f);
            if (variant % 2 == 0) f.B(DecorMat.Metal, w - 0.3f, w - 0.18f, d - 0.5f, d - 0.38f, 0.36f, h - 0.15f);
        }

        static void Crate(Frame f, float w, float d, float h)
        {
            f.B(DecorMat.Crate, 0.02f, w - 0.02f, 0.02f, d - 0.02f, 0.05f, h - 0.01f);
            f.B(DecorMat.WoodDark, 0f, w, 0f, d, 0f, 0.06f);
            f.B(DecorMat.WoodDark, 0f, w, 0f, d, h - 0.07f, h);
            f.B(DecorMat.WoodDark, 0f, 0.07f, 0f, 0.07f, 0f, h);
            f.B(DecorMat.WoodDark, w - 0.07f, w, 0f, 0.07f, 0f, h);
            f.B(DecorMat.WoodDark, 0f, 0.07f, d - 0.07f, d, 0f, h);
            f.B(DecorMat.WoodDark, w - 0.07f, w, d - 0.07f, d, 0f, h);
            f.B(DecorMat.Crate, 0.01f, w - 0.01f, 0.01f, d - 0.01f, h - 0.02f, h - 0.005f);
        }
    }
}
