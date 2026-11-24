using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The local player's breath while swimming (G12, ADR-112): a bar low in the middle once the breath is not full,
    // red while drowning, and a blue wash over the view while the head is under water.
    public class SwimHud : MonoBehaviour
    {
        CanvasGroup _group;
        RectTransform _bar;
        Image _fill, _wash;
        CombatantSwim _swim;
        Combatant _owner;

        public static bool BarShown { get; private set; }
        public static bool WashShown { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { BarShown = WashShown = false; }

        void Awake()
        {
            var canvas = UiKit.Canvas("SwimHud", 9, false);
            canvas.transform.SetParent(transform, false);
            var root = UiKit.Fill("Root", canvas.transform);
            _group = root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            _wash = UiKit.Box(UiKit.Fill("Wash", root), new Color(0.05f, 0.25f, 0.4f, 0.45f));
            _wash.enabled = false;
            var centre = new Vector2(0.5f, 0.5f);
            _bar = UiKit.Rect("Breath", root, new Vector2(0.5f, 0.18f), centre, Vector2.zero, new Vector2(260f, 8f));
            UiKit.Box(_bar, new Color(0f, 0f, 0f, 0.55f));
            _fill = UiKit.Box(UiKit.Fill("Fill", _bar), UiKit.Steel);
            _bar.gameObject.SetActive(false);
        }

        void OnDestroy() => ResetStatics();

        void Update()
        {
            var me = Combatant.LocalPlayer;
            if (me == null) return;
            if (_owner != me) { _owner = me; _swim = me.GetComponent<CombatantSwim>(); }
            if (_swim == null) return;
            bool bar = _swim.InWater && _swim.Breath < 0.999f;
            // HUD v2 (ADR-563): the breath shows as the lungs ring in the vitals block; this centre bar stays hidden, BarShown keeps its meaning.
            BarShown = bar;
            bool wash = _swim.HeadUnder && !me.Dead.Value;
            if (_wash.enabled != wash) _wash.enabled = wash;
            WashShown = wash;
            if (!bar) return;
            _fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(_swim.Breath), 1f);
            _fill.color = _swim.Breath <= 0f ? UiKit.Danger : _swim.HeadUnder ? UiKit.Accent : UiKit.Steel;
        }
    }
}
