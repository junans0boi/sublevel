using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // The event race on the screen (ADR-519, #445): the course name while waiting, five start lights that come on one by one with the big
    // countdown number, then GO. It reads Net's RaceHud (what the server said); it decides nothing. Put into any scene that has a
    // RaceDirector.
    public sealed class RaceHudView : MonoBehaviour
    {
        Text _top, _big;
        readonly Image[] _lights = new Image[5];
        static readonly Color LightOff = new Color(0.13f, 0.13f, 0.15f, 0.95f), LightOn = new Color(1f, 0.12f, 0.08f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            OnLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            if (Object.FindAnyObjectByType<RaceDirector>() == null || Object.FindAnyObjectByType<RaceHudView>() != null) return;
            new GameObject("RaceHudView").AddComponent<RaceHudView>();
        }

        void Awake()
        {
            var canvas = UiKit.Canvas("RaceHudCanvas", 40, false);
            canvas.transform.SetParent(transform, false);
            var root = (RectTransform)canvas.transform;
            _top = UiKit.Label(UiKit.Rect("Top", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -30f), new Vector2(1400f, 50f)),
                "", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            _big = UiKit.Label(UiKit.Rect("Big", root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 80f), new Vector2(800f, 220f)),
                "", 180, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            for (int i = 0; i < 5; i++)
            {
                var r = UiKit.Rect("Light" + i, root, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(-120f + i * 60f, -150f), new Vector2(48f, 48f));
                _lights[i] = r.gameObject.AddComponent<Image>();
                _lights[i].raycastTarget = false;
            }
        }

        void Update()
        {
            var director = RaceDirector.Current;
            var phase = RaceHud.Phase;
            bool counting = phase == RacePhase.Countdown, waiting = phase == RacePhase.Waiting && director != null;
            _top.text = waiting ? $"{director.Info.Name}  ·  다른 참가자를 기다리는 중" : "";
            int lit = counting ? Mathf.Clamp(5 - Mathf.CeilToInt(RaceHud.CountdownNow / RaceEvent.CountdownSeconds * 5f), 0, 5) : 0;
            for (int i = 0; i < 5; i++)
            {
                _lights[i].enabled = counting || waiting;
                _lights[i].color = i < lit ? LightOn : LightOff;
            }
            if (counting) { _big.text = Mathf.Max(1, Mathf.CeilToInt(RaceHud.CountdownNow)).ToString(); _big.color = new Color(1f, 0.85f, 0.2f); }
            else if (RaceHud.GoShowing) { _big.text = "GO!"; _big.color = new Color(0.4f, 1f, 0.45f); }
            else _big.text = "";
        }
    }
}
