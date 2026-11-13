using System;
using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Game.Gameplay
{
    // What the local player sees through the optic on the weapon in hand while looking down the sights (a tap on
    // aim; ADR-045, ADR-056, ADR-058, ADR-060). Shoulder (hold) aim and the hip show nothing: no magnification.
    //  - 1x sights ("반사 조준 사이트", "투영 조준 사이트"): the sight model on the weapon frames the middle of the screen
    //    (OpticVisual) and the reticle is drawn here: an amber-red dot with a short tick below, or four broken arc
    //    pieces round a dot. No zoom; everything outside the window stays in view.
    //  - Scopes (2x and up): the WHOLE view zooms (camera field of view from Optics.FovFor). A 3D eyepiece housing
    //    (OpticMeshes.Eyepiece, dark metal lit by the scene) is drawn round the lens by an overlay camera stacked on
    //    the main camera (URP camera stacking, layer HousingLayer), so it never pokes into walls; without stacking it
    //    falls back to a child of the main camera scaled about the eye. Over it, a screen overlay: the zoomed scene
    //    outside the lens darkened by a radial shade, a faint tint and edge shadow in the lens, the reticle for the
    //    magnification, the magnification inside the lens rim, "영점 100m" and the ammo count under the lens.
    //    While scoped the HUD hides its bottom weapon panel (HidesHudWeapon).
    // Every size follows the screen height (OpticLook fractions; canvas scaled against 1080 tall).
    // It also publishes what the rest of the game follows: Active (the HUD hides its crosshair), FovOverride (the
    // camera's field of view), SensitivityFactor (mouse look), HidesWeapon (the weapon at the eye is not drawn).
    // Created by PlayerController next to the other local screens.
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
        // True while a magnified scope is up: the HUD hides its bottom weapon panel (ammo shows under the lens).
        public static bool HidesHudWeapon { get; private set; }
        public static ScopeView Instance { get; private set; }
        // The zoom of the optic on the weapon in hand right now (variable scopes: set with the mouse wheel while in
        // sights, ADR-063); 1 when there is no magnified optic. Kept while leaving and re-entering sights; back to the
        // optic's highest zoom when the weapon in hand or its optic changes.
        public static float CurrentZoom { get; private set; } = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Active = false;
            SensitivityFactor = 1f;
            FovOverride = 0f;
            HidesWeapon = false;
            HidesHudWeapon = false;
            CurrentZoom = 1f;
        }

        // Unnamed layer (ProjectSettings/TagManager has no free named one) that only the housing camera draws.
        public const int HousingLayer = 30;
        public const string ZeroText = "영점 100m";

        const float ReferenceHeight = 1080f;
        const float FadePerSecond = 8f;
        // The housing comes up to the eye from this far in front as the scope fades in (metres, housing space).
        const float RiseDistance = 0.05f;
        // The shade picture is a square this many reference pixels from its centre to each side (covers 21:9).
        const float ShadeRadius = 1.7f * ReferenceHeight;
        const float LensEdgeOverscan = 1.06f;
        // Canvas units at the bottom of the screen kept for the HUD's health bar.
        const float BottomHudReserve = 95f;
        const int ReticleTextureSize = 512, ShadeTextureSize = 256, EdgeTextureSize = 256;

        static readonly Color SightRed = new Color(1f, 0.30f, 0.12f, 1f);
        static readonly Color ProjectorRed = new Color(1f, 0.42f, 0.14f, 1f);
        static readonly Color LitAmber = new Color(1f, 0.64f, 0.16f, 1f);
        static readonly Color Ink = new Color(0.02f, 0.02f, 0.02f, 0.95f);
        static readonly Color ShadeColor = new Color(0.015f, 0.017f, 0.02f, 1f);
        static readonly Color LensTint = new Color(0.01f, 0.025f, 0.03f, 1f);

        // Read by tests and the HUD.
        public float ScopeBlend { get; private set; }
        public float ReticleBlend { get; private set; }
        // The eyepiece housing (null until a scope was first raised) and the camera that draws it (null when stacking
        // was not possible and the housing hangs under the main camera instead).
        public GameObject Housing => _housing;
        public Camera HousingCamera => _stacked ? _housingCamera : null;
        // Lens window radius in canvas units (1080 tall) of the scope last shown.
        public float LensRadiusUnits { get; private set; }

        Canvas _canvas;
        CanvasGroup _scopeGroup, _reticleGroup;
        GameObject _reflexReticle, _projectorReticle;
        RectTransform _shade, _lensEdge, _ink, _lit, _zoomRect, _zeroRect, _ammoRect;
        Image _shadeImage, _inkImage, _litImage;
        Text _zoomLabel, _ammoLabel;
        readonly List<Object> _owned = new List<Object>();
        readonly Dictionary<int, Sprite> _shadeSprites = new Dictionary<int, Sprite>();
        readonly Sprite[] _inkSprites = new Sprite[4], _litSprites = new Sprite[4];
        readonly bool[] _reticleBuilt = new bool[4];

        Combatant _combatantOf;
        CombatantOptics _optics;
        int _shownOptic = -1;
        float _shownZoom, _labelZoom;
        // What the zoom belongs to: the weapon in hand, its slot and its optic. A change resets the zoom.
        int _zoomWeapon = -1, _zoomSlot = -1, _zoomOptic = -1;
        float _lastFactor = 1f;
        int _shownMag = int.MinValue, _shownReserve = int.MinValue;

        Camera _housingMain, _housingCamera;
        GameObject _housing;
        MeshFilter _housingFilter;
        bool _stacked, _housingWarned;

        void Awake()
        {
            Instance = this;
            _canvas = UiKit.Canvas("ScopeCanvas", -1, false);
            _canvas.transform.SetParent(transform, false);
            BuildScope(_canvas.transform);
            BuildReticles(_canvas.transform);
            _canvas.enabled = false;
        }

        void OnDestroy()
        {
            ResetStatics();
            if (Instance == this) Instance = null;
            DestroyHousing();
            foreach (var o in _owned)
                if (o != null) Destroy(o);
            _owned.Clear();
        }

        void Update()
        {
            TrackZoomOwner();
            var def = CurrentOptic();
            bool scoped = def != null && def.Kind == OpticKind.Scope && def.Zoom > 1f;
            bool reticle = def != null && !scoped;

            // The mouse wheel turns a variable scope while looking through it (read from the player's last input:
            // this runs before PlayerController, so every frame's wheel is seen exactly once).
            if (scoped && Optics.IsVariable(def))
            {
                var pc = PlayerController.Local;
                int notches = pc != null ? pc.LastInput.ZoomSteps : 0;
                if (notches != 0) CurrentZoom = Optics.StepZoom(def, CurrentZoom, notches);
            }

            if (def != null)
            {
                _lastFactor = Optics.SensitivityAt(def, scoped ? CurrentZoom : def.Zoom);
                if (def.Id != _shownOptic) ShowOptic(def);
                if (scoped && CurrentZoom != _labelZoom)
                {
                    _labelZoom = CurrentZoom;
                    _zoomLabel.text = Optics.ZoomText(CurrentZoom);
                }
            }

            float step = FadePerSecond * Time.unscaledDeltaTime;
            ScopeBlend = Mathf.MoveTowards(ScopeBlend, scoped ? 1f : 0f, step);
            ReticleBlend = Mathf.MoveTowards(ReticleBlend, reticle ? 1f : 0f, step);
            if (ScopeBlend <= 0f && !scoped) _lastFactor = 1f;

            // The view follows the target at once: the camera eases its own field of view.
            FovOverride = scoped ? Optics.FovFor(GameData.Rules.Aim.Sights.Fov, CurrentZoom) : 0f;
            SensitivityFactor = Mathf.Lerp(1f, _lastFactor, ScopeBlend);
            Active = def != null || ScopeBlend > 0f || ReticleBlend > 0f;
            HidesWeapon = ScopeBlend >= 0.5f;
            HidesHudWeapon = ScopeBlend >= 0.5f;

            UpdateHousing();

            bool draw = ScopeBlend > 0f || ReticleBlend > 0f;
            if (_canvas.enabled != draw) _canvas.enabled = draw;
            if (!draw) return;
            if (_scopeGroup.alpha != ScopeBlend) _scopeGroup.alpha = ScopeBlend;
            if (_reticleGroup.alpha != ReticleBlend) _reticleGroup.alpha = ReticleBlend;
            if (ScopeBlend > 0f) TickAmmo();
        }

        // Resets the zoom to the optic's highest whenever the weapon in hand, its slot or its optic changes.
        void TrackZoomOwner()
        {
            var me = Combatant.LocalPlayer;
            int weapon = me != null ? me.ActiveWeaponId : 0;
            int slot = me != null ? me.ActiveSlot.Value : -1;
            if (me != null && _combatantOf != me)
            {
                _combatantOf = me;
                _optics = me.GetComponent<CombatantOptics>();
            }
            int optic = me != null && _optics != null && weapon != 0 ? _optics.ActiveOptic : 0;
            if (weapon == _zoomWeapon && slot == _zoomSlot && optic == _zoomOptic) return;
            _zoomWeapon = weapon;
            _zoomSlot = slot;
            _zoomOptic = optic;
            CurrentZoom = optic != 0 ? Optics.ZoomOf(optic) : 1f;
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
            if (def.Kind != OpticKind.Scope || !(def.Zoom > 1f))
            {
                bool projector = def.Kind == OpticKind.Holo || def.Reticle == ReticleStyle.Ring;
                _reflexReticle.SetActive(!projector);
                _projectorReticle.SetActive(projector);
                return;
            }

            _shownZoom = def.Zoom;
            var look = OpticLook.For(def.Zoom);
            float r = look.LensRadius * ReferenceHeight;
            LensRadiusUnits = r;
            _shadeImage.sprite = ShadeSprite(look);
            _lensEdge.sizeDelta = Vector2.one * (2f * r * LensEdgeOverscan);
            int style = (int)look.Reticle;
            EnsureReticle(look.Reticle);
            _inkImage.sprite = _inkSprites[style];
            _ink.sizeDelta = Vector2.one * (2f * r);
            bool lit = _litSprites[style] != null;
            _litImage.sprite = _litSprites[style];
            _lit.gameObject.SetActive(lit);
            _lit.sizeDelta = Vector2.one * (2f * r);
            // The magnification sits inside the lens rim, lower right; zero and ammo hang under the lens.
            _labelZoom = CurrentZoom;
            _zoomLabel.text = Optics.ZoomText(CurrentZoom);
            _zoomRect.anchoredPosition = new Vector2(r * 0.66f, -r * 0.66f);
            // Under the lens while there is room above the HUD's health bar; inside the lens bottom (on its dark edge)
            // for the big lenses (8x, 15x), so they never sit on the bar.
            bool below = r + 60f + 15f <= ReferenceHeight * 0.5f - BottomHudReserve;
            _zeroRect.anchoredPosition = new Vector2(0f, below ? -r - 30f : -r + 66f);
            _ammoRect.anchoredPosition = new Vector2(0f, below ? -r - 60f : -r + 38f);
            if (_housingFilter != null) _housingFilter.sharedMesh = OpticMeshes.Eyepiece(def.Zoom);
        }

        void TickAmmo()
        {
            var me = Combatant.LocalPlayer;
            int id = HudBackend.ActiveWeaponId(me);
            var weapon = id != 0 ? GameData.Weapons.Find(id) : null;
            int mag = weapon != null ? HudBackend.ActiveMag(me) : 0;
            int reserve = HudBackend.Reserve(me, weapon);
            if (mag == _shownMag && reserve == _shownReserve) return;
            _shownMag = mag;
            _shownReserve = reserve;
            _ammoLabel.text = weapon != null ? mag + " / " + reserve : "";
        }

        // ---- The 3D eyepiece housing ----

        void UpdateHousing()
        {
            bool show = ScopeBlend > 0f && _shownZoom > 1f && EnsureHousing();
            if (_housing != null && _housing.activeSelf != show) _housing.SetActive(show);
            if (_stacked && _housingCamera != null && _housingCamera.enabled != show) _housingCamera.enabled = show;
            if (!show) return;
            float z = OpticLook.RimDistance + (1f - ScopeBlend) * RiseDistance;
            var t = _housing.transform;
            if (_stacked)
            {
                t.localPosition = new Vector3(0f, 0f, z);
                return;
            }
            // Fallback in the main camera's own view: scaling about the eye keeps every angle, so the housing covers
            // the same part of the screen at the zoomed field of view as it would at the housing camera's.
            float k = Mathf.Tan(OpticLook.HousingFovDeg * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(Mathf.Max(0.5f, _housingMain.fieldOfView) * 0.5f * Mathf.Deg2Rad);
            t.localPosition = new Vector3(0f, 0f, z * k);
            t.localScale = Vector3.one * k;
        }

        bool EnsureHousing()
        {
            var pc = PlayerController.Local;
            var main = pc != null ? pc.Camera : null;
            if (main == null) return false;
            if (_housing != null && _housingMain == main) return true;
            DestroyHousing();

            var me = Combatant.LocalPlayer;
            var visual = me != null ? me.GetComponent<OpticVisual>() : null;
            var materials = visual != null ? visual.Materials : null;
            if (materials == null || materials[0] == null)
            {
                if (!_housingWarned) Debug.LogWarning("ScopeView: the player's OpticVisual has no optic materials; the scope eyepiece is not drawn");
                _housingWarned = true;
                return false;
            }
            _housingMain = main;

            var camGo = new GameObject("ScopeHousingCamera");
            camGo.transform.SetParent(main.transform, false);
            _housingCamera = camGo.AddComponent<Camera>();
            _housingCamera.enabled = false;
            _housingCamera.clearFlags = CameraClearFlags.Depth;
            _housingCamera.cullingMask = 1 << HousingLayer;
            _housingCamera.fieldOfView = OpticLook.HousingFovDeg;
            _housingCamera.nearClipPlane = 0.01f;
            _housingCamera.farClipPlane = 1f;
            _housingCamera.useOcclusionCulling = false;
            _housingCamera.allowHDR = main.allowHDR;
            _housingCamera.allowMSAA = main.allowMSAA;
            _stacked = TryStack(main, _housingCamera);
            if (!_stacked)
            {
                Destroy(camGo);
                _housingCamera = null;
            }

            _housing = new GameObject("ScopeHousing");
            _housing.layer = _stacked ? HousingLayer : main.gameObject.layer;
            _housing.transform.SetParent(_stacked ? camGo.transform : main.transform, false);
            _housingFilter = _housing.AddComponent<MeshFilter>();
            if (_shownZoom > 1f) _housingFilter.sharedMesh = OpticMeshes.Eyepiece(_shownZoom);
            var r = _housing.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _housing.SetActive(false);
            return true;
        }

        // The housing camera becomes an Overlay camera in the main camera's stack; the main camera stops drawing
        // the housing layer. False when the main camera's renderer cannot stack.
        static bool TryStack(Camera main, Camera overlay)
        {
            var baseData = main.GetUniversalAdditionalCameraData();
            if (baseData == null || baseData.renderType != CameraRenderType.Base) return false;
            var data = overlay.GetUniversalAdditionalCameraData();
            if (data == null) return false;
            data.renderType = CameraRenderType.Overlay;
            data.renderShadows = false;
            data.renderPostProcessing = false;
            var stack = baseData.cameraStack;
            if (stack == null) return false;
            if (!stack.Contains(overlay)) stack.Add(overlay);
            main.cullingMask &= ~(1 << HousingLayer);
            return true;
        }

        void DestroyHousing()
        {
            if (_stacked && _housingMain != null && _housingCamera != null)
            {
                var baseData = _housingMain.GetUniversalAdditionalCameraData();
                if (baseData != null && baseData.renderType == CameraRenderType.Base) baseData.cameraStack?.Remove(_housingCamera);
            }
            if (_housingCamera != null) Destroy(_housingCamera.gameObject);
            if (_housing != null) Destroy(_housing);
            _housingCamera = null;
            _housing = null;
            _housingFilter = null;
            _housingMain = null;
            _stacked = false;
        }

        // ---- Building the overlay ----

        void BuildScope(Transform root)
        {
            var group = UiKit.Fill("Scope", root);
            _scopeGroup = group.gameObject.AddComponent<CanvasGroup>();
            _scopeGroup.blocksRaycasts = false;
            _scopeGroup.interactable = false;
            _scopeGroup.alpha = 0f;

            _shade = Centred("Shade", group, 2f * ShadeRadius);
            _shadeImage = Picture(_shade, null, ShadeColor);
            _lensEdge = Centred("LensEdge", group, 100f);
            Picture(_lensEdge, Own(MakeSprite(Own(EdgeTexture(EdgeTextureSize)))), LensTint);
            _ink = Centred("ReticleInk", group, 100f);
            _inkImage = Picture(_ink, null, Ink);
            _lit = Centred("ReticleLit", group, 100f);
            _litImage = Picture(_lit, null, LitAmber);

            var centre = new Vector2(0.5f, 0.5f);
            _zoomRect = UiKit.Rect("Zoom", group, centre, centre, Vector2.zero, new Vector2(90f, 34f));
            _zoomLabel = UiKit.Label(_zoomRect, "", 22, new Color(0.92f, 0.92f, 0.9f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Bold);
            Shadowed(_zoomLabel);
            _zeroRect = UiKit.Rect("Zero", group, centre, centre, Vector2.zero, new Vector2(200f, 26f));
            Shadowed(UiKit.Label(_zeroRect, ZeroText, 17, UiKit.TextDim, TextAnchor.MiddleCenter));
            _ammoRect = UiKit.Rect("Ammo", group, centre, centre, Vector2.zero, new Vector2(200f, 30f));
            _ammoLabel = UiKit.Label(_ammoRect, "", 22, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Shadowed(_ammoLabel);
        }

        void BuildReticles(Transform root)
        {
            var group = UiKit.Fill("Reticle", root);
            _reticleGroup = group.gameObject.AddComponent<CanvasGroup>();
            _reticleGroup.blocksRaycasts = false;
            _reticleGroup.interactable = false;
            _reticleGroup.alpha = 0f;

            // Reflex sight: a small bright dot with a short vertical tick under it and a faint glow. 64 units across,
            // 2 pixels per unit.
            var reflex = Centred("Reflex", group, 64f);
            Picture(reflex, Own(MakeSprite(Own(Paint(128, 32f, ReflexShape, 1.2f, 0.6f)))), SightRed);
            _reflexReticle = reflex.gameObject;

            // Projector sight: four broken arc pieces round a centre dot. 96 units across.
            var projector = Centred("Projector", group, 96f);
            Picture(projector, Own(MakeSprite(Own(Paint(192, 48f, ProjectorShape, 1.5f, 0.5f)))), ProjectorRed);
            _projectorReticle = projector.gameObject;
            _projectorReticle.SetActive(false);
        }

        static RectTransform Centred(string name, Transform parent, float size)
        {
            var centre = new Vector2(0.5f, 0.5f);
            return UiKit.Rect(name, parent, centre, centre, Vector2.zero, new Vector2(size, size));
        }

        static Image Picture(RectTransform rect, Sprite sprite, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void Shadowed(Text label)
        {
            var shadow = label.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        T Own<T>(T o) where T : Object
        {
            _owned.Add(o);
            return o;
        }

        static Sprite MakeSprite(Texture2D texture)
        {
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        Sprite ShadeSprite(ScopeLook look)
        {
            int key = Mathf.RoundToInt(look.LensRadius * 1000f) * 1000 + Mathf.RoundToInt(look.OuterRadius * 1000f);
            if (_shadeSprites.TryGetValue(key, out var sprite) && sprite != null) return sprite;
            sprite = Own(MakeSprite(Own(ShadeTexture(ShadeTextureSize, look))));
            _shadeSprites[key] = sprite;
            return sprite;
        }

        void EnsureReticle(ScopeReticle style)
        {
            int i = (int)style;
            if (_reticleBuilt[i]) return;
            _reticleBuilt[i] = true;
            const float px = 2f / ReticleTextureSize;
            switch (style)
            {
                case ScopeReticle.Arc:
                    _inkSprites[i] = Own(MakeSprite(Own(Paint(ReticleTextureSize, 1f, ArcInk, px, 0f))));
                    _litSprites[i] = Own(MakeSprite(Own(Paint(ReticleTextureSize, 1f, ArcLit, px, 0.3f))));
                    break;
                case ScopeReticle.Post:
                    _inkSprites[i] = Own(MakeSprite(Own(Paint(ReticleTextureSize, 1f, PostInk, px, 0f))));
                    _litSprites[i] = Own(MakeSprite(Own(Paint(ReticleTextureSize, 1f, PostLit, px, 0.3f))));
                    break;
                case ScopeReticle.Fine:
                    _inkSprites[i] = Own(MakeSprite(Own(Paint(ReticleTextureSize, 1f, FineInk, px, 0f))));
                    break;
                default:
                    _inkSprites[i] = Own(MakeSprite(Own(Paint(ReticleTextureSize, 1f, HoldoverInk, px, 0f))));
                    break;
            }
        }

        // ---- Shapes: signed distances (negative inside) in the picture's own units ----

        static float Circle(float x, float y, float r) => Mathf.Sqrt(x * x + y * y) - r;

        static float Box(float x, float y, float cx, float cy, float hx, float hy)
        {
            float qx = Mathf.Abs(x - cx) - hx, qy = Mathf.Abs(y - cy) - hy;
            float ox = Mathf.Max(qx, 0f), oy = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f);
        }

        // Units: reference pixels from the centre.
        static float ReflexShape(float x, float y) => Mathf.Min(Circle(x, y, 3.6f), Box(x, y, 0f, -13f, 1.1f, 3.8f));

        static float ProjectorShape(float x, float y)
        {
            float r = Mathf.Sqrt(x * x + y * y);
            float ring = Mathf.Abs(r - 33f) - 1.7f;
            // Gaps 30 degrees wide round each of the four directions: keep the ring only near the diagonals.
            float angle = Mathf.Atan2(Mathf.Abs(y), Mathf.Abs(x)) * Mathf.Rad2Deg; // 0..90 in the quadrant
            float gap = (15f - Mathf.Min(angle, 90f - angle)) * Mathf.Deg2Rad * 30f;    // > 0 inside a gap
            return Mathf.Min(Mathf.Max(ring, gap), Circle(x, y, 3.2f));
        }

        // Units: lens radii from the centre (the lens edge is at 1).
        static float ArcLit(float x, float y) =>
            Mathf.Min(Circle(x, y, 0.017f), Mathf.Max(Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 0.15f) - 0.0065f, -(y + 0.065f)));

        static readonly float[] RangingTicks = { 0.075f, 0.06f, 0.045f, 0.03f };

        static float ArcInk(float x, float y)
        {
            float d = Box(x, y, 0f, -0.43f, 0.0028f, 0.21f);
            for (int i = 0; i < RangingTicks.Length; i++) d = Mathf.Min(d, Box(x, y, 0f, -0.30f - 0.10f * i, RangingTicks[i], 0.0028f));
            d = Mathf.Min(d, Box(x, y, 0.775f, 0f, 0.225f, 0.006f));
            d = Mathf.Min(d, Box(x, y, -0.775f, 0f, 0.225f, 0.006f));
            return Mathf.Min(d, Box(x, y, 0f, 0.775f, 0.006f, 0.225f));
        }

        static float PostLit(float x, float y) => Circle(x, y, 0.016f);

        static float PostInk(float x, float y)
        {
            float d = Mathf.Min(Box(x, y, 0.55f, 0f, 0.45f, 0.0035f), Box(x, y, -0.55f, 0f, 0.45f, 0.0035f));
            d = Mathf.Min(d, Box(x, y, 0f, 0.55f, 0.0035f, 0.45f));
            // The thick lower post, pointed toward the centre.
            float hw = Mathf.Lerp(0.003f, 0.024f, Mathf.Clamp01((-0.09f - y) / 0.12f));
            float post = Mathf.Max(Mathf.Abs(x) - hw, Mathf.Max(y + 0.09f, -1.05f - y));
            return Mathf.Min(d, post);
        }

        static float FineInk(float x, float y)
        {
            float d = Mathf.Min(Box(x, y, 0f, 0f, 1.05f, 0.0024f), Box(x, y, 0f, 0f, 0.0024f, 1.05f));
            d = Mathf.Min(d, Mathf.Min(Box(x, y, 0.88f, 0f, 0.14f, 0.009f), Box(x, y, -0.88f, 0f, 0.14f, 0.009f)));
            return Mathf.Min(d, Mathf.Min(Box(x, y, 0f, 0.88f, 0.009f, 0.14f), Box(x, y, 0f, -0.88f, 0.009f, 0.14f)));
        }

        static float HoldoverInk(float x, float y)
        {
            float d = Mathf.Min(Box(x, y, 0f, 0f, 1.05f, 0.0022f), Box(x, y, 0f, 0f, 0.0022f, 1.05f));
            d = Mathf.Min(d, Mathf.Min(Box(x, y, 0.89f, 0f, 0.13f, 0.008f), Box(x, y, -0.89f, 0f, 0.13f, 0.008f)));
            d = Mathf.Min(d, Mathf.Min(Box(x, y, 0f, 0.89f, 0.008f, 0.13f), Box(x, y, 0f, -0.89f, 0.008f, 0.13f)));
            // Holdover lines below the centre, a little wider each step down.
            for (int i = 1; i <= 6; i++) d = Mathf.Min(d, Box(x, y, 0f, -0.075f * i, 0.035f + 0.012f * i, 0.0022f));
            return d;
        }

        // ---- Textures: white with alpha, tinted by the Image colour. Built once per look, no asset files. ----

        static Texture2D NewTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        // Paints `shape` on a square picture spanning -extent..extent; `pixel` is one pixel in shape units. A glow of
        // strength `glow` (0 = none) spreads six pixels round lit shapes.
        static Texture2D Paint(int size, float extent, Func<float, float, float> shape, float pixel, float glow)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float glowWidth = pixel * 6f;
            for (int py = 0; py < size; py++)
            {
                float y = ((py + 0.5f) / size * 2f - 1f) * extent;
                for (int px = 0; px < size; px++)
                {
                    float x = ((px + 0.5f) / size * 2f - 1f) * extent;
                    float d = shape(x, y);
                    float alpha = Mathf.Clamp01(0.5f - d / pixel);
                    if (glow > 0f && d > 0f)
                    {
                        float g = 1f - Mathf.Clamp01(d / glowWidth);
                        alpha = Mathf.Max(alpha, glow * g * g);
                    }
                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // The radial shade over the zoomed scene: clear in the lens, a little dark over the eye ring, darker beyond.
        static Texture2D ShadeTexture(int size, ScopeLook look)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float lens = look.LensRadius * ReferenceHeight / ShadeRadius;
            float outer = look.OuterRadius * ReferenceHeight / ShadeRadius;
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (size * 0.5f);
                    float alpha;
                    if (u < lens) alpha = 0f;
                    else if (u < outer) alpha = 0.22f * Mathf.SmoothStep(0f, 1f, (u - lens) / Mathf.Max(1e-4f, outer - lens));
                    else alpha = 0.22f + 0.40f * Mathf.SmoothStep(0f, 1f, (u - outer) / 0.3f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        // The lens: a faint tint, a soft shadow toward the edge, opaque just past it (hides the seam with the housing).
        static Texture2D EdgeTexture(int size)
        {
            var texture = NewTexture(size);
            var pixels = new Color32[size * size];
            float c = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float v = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / (size * 0.5f) * LensEdgeOverscan;
                    float alpha;
                    if (v < 0.80f) alpha = 0.07f;
                    else if (v < 1f) alpha = 0.07f + 0.83f * Mathf.SmoothStep(0f, 1f, (v - 0.80f) / 0.20f);
                    else alpha = v < LensEdgeOverscan ? 0.95f : 0f;
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
