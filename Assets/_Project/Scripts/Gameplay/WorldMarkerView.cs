using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The map marker in the world (ADR-065 (6)): a small diamond with the distance under it, drawn on the HUD canvas
    // where the marker's point projects on screen, hidden while that point is behind the camera or off screen.
    // Moved only when it shifts by more than half a reference pixel; the label is rebuilt only when the whole-metre
    // distance changes (the strings come from MapMarkerText's cache).
    public sealed class WorldMarkerView
    {
        // Drawn this far above the marked ground so it does not sink into grass or a floor.
        public const float LiftMeters = 1.2f;
        const float Diamond = 14f;
        static readonly Color Rim = new Color(0.04f, 0.05f, 0.06f, 0.95f);

        public RectTransform Root { get; private set; }
        public bool Shown => Root != null && Root.gameObject.activeSelf;
        // Position on the canvas from its centre, in reference pixels; for tests.
        public Vector2 CanvasPosition => Root != null ? Root.anchoredPosition : Vector2.zero;
        public string DistanceText => Shown ? _distance.text : "";

        Image _fill;
        Text _distance;
        Color _colour;
        int _metres = -1;

        public static WorldMarkerView Build(Transform canvasRoot)
        {
            var v = new WorldMarkerView();
            var centre = new Vector2(0.5f, 0.5f);
            v._colour = MapMarker.Colour;
            v.Root = UiKit.Rect("WorldMarker", canvasRoot, centre, centre, Vector2.zero, new Vector2(Diamond, Diamond));
            var diamond = UiKit.Rect("Diamond", v.Root, centre, centre, Vector2.zero, new Vector2(Diamond, Diamond));
            diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
            UiKit.Box(UiKit.Fill("Rim", diamond, -2f), Rim);
            v._fill = UiKit.Box(UiKit.Fill("Fill", diamond), v._colour);
            v._distance = UiKit.Label(UiKit.Rect("Distance", v.Root, centre, new Vector2(0.5f, 1f), new Vector2(0f, -Diamond * 0.5f - 6f), new Vector2(80f, 22f)),
                "", 17, v._colour, TextAnchor.MiddleCenter, FontStyle.Bold);
            var shadow = v._distance.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Rim;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
            v.Root.gameObject.SetActive(false);
            return v;
        }

        // `canvasScale` is the HUD canvas's scale factor (screen pixels per reference pixel).
        public void Tick(Camera camera, float canvasScale, bool show, int metres)
        {
            if (Root == null) return;
            bool visible = show && camera != null && canvasScale > 0f;
            Vector3 screen = default;
            if (visible)
            {
                screen = camera.WorldToScreenPoint(MapMarker.Position + Vector3.up * LiftMeters);
                visible = screen.z > 0f && screen.x >= 0f && screen.y >= 0f && screen.x <= Screen.width && screen.y <= Screen.height;
            }
            if (Root.gameObject.activeSelf != visible) Root.gameObject.SetActive(visible);
            if (!visible) return;

            var at = new Vector2((screen.x - Screen.width * 0.5f) / canvasScale, (screen.y - Screen.height * 0.5f) / canvasScale);
            if ((at - Root.anchoredPosition).sqrMagnitude > 0.25f) Root.anchoredPosition = at;

            var colour = MapMarker.Colour;
            if (colour != _colour)
            {
                _colour = colour;
                _fill.color = colour;
                _distance.color = colour;
            }
            if (metres == _metres) return;
            _metres = metres;
            _distance.text = MapMarkerText.Metres(metres);
        }
    }
}
