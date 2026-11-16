using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Short, quiet UI motion (docs: motion tokens 150/200/300 ms, ease-out in, ease-in out, only opacity and small
    // translations). Everything is skipped when reduced motion is on (PlayerPrefs "ui.reducedMotion" = 1).
    public class UiFx : MonoBehaviour
    {
        public const float Fast = 0.15f, Normal = 0.2f, Slow = 0.3f;
        public const string ReducedKey = "ui.reducedMotion";
        public static bool ReducedMotion => PlayerPrefs.GetInt(ReducedKey, 0) == 1;

        struct Tween
        {
            public CanvasGroup Group; public RectTransform Slide; public GameObject Disable;
            public float T, Duration, From, To; public Vector2 BasePos; public float SlidePx; public bool Out;
        }

        static UiFx _runner;
        static readonly List<Tween> Tweens = new List<Tween>();

        static UiFx Runner()
        {
            if (_runner == null) _runner = new GameObject("UiFx").AddComponent<UiFx>();
            return _runner;
        }

        static CanvasGroup Group(GameObject go) => go.TryGetComponent<CanvasGroup>(out var g) ? g : go.AddComponent<CanvasGroup>();

        static void Remove(CanvasGroup g)
        {
            for (int i = Tweens.Count - 1; i >= 0; i--) if (Tweens[i].Group == g) Tweens.RemoveAt(i);
        }

        // Shows `go` with a fade in (and, if given, a rise of `slidePx` stage pixels of `slide`).
        public static void Show(GameObject go, float duration = Normal, RectTransform slide = null, float slidePx = 0f)
        {
            go.SetActive(true);
            var g = Group(go);
            Remove(g);
            if (ReducedMotion || duration <= 0f) { g.alpha = 1f; return; }
            g.alpha = 0f;
            Runner();
            var tw = new Tween { Group = g, Slide = slide, T = 0f, Duration = duration, From = 0f, To = 1f, SlidePx = slidePx };
            if (slide != null) { tw.BasePos = slide.anchoredPosition; slide.anchoredPosition = tw.BasePos + new Vector2(0f, -slidePx); }
            Tweens.Add(tw);
        }

        // Fades `go` out and then deactivates it.
        public static void Hide(GameObject go, float duration = Fast)
        {
            var g = Group(go);
            Remove(g);
            if (ReducedMotion || duration <= 0f || !go.activeInHierarchy) { go.SetActive(false); return; }
            Runner();
            Tweens.Add(new Tween { Group = g, Disable = go, T = 0f, Duration = duration, From = g.alpha, To = 0f, Out = true });
        }

        // Changes a text with a short fade up so numbers and phrases swap without a jump.
        public static void Swap(Text text, string value)
        {
            if (text == null || text.text == value) return;
            text.text = value;
            if (ReducedMotion || !text.gameObject.activeInHierarchy) return;
            var g = Group(text.gameObject);
            Remove(g);
            g.alpha = 0.15f;
            Runner();
            Tweens.Add(new Tween { Group = g, T = 0f, Duration = Fast, From = 0.15f, To = 1f });
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = Tweens.Count - 1; i >= 0; i--)
            {
                var tw = Tweens[i];
                if (tw.Group == null) { Tweens.RemoveAt(i); continue; }
                tw.T += dt;
                float k = Mathf.Clamp01(tw.T / tw.Duration);
                float e = tw.Out ? k * k : 1f - (1f - k) * (1f - k) * (1f - k); // ease-in out, ease-out in
                tw.Group.alpha = Mathf.Lerp(tw.From, tw.To, e);
                if (tw.Slide != null) tw.Slide.anchoredPosition = tw.BasePos + new Vector2(0f, -tw.SlidePx * (1f - e));
                if (k >= 1f)
                {
                    if (tw.Disable != null) tw.Disable.SetActive(false);
                    Tweens.RemoveAt(i);
                }
                else Tweens[i] = tw;
            }
        }

        void OnDestroy() { if (_runner == this) { _runner = null; Tweens.Clear(); } }
    }
}
