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

        void ApplyPaint(int index)
        {
            if (!VehicleColors.Valid(index)) return;
            VehicleColors.Tint(index, out float r, out float g, out float b);
            var block = new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].name.EndsWith("_Paint")) continue;
                    var baseColor = mats[i].HasProperty(BaseColor) ? mats[i].GetColor(BaseColor) : Color.white;
                    renderer.GetPropertyBlock(block, i);
                    block.SetColor(BaseColor, new Color(baseColor.r * r, baseColor.g * g, baseColor.b * b, baseColor.a));
                    renderer.SetPropertyBlock(block, i);
                }
            }
        }
    }
}
