using Game.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Top left: where the local player is. The region is the one whose centre is nearest on the ground
    // (WorldInfo.Current); the card is hidden when the scene has no map. The state is the lockdown's for that region
    // (ZoneStatus.Current, "안전" without a lockdown); the arrow points at the nearest pass of the region.
    public sealed class RegionCard
    {
        public const float Width = 320f, Height = 104f;
        public const string StateSafe = "안전";

        public RectTransform Root { get; private set; }

        Text _name, _state, _pass;
        RectTransform _arrow;
        GameObject _arrowRoot, _passRoot;
        int _shownRegion = -2, _shownMeters = int.MinValue;
        float _shownArrowZ = float.NaN;
        ZoneState _shownState = ZoneState.Safe;

        public bool Shown => Root != null && Root.gameObject.activeSelf;
        public string NameShown => Shown ? _name.text : "";
        public string StateShownText => Shown ? _state.text : "";
        public bool PassArrowShown => Shown && _passRoot.activeSelf;

        public static RegionCard Build(Transform parent, Vector2 anchor, Vector2 position)
        {
            var c = new RegionCard();
            var root = UiKit.Rect("Region", parent, anchor, anchor, position, new Vector2(Width, Height));
            UiKit.Box(root, UiKit.Panel);
            UiKit.Frame(root, 1f, UiKit.SteelDim);
            UiKit.Brackets(root, 10f, 2f, UiKit.Steel);
            c.Root = root;
            var topLeft = new Vector2(0f, 1f);
            UiKit.Label(UiKit.Rect("Caption", root, topLeft, topLeft, new Vector2(14f, -6f), new Vector2(120f, 22f)), "현재 지역", 18, UiKit.TextDim);
            c._name = UiKit.Label(UiKit.Rect("Name", root, topLeft, topLeft, new Vector2(14f, -28f), new Vector2(292f, 34f)), "", 26, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            c._state = UiKit.Label(UiKit.Rect("State", root, topLeft, topLeft, new Vector2(14f, -68f), new Vector2(120f, 28f)), "상태  " + StateSafe, 20, UiKit.Text);

            // Nearest pass: an arrow turned relative to the way the player faces, and the distance.
            c._passRoot = UiKit.Rect("Pass", root, topLeft, topLeft, new Vector2(150f, -66f), new Vector2(160f, 32f)).gameObject;
            c._arrow = UiKit.Rect("Arrow", c._passRoot.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(24f, 24f));
            UiKit.Box(c._arrow, UiKit.TextDim).sprite = HudSprites.Arrow;
            c._pass = UiKit.Label(UiKit.Rect("Distance", c._passRoot.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(126f, 30f)),
                "", 20, UiKit.TextDim);
            c._arrowRoot = c._arrow.gameObject;
            c._passRoot.SetActive(false);
            root.gameObject.SetActive(false);
            return c;
        }

        public void Tick(Vector3 position, float yaw)
        {
            var world = WorldInfo.Current;
            int region = world != null ? Minimap.NearestRegion(world.Regions, position) : -1;
            bool on = region >= 0;
            if (Root.gameObject.activeSelf != on) Root.gameObject.SetActive(on);
            if (!on)
            {
                _shownRegion = -2;
                return;
            }
            if (region != _shownRegion)
            {
                _shownRegion = region;
                _name.text = world.Regions[region].Name ?? "";
                _shownMeters = int.MinValue;
            }

            // The lockdown state where the player stands (safe when there is no lockdown in this scene).
            var zone = ZoneStatus.Current;
            var state = zone.Valid ? zone.State : ZoneState.Safe;
            if (state != _shownState)
            {
                _shownState = state;
                _state.text = "상태  " + ZoneStatus.Label(state);
                _state.color = state == ZoneState.Safe ? UiKit.Text : state == ZoneState.Warning ? UiKit.Accent : UiKit.Danger;
            }

            // The nearest pass that leaves this region.
            var passes = world.Passes;
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < passes.Count; i++)
            {
                if (passes[i].RegionA != region && passes[i].RegionB != region) continue;
                float dx = passes[i].Position.x - position.x, dz = passes[i].Position.z - position.z;
                float sqr = dx * dx + dz * dz;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = i;
            }
            bool hasPass = best >= 0;
            if (_passRoot.activeSelf != hasPass) _passRoot.SetActive(hasPass);
            if (!hasPass) return;

            int meters = Mathf.RoundToInt(Mathf.Sqrt(bestSqr) / 5f) * 5;
            if (meters != _shownMeters)
            {
                _shownMeters = meters;
                _pass.text = "통로 " + HudDigits.IntText(meters) + "m";
            }
            float z = Minimap.RelativeArrowZ(Minimap.Bearing(position, passes[best].Position), yaw);
            if (Mathf.Abs(Mathf.DeltaAngle(z, _shownArrowZ)) >= 1f || float.IsNaN(_shownArrowZ))
            {
                _shownArrowZ = z;
                _arrow.localRotation = Quaternion.Euler(0f, 0f, z);
            }
        }
    }
}
