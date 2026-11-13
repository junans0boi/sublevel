using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // Ground models of the pickups that have no model asset (#114): ammo, heals, armour, throwables, the key and the
    // jerrycan, built in code like the melee weapons (MeleeMeshes.Draft), no markings. Metres, y up, lying as they lie
    // on the floor; a little larger than life so they read at a distance. Built once per shape and shared by every
    // pickup. Three sub-meshes: Body (the kind's colour), Dark (lids, straps, steel) and Accent (a detail colour).
    public static class LootMeshes
    {
        public const int Body = 0, Dark = 1, Accent = 2;

        static readonly Dictionary<int, Mesh> Cache = new Dictionary<int, Mesh>();
        static readonly Color DarkColor = new Color(0.13f, 0.13f, 0.14f);
        static readonly Color Brass = new Color(0.80f, 0.62f, 0.28f);
        static readonly Color Cloth = new Color(0.88f, 0.85f, 0.76f);
        static readonly Color White = new Color(0.92f, 0.93f, 0.92f);
        // Backpacks by tier: khaki, olive, dark green (darker = higher, like the armour).
        static readonly Color[] BackpackColors = { new Color(0.66f, 0.60f, 0.42f), new Color(0.42f, 0.46f, 0.28f), new Color(0.20f, 0.27f, 0.18f) };

        // Null for kinds drawn otherwise (weapons, optics) or unknown.
        public static Mesh Model(LootKind kind, AmmoType ammo, int tier)
        {
            int key = (int)kind * 100 + (kind == LootKind.Ammo ? (int)ammo : Armor.IsArmor(kind) || kind == LootKind.Backpack ? Mathf.Clamp(tier, 1, 3) : 0);
            if (Cache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var d = new MeleeMeshes.Draft();
            switch (kind)
            {
                case LootKind.Ammo: BuildAmmo(d); break;
                case LootKind.Medkit: BuildMedkit(d); break;
                case LootKind.Helmet: BuildHelmet(d); break;
                case LootKind.Vest: BuildVest(d, Mathf.Clamp(tier, 1, 3)); break;
                case LootKind.Grenade: BuildGrenade(d); break;
                case LootKind.Key: BuildKey(d); break;
                case LootKind.Bandage: BuildBandage(d); break;
                case LootKind.FirstAidKit: BuildFirstAid(d); break;
                case LootKind.Smoke: BuildSmoke(d); break;
                case LootKind.Flash: BuildFlash(d); break;
                case LootKind.Molotov: BuildMolotov(d); break;
                case LootKind.Drink: BuildDrink(d); break;
                case LootKind.Pill: BuildPill(d); break;
                case LootKind.Syringe: BuildSyringe(d); break;
                case LootKind.Jerrycan: BuildJerrycan(d); break;
                case LootKind.Backpack: BuildBackpack(d, Mathf.Clamp(tier, 1, 3)); break;
                default: return null;
            }
            mesh = d.ToMesh("Loot" + kind + key % 100);
            Cache[key] = mesh;
            return mesh;
        }

        // ADR-100: ground model of an attachment (grip or stock) by its id; null for another id.
        public static Mesh Attachment(int id)
        {
            int key = -id;
            if (Cache.TryGetValue(key, out var mesh) && mesh != null) return mesh;
            var d = new MeleeMeshes.Draft();
            switch (id)
            {
                case 12: BuildGrip(d, 0f, 1); break;
                case 13: BuildGrip(d, 38f, 1); break;
                case 14: BuildGrip(d, 0f, 0); break;
                case 15: BuildStock(d); break;
                case 9: BuildMuzzle(d, 1); break;
                case 10: BuildMuzzle(d, 2); break;
                case 11: BuildMuzzle(d, 3); break;
                case 16: BuildMagazine(d); break;
                default: return null;
            }
            mesh = d.ToMesh("Attachment" + id);
            Cache[key] = mesh;
            return mesh;
        }

        public static void AttachmentColors(Color[] into)
        {
            into[Body] = new Color(0.42f, 0.46f, 0.52f);
            into[Dark] = new Color(0.16f, 0.17f, 0.19f);
            into[Accent] = new Color(0.96f, 0.82f, 0.38f);
        }

        // A grip lying on its side: the rail clamp and a post leaning `leanDeg` off straight; straight = false bends it.
        static void BuildGrip(MeleeMeshes.Draft d, float leanDeg, int straight)
        {
            const float r = 0.026f;
            d.Box(new Vector3(0f, 0.012f, 0f), new Vector3(0.11f, 0.024f, 0.045f), Dark);
            if (straight == 1)
            {
                var a = new Vector3(0f, r + 0.012f, 0.03f);
                var b = a + new Vector3(Mathf.Sin(leanDeg * Mathf.Deg2Rad) * 0.14f, 0f, Mathf.Cos(leanDeg * Mathf.Deg2Rad) * 0.14f);
                d.Tube(a, b, r, Body, 12);
                d.Disc(b, r + 0.006f, (b - a).normalized, Dark, 12);
                for (int i = 1; i <= 3; i++) d.Tube(Vector3.Lerp(a, b, 0.2f + i * 0.17f), Vector3.Lerp(a, b, 0.22f + i * 0.17f), r + 0.002f, Dark, 12);
            }
            else
            {
                // curved: beads along an arc, fatter toward the hand
                Vector3 prev = new Vector3(0f, r + 0.012f, 0.03f);
                for (int i = 1; i <= 7; i++)
                {
                    float t = i / 7f, ang = t * 1.2f;
                    var next = new Vector3(0f, r + 0.012f, 0.03f) + new Vector3(Mathf.Sin(ang) * 0.17f, 0f, Mathf.Cos(ang) * 0.17f - 0.17f + 0.13f * t);
                    d.Tube(prev, next, Mathf.Lerp(r * 0.8f, r * 1.2f, t), Body, 12);
                    prev = next;
                }
            }
        }

        // A muzzle device lying along x: collar, then a slotted block (1), a prong tube (2) or a long can (3).
        static void BuildMuzzle(MeleeMeshes.Draft d, int kind)
        {
            const float y = 0.035f;
            d.Tube(new Vector3(-0.09f, y, 0f), new Vector3(-0.07f, y, 0f), 0.026f, Dark, 12);
            if (kind == 1)
            {
                d.Box(new Vector3(0.0f, y, 0f), new Vector3(0.12f, 0.06f, 0.06f), Body);
                for (int i = 0; i < 3; i++) d.Box(new Vector3(-0.025f + i * 0.04f, y + 0.03f, 0f), new Vector3(0.012f, 0.004f, 0.062f), Dark);
            }
            else if (kind == 2)
            {
                d.Tube(new Vector3(-0.07f, y, 0f), new Vector3(0.02f, y, 0f), 0.022f, Body, 12);
                foreach (float z in new[] { -0.022f, 0f, 0.022f }) d.Box(new Vector3(0.065f, y, z), new Vector3(0.09f, 0.012f, 0.012f), Body);
            }
            else
            {
                d.Tube(new Vector3(-0.07f, y, 0f), new Vector3(0.17f, y, 0f), 0.034f, Body, 14);
                for (int i = 0; i < 4; i++) d.Tube(new Vector3(-0.03f + i * 0.06f, y, 0f), new Vector3(-0.026f + i * 0.06f, y, 0f), 0.0355f, Dark, 14);
                d.Disc(new Vector3(0.17f, y, 0f), 0.02f, Vector3.right, Dark, 12);
            }
        }

        // A magazine lying on its side: a box tapering up to the feed lips, floor plate in the accent colour.
        static void BuildMagazine(MeleeMeshes.Draft d)
        {
            d.Box(new Vector3(-0.02f, 0.03f, 0f), new Vector3(0.17f, 0.06f, 0.05f), Body);
            d.Box(new Vector3(0.075f, 0.03f, 0f), new Vector3(0.05f, 0.06f, 0.05f), Body);
            d.Box(new Vector3(0.1f, 0.062f, 0f), new Vector3(0.04f, 0.012f, 0.056f), Dark); // feed lips
            d.Box(new Vector3(-0.115f, 0.03f, 0f), new Vector3(0.012f, 0.066f, 0.058f), Accent); // floor plate
        }

        // A stock lying on its side: tube, butt plate with a pad, cheek riser, the receiver stub.
        static void BuildStock(MeleeMeshes.Draft d)
        {
            d.Box(new Vector3(0f, 0.035f, 0.11f), new Vector3(0.07f, 0.07f, 0.08f), Dark);
            d.Tube(new Vector3(0f, 0.035f, 0.07f), new Vector3(0f, 0.032f, -0.14f), 0.026f, Body, 12);
            d.Box(new Vector3(0f, 0.062f, -0.03f), new Vector3(0.044f, 0.018f, 0.14f), Body);
            d.Box(new Vector3(0f, 0.04f, -0.155f), new Vector3(0.07f, 0.13f, 0.02f), Dark);
            d.Box(new Vector3(0f, 0.04f, -0.168f), new Vector3(0.06f, 0.11f, 0.008f), Accent);
        }

        // Body, Dark and Accent colours of a pickup.
        public static void Colors(LootKind kind, AmmoType ammo, int tier, Color[] into)
        {
            into[Dark] = DarkColor;
            into[Accent] = White;
            switch (kind)
            {
                case LootKind.Ammo:
                    into[Body] = ammo == AmmoType.Pistol9 ? new Color(0.62f, 0.55f, 0.36f)
                        : ammo == AmmoType.Rifle556 ? new Color(0.30f, 0.45f, 0.26f)
                        : ammo == AmmoType.Shotgun12 ? new Color(0.62f, 0.18f, 0.15f)
                        : new Color(0.38f, 0.30f, 0.20f);
                    into[Accent] = Brass;
                    break;
                case LootKind.Medkit: into[Body] = new Color(0.36f, 0.40f, 0.42f); break;
                case LootKind.FirstAidKit: into[Body] = new Color(0.40f, 0.47f, 0.32f); break;
                case LootKind.Bandage: into[Body] = Cloth; into[Accent] = new Color(0.40f, 0.47f, 0.32f); break;
                case LootKind.Helmet:
                case LootKind.Vest: into[Body] = LootView.ArmorColor(kind, tier); into[Accent] = new Color(0.96f, 0.82f, 0.38f); break;
                case LootKind.Grenade: into[Body] = new Color(0.30f, 0.37f, 0.19f); into[Accent] = new Color(0.70f, 0.72f, 0.75f); break;
                case LootKind.Smoke: into[Body] = new Color(0.50f, 0.53f, 0.56f); into[Accent] = new Color(0.88f, 0.62f, 0.18f); break;
                case LootKind.Flash: into[Body] = new Color(0.78f, 0.80f, 0.84f); into[Accent] = new Color(0.96f, 0.84f, 0.25f); break;
                case LootKind.Molotov: into[Body] = new Color(0.22f, 0.40f, 0.24f); into[Accent] = new Color(0.82f, 0.74f, 0.55f); break;
                case LootKind.Drink: into[Body] = new Color(0.14f, 0.58f, 0.62f); into[Accent] = new Color(0.95f, 0.80f, 0.20f); break;
                case LootKind.Pill: into[Body] = new Color(0.86f, 0.52f, 0.14f); into[Accent] = White; break;
                case LootKind.Syringe: into[Body] = new Color(0.78f, 0.18f, 0.20f); into[Accent] = new Color(0.82f, 0.88f, 0.90f); break;
                case LootKind.Key: into[Body] = Brass; into[Accent] = new Color(0.78f, 0.22f, 0.20f); break;
                case LootKind.Jerrycan: into[Body] = new Color(0.62f, 0.16f, 0.10f); into[Accent] = new Color(0.70f, 0.72f, 0.75f); break;
                case LootKind.Backpack: into[Body] = BackpackColors[Mathf.Clamp(tier, 1, 3) - 1]; into[Accent] = new Color(0.96f, 0.82f, 0.38f); break;
                default: into[Body] = new Color(0.5f, 0.5f, 0.5f); break;
            }
        }

        // An ammo can with a lid and a carry handle, a few rounds lying in front of it.
        static void BuildAmmo(MeleeMeshes.Draft d)
        {
            d.Box(new Vector3(0f, 0.08f, 0f), new Vector3(0.26f, 0.16f, 0.12f), Body);
            d.Box(new Vector3(0f, 0.17f, 0f), new Vector3(0.27f, 0.02f, 0.13f), Dark);
            d.Box(new Vector3(0f, 0.19f, 0f), new Vector3(0.12f, 0.015f, 0.025f), Dark);
            d.Box(new Vector3(-0.135f, 0.12f, 0f), new Vector3(0.012f, 0.05f, 0.05f), Dark); // latch
            // Rounds lying on the lid beside the handle, so it reads as ammunition from any side.
            foreach (var at in new[] { new Vector3(-0.09f, 0f, 0.042f), new Vector3(0.02f, 0f, -0.042f), new Vector3(0.03f, 0f, 0.045f) })
            {
                float y = 0.18f + 0.011f;
                d.Tube(new Vector3(at.x, y, at.z), new Vector3(at.x + 0.06f, y, at.z), 0.011f, Accent, 8);
                d.Tube(new Vector3(at.x + 0.06f, y, at.z), new Vector3(at.x + 0.085f, y, at.z), 0.007f, Dark, 8);
            }
        }

        // A hard case with a handle on the long side, latches, and a white plus on the lid.
        static void BuildMedkit(MeleeMeshes.Draft d)
        {
            d.Box(new Vector3(0f, 0.065f, 0f), new Vector3(0.40f, 0.13f, 0.28f), Body);
            d.Box(new Vector3(0f, 0.105f, 0f), new Vector3(0.405f, 0.012f, 0.285f), Dark); // lid seam
            d.Box(new Vector3(0f, 0.07f, -0.155f), new Vector3(0.13f, 0.025f, 0.03f), Dark); // handle
            d.Box(new Vector3(-0.12f, 0.09f, -0.143f), new Vector3(0.03f, 0.04f, 0.01f), Dark);
            d.Box(new Vector3(0.12f, 0.09f, -0.143f), new Vector3(0.03f, 0.04f, 0.01f), Dark);
            Plus(d, new Vector3(0f, 0.131f, 0.01f), 0.15f, 0.045f);
        }

        // A soft pouch: rounded edges, a zip round the top and a white plus.
        static void BuildFirstAid(MeleeMeshes.Draft d)
        {
            const float w = 0.26f, h = 0.08f, l = 0.17f, r = 0.025f;
            d.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(w - 2f * r, h, l), Body);
            d.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h - 2f * r, l - 2f * r), Body);
            d.Tube(new Vector3(-w * 0.5f + r, r, -l * 0.5f + r), new Vector3(-w * 0.5f + r, r, l * 0.5f - r), r, Body, 8);
            d.Tube(new Vector3(w * 0.5f - r, r, -l * 0.5f + r), new Vector3(w * 0.5f - r, r, l * 0.5f - r), r, Body, 8);
            d.Tube(new Vector3(-w * 0.5f + r, h - r, -l * 0.5f + r), new Vector3(-w * 0.5f + r, h - r, l * 0.5f - r), r, Body, 8);
            d.Tube(new Vector3(w * 0.5f - r, h - r, -l * 0.5f + r), new Vector3(w * 0.5f - r, h - r, l * 0.5f - r), r, Body, 8);
            d.Box(new Vector3(0f, h * 0.72f, 0f), new Vector3(w + 0.004f, 0.008f, l + 0.004f), Dark); // zip
            d.Box(new Vector3(w * 0.5f + 0.01f, h * 0.72f, l * 0.25f), new Vector3(0.03f, 0.008f, 0.02f), Dark); // pull tab
            Plus(d, new Vector3(0f, h + 0.001f, 0f), 0.09f, 0.028f);
        }

        // A rolled bandage on its side with a loose tail on the floor and a coloured band.
        static void BuildBandage(MeleeMeshes.Draft d)
        {
            const float r = 0.055f;
            d.Tube(new Vector3(-0.05f, r, 0f), new Vector3(0.05f, r, 0f), r, Body, 16);
            d.Tube(new Vector3(-0.012f, r, 0f), new Vector3(0.012f, r, 0f), r + 0.003f, Accent, 16);
            d.Disc(new Vector3(-0.0505f, r, 0f), 0.018f, Vector3.left, Dark, 12);
            d.Disc(new Vector3(0.0505f, r, 0f), 0.018f, Vector3.right, Dark, 12);
            d.Box(new Vector3(0f, 0.003f, r + 0.07f), new Vector3(0.096f, 0.006f, 0.15f), Body); // tail
        }

        // A dome with a brim and a chin strap lying beside it; the tier shows in the colour.
        static void BuildHelmet(MeleeMeshes.Draft d)
        {
            const float r = 0.14f, y0 = 0.03f;
            const int rings = 6;
            for (int i = 0; i < rings; i++)
            {
                float a0 = i * Mathf.PI * 0.5f / rings, a1 = (i + 1) * Mathf.PI * 0.5f / rings;
                Frustum(d, new Vector3(0f, y0 + r * Mathf.Sin(a0), 0f), new Vector3(0f, y0 + r * Mathf.Sin(a1), 0f),
                    r * Mathf.Cos(a0), Mathf.Max(0.002f, r * Mathf.Cos(a1)), Body, 20);
            }
            Frustum(d, new Vector3(0f, 0f, 0f), new Vector3(0f, y0, 0f), r + 0.02f, r + 0.005f, Body, 20); // brim
            d.Disc(new Vector3(0f, 0.001f, 0f), r + 0.02f, Vector3.down, Dark, 20);
            d.Box(new Vector3(0f, 0.004f, r + 0.05f), new Vector3(0.16f, 0.008f, 0.02f), Dark); // strap
            d.Box(new Vector3(0f, y0 + r * 0.55f, -r * 0.83f), new Vector3(0.08f, 0.03f, 0.03f), Dark); // rear mount
        }

        // A plate carrier lying flat: front panel, shoulder straps, a plate pouch with one pale bar per tier.
        static void BuildVest(MeleeMeshes.Draft d, int tier)
        {
            d.Box(new Vector3(0f, 0.03f, 0f), new Vector3(0.38f, 0.06f, 0.44f), Body);
            d.Box(new Vector3(-0.13f, 0.03f, 0.29f), new Vector3(0.08f, 0.04f, 0.16f), Body); // shoulder straps
            d.Box(new Vector3(0.13f, 0.03f, 0.29f), new Vector3(0.08f, 0.04f, 0.16f), Body);
            d.Box(new Vector3(0f, 0.07f, -0.03f), new Vector3(0.26f, 0.025f, 0.28f), Dark); // plate pouch
            d.Box(new Vector3(-0.2f, 0.035f, -0.06f), new Vector3(0.03f, 0.04f, 0.22f), Dark); // side straps
            d.Box(new Vector3(0.2f, 0.035f, -0.06f), new Vector3(0.03f, 0.04f, 0.22f), Dark);
            for (int i = 0; i < tier; i++)
                d.Box(new Vector3(0f, 0.084f, -0.03f + (i - (tier - 1) * 0.5f) * 0.06f), new Vector3(0.16f, 0.006f, 0.025f), Accent);
        }

        // A frag grenade on its side: an egg-shaped body, the fuse head, the lever along the body and the pull ring.
        static void BuildGrenade(MeleeMeshes.Draft d)
        {
            const float r = 0.05f, half = 0.065f;
            const int rings = 8;
            for (int i = 0; i < rings; i++)
            {
                float t0 = -1f + 2f * i / rings, t1 = -1f + 2f * (i + 1) / rings;
                Frustum(d, new Vector3(t0 * half, r, 0f), new Vector3(t1 * half, r, 0f),
                    Mathf.Max(0.004f, r * Mathf.Sqrt(1f - t0 * t0)), Mathf.Max(0.004f, r * Mathf.Sqrt(1f - t1 * t1)), Body, 14);
            }
            d.Tube(new Vector3(half - 0.006f, r, 0f), new Vector3(half + 0.022f, r, 0f), 0.016f, Dark, 10); // fuse head
            d.Box(new Vector3(0.01f, r + r * 0.92f, 0f), new Vector3(0.08f, 0.006f, 0.016f), Dark); // lever
            d.Box(new Vector3(half + 0.026f, r + 0.008f, 0f), new Vector3(0.012f, 0.03f, 0.016f), Dark);
            Ring(d, new Vector3(half + 0.03f, r, 0.03f), 0.016f, 0.003f, Accent);
        }

        // A smoke canister on its side: a grey can with a dark cap, an orange band and the lever.
        static void BuildSmoke(MeleeMeshes.Draft d)
        {
            const float r = 0.042f;
            d.Tube(new Vector3(-0.07f, r, 0f), new Vector3(0.05f, r, 0f), r, Body, 14);
            d.Tube(new Vector3(0.05f, r, 0f), new Vector3(0.075f, r, 0f), r * 0.75f, Dark, 12);
            d.Tube(new Vector3(-0.025f, r, 0f), new Vector3(0.0f, r, 0f), r + 0.002f, Accent, 14);
            d.Box(new Vector3(0.02f, r * 2f + 0.003f, 0f), new Vector3(0.08f, 0.006f, 0.016f), Dark); // lever
            Ring(d, new Vector3(0.085f, r, 0.028f), 0.014f, 0.003f, Dark);
        }

        // A flash grenade on its side: a pale steel can, a yellow band, a dark cap with the lever and a pull ring.
        static void BuildFlash(MeleeMeshes.Draft d)
        {
            const float r = 0.036f;
            d.Tube(new Vector3(-0.06f, r, 0f), new Vector3(0.05f, r, 0f), r, Body, 14);
            d.Tube(new Vector3(0.05f, r, 0f), new Vector3(0.072f, r, 0f), r * 0.8f, Dark, 12);
            d.Tube(new Vector3(-0.045f, r, 0f), new Vector3(-0.02f, r, 0f), r + 0.002f, Accent, 14);
            d.Tube(new Vector3(-0.012f, r, 0f), new Vector3(0.012f, r, 0f), r + 0.002f, Accent, 14);
            d.Box(new Vector3(0.015f, r * 2f + 0.003f, 0f), new Vector3(0.085f, 0.006f, 0.016f), Dark); // lever
            Ring(d, new Vector3(0.082f, r, 0.026f), 0.014f, 0.003f, Dark);
        }

        // A fire bottle on its side: a green glass bottle with a long neck and a pale rag stuffed into the mouth.
        static void BuildMolotov(MeleeMeshes.Draft d)
        {
            const float r = 0.04f;
            d.Tube(new Vector3(-0.08f, r, 0f), new Vector3(0.03f, r, 0f), r, Body, 14);
            d.Tube(new Vector3(0.03f, r, 0f), new Vector3(0.1f, r * 0.55f, 0f), r * 0.5f, Body, 12);
            d.Tube(new Vector3(0.1f, r * 0.55f, 0f), new Vector3(0.14f, r * 0.55f, 0f), r * 0.32f, Body, 10);
            d.Tube(new Vector3(0.135f, r * 0.55f, 0f), new Vector3(0.17f, r * 0.55f, 0.01f), 0.014f, Accent, 8); // rag in the mouth
            d.Tube(new Vector3(0.165f, r * 0.55f, 0.01f), new Vector3(0.2f, 0.012f, 0.04f), 0.01f, Accent, 8);   // loose end
            d.Tube(new Vector3(-0.04f, r, 0f), new Vector3(0.0f, r, 0f), r + 0.002f, Accent, 14);                  // label
        }

        // A drink can on its side: a body, a rim at each end, a band and the pull tab on the top end.
        static void BuildDrink(MeleeMeshes.Draft d)
        {
            const float r = 0.04f, half = 0.085f;
            d.Tube(new Vector3(-half, r, 0f), new Vector3(half, r, 0f), r, Body, 14);
            d.Tube(new Vector3(-half - 0.012f, r, 0f), new Vector3(-half, r, 0f), r * 0.86f, Dark, 12);
            d.Tube(new Vector3(half, r, 0f), new Vector3(half + 0.012f, r, 0f), r * 0.86f, Dark, 12);
            d.Tube(new Vector3(-0.03f, r, 0f), new Vector3(0.03f, r, 0f), r + 0.002f, Accent, 14); // band
            d.Box(new Vector3(half + 0.012f, r + r * 0.86f, 0f), new Vector3(0.022f, 0.004f, 0.016f), Accent); // tab
        }

        // A pill bottle on its side: an amber body, a dark cap, a pale label round it and two pills in front.
        static void BuildPill(MeleeMeshes.Draft d)
        {
            const float r = 0.032f;
            d.Tube(new Vector3(-0.055f, r, 0f), new Vector3(0.045f, r, 0f), r, Body, 14);
            d.Tube(new Vector3(0.045f, r, 0f), new Vector3(0.075f, r, 0f), r + 0.003f, Dark, 14);
            d.Tube(new Vector3(-0.035f, r, 0f), new Vector3(0.02f, r, 0f), r + 0.002f, Accent, 14);
            d.Tube(new Vector3(-0.02f, 0.008f, 0.07f), new Vector3(0.004f, 0.008f, 0.07f), 0.008f, Accent, 8);
            d.Tube(new Vector3(0.02f, 0.008f, 0.088f), new Vector3(0.044f, 0.008f, 0.092f), 0.008f, Accent, 8);
        }

        // An auto-injector on its side: a clear barrel with a coloured fill, a plunger with a thumb rest, a needle guard.
        static void BuildSyringe(MeleeMeshes.Draft d)
        {
            const float r = 0.02f;
            d.Tube(new Vector3(-0.07f, r, 0f), new Vector3(0.07f, r, 0f), r, Accent, 12);
            d.Tube(new Vector3(-0.045f, r, 0f), new Vector3(0.05f, r, 0f), r * 0.7f, Body, 10); // fluid
            d.Tube(new Vector3(-0.12f, r, 0f), new Vector3(-0.07f, r, 0f), 0.005f, Dark, 8); // plunger rod
            d.Box(new Vector3(-0.125f, r, 0f), new Vector3(0.008f, 0.022f, 0.05f), Dark); // thumb rest
            d.Box(new Vector3(-0.07f, r, 0f), new Vector3(0.008f, 0.012f, 0.06f), Dark); // finger flange
            d.Tube(new Vector3(0.07f, r, 0f), new Vector3(0.1f, r, 0f), r * 0.55f, Dark, 8); // hub
            d.Tube(new Vector3(0.1f, r, 0f), new Vector3(0.15f, r, 0f), 0.003f, Accent, 6); // needle
        }

        // A key lying flat: a ring bow, the shaft with two teeth, and a red tag through the bow.
        static void BuildKey(MeleeMeshes.Draft d)
        {
            const float y = 0.008f;
            Ring(d, new Vector3(-0.06f, y, 0f), 0.035f, 0.009f, Body, true);
            d.Box(new Vector3(0.025f, y, 0f), new Vector3(0.12f, 0.012f, 0.018f), Body);
            d.Box(new Vector3(0.07f, y, 0.018f), new Vector3(0.014f, 0.012f, 0.025f), Body);
            d.Box(new Vector3(0.045f, y, 0.016f), new Vector3(0.012f, 0.012f, 0.02f), Body);
            d.Box(new Vector3(-0.12f, 0.006f, -0.03f), new Vector3(0.045f, 0.012f, 0.06f), Accent); // tag
        }

        // A jerrycan standing up: three handles on top, a capped spout on the front corner, pressed ridges on the sides.
        static void BuildJerrycan(MeleeMeshes.Draft d)
        {
            const float w = 0.17f, h = 0.46f, l = 0.34f;
            d.Box(new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, l), Body);
            for (int i = 0; i < 3; i++)
            {
                float z = -0.09f + i * 0.07f;
                d.Box(new Vector3(0f, h + 0.03f, z), new Vector3(0.025f, 0.06f, 0.05f), Body);
            }
            d.Box(new Vector3(0f, h + 0.06f, -0.02f), new Vector3(0.025f, 0.012f, 0.19f), Dark); // handle grips
            d.Tube(new Vector3(0f, h - 0.01f, l * 0.5f - 0.04f), new Vector3(0f, h + 0.05f, l * 0.5f - 0.02f), 0.024f, Body, 10);
            d.Tube(new Vector3(0f, h + 0.045f, l * 0.5f - 0.022f), new Vector3(0f, h + 0.075f, l * 0.5f - 0.016f), 0.028f, Accent, 10);
            foreach (float s in new[] { -1f, 1f })
            {
                float x = s * (w * 0.5f + 0.002f);
                Strip(d, new Vector3(x, 0.08f, -0.13f), new Vector3(x, 0.38f, 0.13f), s, Dark);
                Strip(d, new Vector3(x, 0.08f, 0.13f), new Vector3(x, 0.38f, -0.13f), s, Dark);
            }
        }

        // A backpack standing up (ADR-091), its back panel toward -z: a rounded pack with a lid flap and a front
        // pocket carrying one pale bar per tier, two shoulder straps and a carry loop. Higher tiers are bigger.
        // The same model is worn on the character's back (BackpackVisual).
        static void BuildBackpack(MeleeMeshes.Draft d, int tier)
        {
            float w = 0.30f + 0.03f * tier, h = 0.36f + 0.06f * tier, l = 0.16f + 0.03f * tier, r = l * 0.5f;
            d.Box(new Vector3(0f, (h - r) * 0.5f, 0f), new Vector3(w, h - r, l), Body);
            d.Tube(new Vector3(-w * 0.5f, h - r, 0f), new Vector3(w * 0.5f, h - r, 0f), r, Body, 14); // rounded top
            d.Box(new Vector3(0f, h - r * 0.4f, l * 0.18f), new Vector3(w * 0.9f, r * 0.9f, l * 0.75f), Dark); // lid flap
            d.Box(new Vector3(0f, h * 0.68f, l * 0.5f + 0.008f), new Vector3(0.06f, h * 0.22f, 0.016f), Dark); // lid strap
            // Front pocket with the tier bars.
            float pw = w * 0.72f, ph = h * 0.36f, pz = l * 0.5f + 0.03f;
            d.Box(new Vector3(0f, h * 0.24f, pz - 0.015f), new Vector3(pw, ph, 0.06f), Body);
            d.Box(new Vector3(0f, h * 0.24f + ph * 0.5f - 0.01f, pz + 0.016f), new Vector3(pw + 0.004f, 0.012f, 0.004f), Dark); // zip
            for (int i = 0; i < tier; i++)
                d.Box(new Vector3(0f, h * 0.22f + (i - (tier - 1) * 0.5f) * 0.035f, pz + 0.017f), new Vector3(pw * 0.55f, 0.016f, 0.004f), Accent);
            // A padded back panel with two flat shoulder straps.
            d.Box(new Vector3(0f, h * 0.45f, -l * 0.5f - 0.01f), new Vector3(w * 0.8f, h * 0.7f, 0.02f), Dark);
            foreach (float s in new[] { -1f, 1f })
            {
                d.Box(new Vector3(s * w * 0.24f, h * 0.5f, -l * 0.5f - 0.035f), new Vector3(0.06f, h * 0.78f, 0.03f), Body);
                d.Box(new Vector3(s * (w * 0.5f + 0.012f), h * 0.42f, 0f), new Vector3(0.024f, h * 0.5f, l * 0.55f), Dark); // side compression strap
            }
            d.Box(new Vector3(0f, h + 0.02f, -l * 0.2f), new Vector3(0.09f, 0.03f, 0.02f), Dark); // carry loop
        }

        // A white plus lying on a lid at `at` (its top surface).
        static void Plus(MeleeMeshes.Draft d, Vector3 at, float size, float bar)
        {
            d.Box(at + new Vector3(0f, 0.002f, 0f), new Vector3(size, 0.004f, bar), Accent);
            d.Box(at + new Vector3(0f, 0.002f, 0f), new Vector3(bar, 0.004f, size), Accent);
        }

        // A cone section with flat sides from a (radius ra) to b (radius rb), closed at both ends.
        static void Frustum(MeleeMeshes.Draft d, Vector3 a, Vector3 b, float ra, float rb, int sub, int segments)
        {
            var dir = (b - a).normalized;
            var side = Vector3.Cross(dir, Mathf.Abs(dir.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
            var up = Vector3.Cross(side, dir);
            float step = Mathf.PI * 2f / segments;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s * step, a1 = (s + 1) * step, am = (a0 + a1) * 0.5f;
                Vector3 u0 = side * Mathf.Cos(a0) + up * Mathf.Sin(a0), u1 = side * Mathf.Cos(a1) + up * Mathf.Sin(a1);
                var outward = side * Mathf.Cos(am) + up * Mathf.Sin(am) + dir * ((ra - rb) / Mathf.Max(1e-4f, (b - a).magnitude));
                d.Quad(a + u0 * ra, a + u1 * ra, b + u1 * rb, b + u0 * rb, outward, sub);
                d.Quad(a, a, a + u1 * ra, a + u0 * ra, -dir, sub);
                d.Quad(b, b, b + u1 * rb, b + u0 * rb, dir, sub);
            }
        }

        // A ring of short bars round `centre`, flat (in the x-z plane) or standing (x-y plane).
        static void Ring(MeleeMeshes.Draft d, Vector3 centre, float radius, float thick, int sub, bool flat = false)
        {
            const int n = 10;
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
                var p0 = flat ? new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) : new Vector3(0f, Mathf.Sin(a0), Mathf.Cos(a0));
                var p1 = flat ? new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) : new Vector3(0f, Mathf.Sin(a1), Mathf.Cos(a1));
                d.Tube(centre + p0 * radius, centre + p1 * radius, thick, sub, 6);
            }
        }

        // A flat diagonal strip on a side face (x = from.x) facing +x or -x.
        static void Strip(MeleeMeshes.Draft d, Vector3 from, Vector3 to, float facing, int sub)
        {
            var along = (to - from).normalized;
            var across = Vector3.Cross(along, Vector3.right).normalized * 0.009f;
            d.Quad(from - across, to - across, to + across, from + across, Vector3.right * facing, sub);
        }
    }
}
