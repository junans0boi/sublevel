using FishNet.Object.Synchronizing;
using Game.Core;
using UnityEngine;

namespace Game.Net
{
    // Paint variant (S10 T8): the server picks a colour index at spawn, everyone tints the model's paint materials
    // (those named "*_Paint") with it. Models without a paint material (the bikes, the sports car) stay as they are.
    public partial class Vehicle
    {
        public readonly SyncVar<int> PaintIndex = new SyncVar<int>(VehicleColors.None);
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        // Server, before the vehicle is spawned.
        public void ServerSetPaint(int index) => PaintIndex.Value = index;

        public override void OnStartClient()
        {
            PaintIndex.OnChange -= OnPaintChanged;
            PaintIndex.OnChange += OnPaintChanged;
            ApplyPaint(PaintIndex.Value);
        }

        public override void OnStopClient() => PaintIndex.OnChange -= OnPaintChanged;

        void OnPaintChanged(int prev, int next, bool asServer) => ApplyPaint(next);

        // #452 (ADR-547): one shared material per (paint material, colour) instead of a property block — a block takes
        // the renderer out of the SRP Batcher, ~2 SetPass per parked car in a town view.
        static readonly System.Collections.Generic.Dictionary<(Material, int), Material> Painted = new System.Collections.Generic.Dictionary<(Material, int), Material>();
        static readonly System.Collections.Generic.Dictionary<Material, Material> Unpainted = new System.Collections.Generic.Dictionary<Material, Material>();

        void ApplyPaint(int index)
        {
            if (!VehicleColors.Valid(index)) return;
            VehicleColors.Tint(index, out float r, out float g, out float b);
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].name.EndsWith("_Paint")) continue;
                    var source = Unpainted.TryGetValue(mats[i], out var original) ? original : mats[i];   // a repaint tints the model's own material again
                    if (!Painted.TryGetValue((source, index), out var painted))
                    {
                        painted = new Material(source) { name = source.name };
                        var baseColor = source.HasProperty(BaseColor) ? source.GetColor(BaseColor) : Color.white;
                        painted.SetColor(BaseColor, new Color(baseColor.r * r, baseColor.g * g, baseColor.b * b, baseColor.a));
                        Painted[(source, index)] = painted;
                        Unpainted[painted] = source;
                    }
                    mats[i] = painted;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = mats;
            }
        }
    }
}
