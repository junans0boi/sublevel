using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Kill feed, top left: the latest kills, newest on top, each fading out after a few seconds.
    // It only shows what MatchState received from the server. Texts are written when a kill arrives, never per frame.
    public class KillFeed : MonoBehaviour
    {
        public const int Rows = 5;
        public const float ShowSeconds = 6f;

        const int SortOrder = 1; // just above the HUD
        const float FadeSeconds = 1f;
        const float Margin = 36f, Top = 24f;
        const float RowWidth = 520f, RowHeight = 34f, RowGap = 4f;

        public static KillFeed Instance { get; private set; }

        readonly GameObject[] _row = new GameObject[Rows];
        readonly CanvasGroup[] _group = new CanvasGroup[Rows];
        readonly Text[] _label = new Text[Rows];
        readonly string[] _text = new string[Rows];
        readonly Color[] _color = new Color[Rows];
        readonly float[] _until = new float[Rows];
        int _shown;

        // ---- Read-only state for tests ----

        public int VisibleCount => _shown;
        // Row 0 is the newest. "" for a row that shows nothing.
        public string TextAt(int row) => row >= 0 && row < _shown ? _text[row] : "";

        // Installs itself next to every match; nothing in the scene or the player prefab refers to it.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            MatchState.Started -= Install;
            MatchState.Started += Install;
        }

        static void Install(MatchState state)
        {
            if (Instance != null || state == null) return;
            var go = new GameObject("KillFeed");
            // Under the match object so it goes away with the scene.
            go.transform.SetParent(state.transform, false);
            go.AddComponent<KillFeed>();
        }

        // "killer [weapon] victim"; a death nobody caused names only the victim.
        public static string Format(in KillEntry entry)
        {
            bool hasKiller = entry.KillerId >= 0 && !string.IsNullOrEmpty(entry.KillerName);
            string tag = Tag(in entry, hasKiller);
            return hasKiller
                ? entry.KillerName + "  [" + tag + "]  " + entry.VictimName
                : entry.VictimName + "  [" + tag + "]";
        }

        static string Tag(in KillEntry entry, bool hasKiller)
        {
            switch ((DamageCause)entry.Cause)
            {
                case DamageCause.Fall: return "낙하";
                case DamageCause.Blast: return "폭발";
                case DamageCause.Zone: return "방사능";
            }
            if (!hasKiller) return "탈락";
            var weapon = GameData.Weapons.Find(entry.WeaponId);
            string name = weapon != null ? weapon.Name : "사격";
            return entry.Headshot ? name + " · 헤드샷" : name;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            Build();
            MatchState.KillPosted += Post;
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            MatchState.KillPosted -= Post;
            Instance = null;
        }

        void Post(KillEntry entry)
        {
            // Scene unloads can deliver a last kill after the canvas is gone.
            if (_row[0] == null) return;
            for (int i = Rows - 1; i > 0; i--)
            {
                _text[i] = _text[i - 1];
                _color[i] = _color[i - 1];
                _until[i] = _until[i - 1];
            }
            var me = Combatant.LocalPlayer;
            int myId = me != null ? me.ObjectId : int.MinValue;
            _text[0] = Format(in entry);
            // The local player's own lines stand out; the names already say who is who.
            _color[0] = entry.VictimId == myId ? UiKit.Danger : entry.KillerId == myId ? UiKit.Accent : UiKit.Text;
            _until[0] = Time.time + ShowSeconds;
            if (_shown < Rows) _shown++;
            for (int i = 0; i < _shown; i++)
            {
                _label[i].text = _text[i];
                _label[i].color = _color[i];
            }
            Tick();
        }

        void Update()
        {
            if (_shown > 0) Tick();
        }

        // Rows expire oldest first, so the shown ones are always rows 0.._shown-1.
        void Tick()
        {
            float now = Time.time;
            while (_shown > 0 && _until[_shown - 1] <= now) _shown--;
            for (int i = 0; i < Rows; i++)
            {
                bool on = i < _shown;
                if (_row[i].activeSelf != on) _row[i].SetActive(on);
                if (!on) continue;
                float alpha = Mathf.Clamp01((_until[i] - now) / FadeSeconds);
                if (_group[i].alpha != alpha) _group[i].alpha = alpha;
            }
        }

        void Build()
        {
            var canvas = UiKit.Canvas("KillFeedCanvas", SortOrder, false);
            canvas.transform.SetParent(transform, false);
            var topLeft = new Vector2(0f, 1f);
            for (int i = 0; i < Rows; i++)
            {
                var row = UiKit.Rect("Row" + i, canvas.transform, topLeft, topLeft,
                    new Vector2(Margin, -Top - i * (RowHeight + RowGap)), new Vector2(RowWidth, RowHeight));
                UiKit.Box(row, UiKit.Panel);
                _group[i] = row.gameObject.AddComponent<CanvasGroup>();
                _group[i].interactable = false;
                _group[i].blocksRaycasts = false;
                var text = UiKit.Fill("Text", row);
                text.offsetMin = new Vector2(14f, 0f);
                text.offsetMax = new Vector2(-14f, 0f);
                _label[i] = UiKit.Label(text, "", 20, UiKit.Text);
                _text[i] = "";
                _color[i] = UiKit.Text;
                _row[i] = row.gameObject;
                _row[i].SetActive(false);
            }
        }
    }
}
