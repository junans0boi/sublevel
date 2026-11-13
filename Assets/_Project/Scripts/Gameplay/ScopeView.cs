using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // What the local player sees through the optic on the weapon in hand while looking down the sights (a tap on
    // aim; ADR-045, ADR-056, ADR-058). Shoulder (hold) aim and the hip show nothing: no magnification.
    //  - Red dot / holographic (1x): a reticle in the middle of the screen; the iron-sight dot is hidden.
    //  - Scope (2x and up): the view narrows by the magnification, a black lens ring covers the screen edges and
    //    a thin crosshair with range ticks and a magnification label sits in the lens.
    // Everything is drawn by code: runtime textures, no shaders or assets. It also publishes what the rest of the
    // game must follow: Active (the HUD hides its crosshair), FovOverride (the camera's field of view) and
    // SensitivityFactor (mouse look). Created by PlayerController next to the other local screens.
    [DefaultExecutionOrder(-50)] // before PlayerController, which reads FovOverride when it places the camera
    public class ScopeView : MonoBehaviour
    {
        // True while a reticle or the scope overlay is drawn (including while it fades out).
        public static bool Active { get; private set; }
        // Multiplier for mouse look: 1 when not scoped, eased with the lens.
        public static float SensitivityFactor { get; private set; } = 1f;
        // Vertical field of view the camera should ease to; 0 or less means "none, use the aim stance's".
        public static float FovOverride { get; private set; }
        // True while a magnified scope covers most of the view: the weapon at the eye is not drawn.
        public static bool HidesWeapon { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = false;
            SensitivityFactor = 1f;
            FovOverride = 0f;
            HidesWeapon = false;
        }

        // The lens window is this many reference pixels across (the screen is 1080 tall in reference space).
        const float LensDiameter = 1040f;
        const float FadePerSecond = 8f;
        const int LensTextureSize = 512;

        static readonly Color ReticleRed = new Color(1f, 0.28f, 0.2f, 1f);
        static readonly Color ReticleAmber = new Color(1f, 0.74f, 0.22f, 1f);
        static readonly Color Ink = new Color(0.03f, 0.03f, 0.03f, 0.94f);

        // Read by tests and the HUD.
        public float ScopeBlend { get; private set; }
        public float ReticleBlend { get; private set; }

        Canvas _canvas;
        CanvasGroup _scopeGroup, _reticleGroup;
        GameObject _dotReticle, _holoReticle;
        Text _zoomLabel;
        Texture2D _lensTexture, _dotTexture, _holoTexture;
        Sprite _lensSprite, _dotSprite, _holoSprite;

        Combatant _combatantOf;
        CombatantOptics _optics;
        int _shownOptic = -1;
        float _lastFactor = 1f;

        void Awake()
        {
            _canvas = UiKit.Canvas("ScopeCanvas", -1, false);
            _canvas.transform.SetParent(transform, false);
            BuildReticles(_canvas.transform);
            BuildScope(_canvas.transform);
            _canvas.enabled = false;
        }

        void OnDestroy()
        {
            ResetStatics();
            if (_lensSprite != null) Destroy(_lensSprite);
            if (_dotSprite != null) Destroy(_dotSprite);
            if (_holoSprite != null) Destroy(_holoSprite);
            if (_lensTexture != null) Destroy(_lensTexture);
            if (_dotTexture != null) Destroy(_dotTexture);
            if (_holoTexture != null) Destroy(_holoTexture);
        }

        void Update()
        {
            var def = CurrentOptic();
            bool scoped = def != null && def.Kind == OpticKind.Scope && def.Zoom > 1f;
            bool reticle = def != null && !scoped;

            if (def != null)
            {
                _lastFactor = def.SensitivityFactor;
                if (def.Id != _shownOptic) ShowOptic(def);
            }

            float step = FadePerSecond * Time.unscaledDeltaTime;
            ScopeBlend = Mathf.MoveTowards(ScopeBlend, scoped ? 1f : 0f, step);
            ReticleBlend = Mathf.MoveTowards(ReticleBlend, reticle ? 1f : 0f, step);
            if (ScopeBlend <= 0f && !scoped) _lastFactor = 1f;

            // The view follows the target at once: the camera eases its own field of view.
            FovOverride = scoped ? Optics.FovFor(GameData.Rules.Aim.Sights.Fov, def.Zoom) : 0f;
            SensitivityFactor = Mathf.Lerp(1f, _lastFactor, ScopeBlend);
            Active = def != null || ScopeBlend > 0f || ReticleBlend > 0f;
            HidesWeapon = ScopeBlend >= 0.5f;

            bool draw = ScopeBlend > 0f || ReticleBlend > 0f;
            if (_canvas.enabled != draw) _canvas.enabled = draw;
            if (!draw) return;
            if (_scopeGroup.alpha != ScopeBlend) _scopeGroup.alpha = ScopeBlend;
            if (_reticleGroup.alpha != ReticleBlend) _reticleGroup.alpha = ReticleBlend;
        }

        // The optic on the weapon in hand while the local player looks down the sights; null otherwise.
        OpticDef CurrentOptic()
        {
            var me = Combatant.LocalPlayer;
            var pc = PlayerController.Local;
            if (me == null || pc == null || me.Dead.Value || pc.Aim != AimMode.Sights || UiFocus.Current != UiScreen.None) return null;
            if (_combatantOf != me)
            {
                _combatantOf = me;
                _optics = me.GetComponent<CombatantOptics>();
            }
            if (_optics == null) return null;
            // The optic is only on the weapon while that weapon is in hand.
            return me.ActiveWeaponId != 0 ? _optics.ActiveOpticDef : null;
        }

        void ShowOptic(OpticDef def)
        {
            _shownOptic = def.Id;
            bool holo = def.Reticle == ReticleStyle.Ring;
            _dotReticle.SetActive(!holo);
            _holoReticle.SetActive(holo);
            if (def.Kind == OpticKind.Scope) _zoomLabel.text = Optics.ZoomText(def.Zoom);
        }

        // ---- Building ----

        void BuildReticles(Transform root)
        {
            var group = UiKit.Fill("Reticle", root);
            _reticleGroup = group.gameObject.AddComponent<CanvasGroup>();
            _reticleGroup.blocksRaycasts = false;
            _reticleGroup.interactable = false;
            _reticleGroup.alpha = 0f;
            var centre = new Vector2(0.5f, 0.5f);

            _dotTexture = DotTexture(64);
            _dotSprite = MakeSprite(_dotTexture);
            var dot = UiKit.Rect("Dot", group, centre, centre, Vector2.zero, new Vector2(48f, 48f));
            Picture(dot, _dotSprite, ReticleRed);
            _dotReticle = dot.gameObject;

            _holoTexture = HoloTexture(128);
            _holoSprite = MakeSprite(_holoTexture);
            var holo = UiKit.Rect("Holo", group, centre, centre, Vector2.zero, new Vector2(110f, 110f));
            Picture(holo, _holoSprite, ReticleAmber);
            _holoReticle = holo.gameObject;
            _holoReticle.SetActive(false);
        }

        void BuildScope(Transform root)
        {
            var group = UiKit.Fill("Scope", root);
            _scopeGroup = group.gameObject.AddComponent<CanvasGroup>();
            _scopeGroup.blocksRaycasts = false;
            _scopeGroup.interactable = false;
            _scopeGroup.alpha = 0f;
            var centre = new Vector2(0.5f, 0.5f);
            float half = LensDiameter * 0.5f;

            // The lens: a square picture, clear inside the circle and black outside; four panels black out the rest.
            _lensTexture = LensTexture(LensTextureSize);
            _lensSprite = MakeSprite(_lensTexture);
            var lens = UiKit.Rect("Lens", group, centre, centre, Vector2.zero, new Vector2(LensDiameter, LensDiameter));
            Picture(lens, _lensSprite, Color.black);
            Panel("Left", group, new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(-half, 0f));
            Panel("Right", group, new Vector2(0.5f, 0f), new Vector2(1f, 1f), new Vector2(half, 0f), new Vector2(0f, 0f));
            Panel("Top", group, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 1f), new Vector2(-half, half), new Vector2(half, 0f));
            Panel("Bottom", group, new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(-half, 0f), new Vector2(half, -half));

            // Crosshair: thin lines from the middle outward, heavy posts at the edge, range ticks below.
            const float gap = 9f, thin = 1.6f, postFrom = 300f, postTo = 505f, post = 5f;
            for (int dir = 0; dir < 4; dir++)
            {
                bool horizontal = dir < 2;
                float sign = (dir & 1) == 0 ? 1f : -1f;
                Segment(group, "Thin" + dir, horizontal, sign, gap, postFrom, thin);
                Segment(group, "Post" + dir, horizontal, sign, postFrom, postTo, post);
            }
            for (int i = 0; i < 5; i++)
            {
                float y = -(70f + i * 40f);
                float width = (i & 1) == 0 ? 22f : 14f;
                Bar("Tick" + i, group, new Vector2(0f, y), new Vector2(width, thin), Ink);
            }
            Bar("Centre", group, Vector2.zero, new Vector2(3.5f, 3.5f), ReticleRed);

            // On the lens edge, upper right (45 degrees): clear of the HUD weapon panel and the compass.
            float edge = half * 0.70710678f - 46f;
            var label = UiKit.Rect("Zoom", group, centre, centre, new Vector2(edge, edge), new Vector2(120f, 40f));
            _zoomLabel = UiKit.Label(label, "", 28, UiKit.TextDim, TextAnchor.MiddleCenter, FontStyle.Bold);
        }

        static void Panel(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            UiKit.Box(rect, Color.black);
        }

        // A segment on one axis from `from` to `to` metres-of-reference-pixels away from the centre.
        static void Segment(Transform parent, string name, bool horizontal, float sign, float from, float to, float thickness)
        {
            float length = to - from;
            float middle = (from + to) * 0.5f * sign;
            Bar(name, parent, horizontal ? new Vector2(middle, 0f) : new Vector2(0f, middle),
                horizontal ? new Vector2(length, thickness) : new Vector2(thickness, length), Ink);
        }

        static void Bar(string name, Transform parent, Vector2 position, Vector2 size, Color color)
        {
            var centre = new Vector2(0.5f, 0.5f);
            UiKit.Box(UiKit.Rect(name, parent, centre, centre, position, size), color);
        }

        static void Picture(RectTransform rect, Sprite sprite, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
        }

        static Sprite MakeSprite(Texture2D texture)
        {
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        // ---- Textures: white with alpha, tinted by the Image colour. Built once, here, with no asset files. ----

        static Texture2D NewTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        // Coverage of a pixel from a signed distance in pixels (negative inside).
        static float Cover(float distance) => Mathf.Clamp01(0.5f - distance);

        static float BoxDistance(float x, float y, float cx, float cy, float hx, float hy)
        {
            float qx = Mathf.Abs(x - cx) - hx, qy = Mathf.Abs(y - cy) - hy;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f);
        }

        // Clear inside the circle with a soft dark rim, opaque outside it.
        static Texture2D LensTexture(int size)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float centre = (size - 1) * 0.5f, radius = size * 0.5f - 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius * 0.8f, radius, r)) * 0.5f;
                    float alpha = Mathf.Max(rim, Mathf.Clamp01(r - radius + 0.5f));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // A small bright core with a soft glow.
        static Texture2D DotTexture(int size)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float centre = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x - centre) * (x - centre) + (y - centre) * (y - centre));
                    float glow = Mathf.Pow(1f - Mathf.Clamp01(r / 14f), 2f) * 0.4f;
                    float alpha = Mathf.Max(glow, Cover(r - 2.5f));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // A ring with a dot in the middle and four short tick marks outside it.
        static Texture2D HoloTexture(int size)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float centre = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - centre, dy = y - centre;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float d = Mathf.Min(Mathf.Abs(r - 44f) - 1.2f, r - 2.4f);
                    d = Mathf.Min(d, BoxDistance(dx, dy, 55f, 0f, 4f, 1.1f));
                    d = Mathf.Min(d, BoxDistance(dx, dy, -55f, 0f, 4f, 1.1f));
                    d = Mathf.Min(d, BoxDistance(dx, dy, 0f, 55f, 1.1f, 4f));
                    d = Mathf.Min(d, BoxDistance(dx, dy, 0f, -55f, 1.1f, 4f));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Cover(d) * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
