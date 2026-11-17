using System;
using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // G5 / ADR-093: the pickups within reach. Two or more show as a list right of the crosshair; the mouse wheel moves
    // the selection (not while looking down the sights: there it turns a variable scope) and F takes the selected
    // one. A single pickup keeps the HUD's bottom prompt. What F asked for leaves the list at once (and its model
    // is hidden) instead of waiting for the server; refused pickups come back after TakenSeconds.
    public sealed class PickupList : MonoBehaviour
    {
        public const int MaxRows = 8;
        const float TakenSeconds = 1f;
        const float WheelHoldSeconds = 1.5f, FollowMargin = 6f; // after a wheel notch the view leaves the selection alone; rank points one row must lead by
        const float RowWidth = 330f, RowHeight = 40f, RowGap = 3f;
        static readonly Vector2 Offset = new Vector2(70f, 0f); // from the screen centre to the list's left edge

        public static PickupList Instance { get; private set; }
        // The list is on screen (the HUD's single prompt stands down then).
        public static bool Showing => Instance != null && Instance._root != null && Instance._root.activeSelf;
        // The player's F goes to a door this frame (it is nearer the view): the list stands down.
        public static bool DoorFirst;

        readonly List<LootItem> _rows = new List<LootItem>();
        readonly List<LootItem> _nearby = new List<LootItem>();
        readonly Dictionary<LootItem, float> _taken = new Dictionary<LootItem, float>();
        readonly List<LootItem> _expired = new List<LootItem>();
        readonly Dictionary<LootItem, float> _rank = new Dictionary<LootItem, float>(), _bearing = new Dictionary<LootItem, float>();
        float _wheelUntil;
        int _selected = -1;
        bool _visible;

        GameObject _root;
        Row[] _rowViews;
        int _shownHash;

        sealed class Row
        {
            public GameObject Go;
            public Image Background;
            public Image[] Frame;
            public InventoryItemIcon Icon;
            public Text Label;
            public Text Key;
        }

        public IReadOnlyList<LootItem> Rows => _rows;
        public int Selected => _selected;
        public LootItem Current => _selected >= 0 && _selected < _rows.Count ? _rows[_selected] : null;

        // ---- Rules (pure, EditMode tested) ----

        // Brings `rows` up to `nearby`. While the same pickups stay in reach the order is kept (no rows jumping as
        // the player moves); when one comes or goes the list is sorted again, nearest first. The selection stays on
        // the same pickup, else on the same row (clamped), and a fresh list selects the nearest. -1 when empty.
        public static void Sync<T>(List<T> rows, List<T> nearby, Func<T, float> sqrDistance, ref int selected) where T : class
        {
            bool same = rows.Count == nearby.Count;
            for (int i = 0; same && i < nearby.Count; i++) same = rows.Contains(nearby[i]);
            if (!same)
            {
                T kept = selected >= 0 && selected < rows.Count ? rows[selected] : null;
                rows.Clear();
                rows.AddRange(nearby);
                rows.Sort((a, b) => sqrDistance(a).CompareTo(sqrDistance(b)));
                int at = kept != null ? rows.IndexOf(kept) : -1;
                if (at >= 0) selected = at;
            }
            selected = rows.Count == 0 ? -1 : Mathf.Clamp(selected, 0, rows.Count - 1);
        }

        // One wheel notch up (+1) selects the row above, down (-1) the row below; stops at the ends.
        public static int Step(int selected, int count, int wheel) => count == 0 ? -1 : Mathf.Clamp(selected - wheel, 0, count - 1);

        // ---- Runtime ----

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Called by the local PlayerController every frame with the pickups it can see in reach (PickupSight.Gather) and
        // their bearing off the view: the wheel applied, the one nearest the middle of the view returned (null when none).
        // Rows keep their order while the same pickups stay; the selection follows the view unless the wheel moved it lately.
        public LootItem Refresh(Vector3 position, IList<LootItem> candidates, IList<float> bearings, int wheel)
        {
            float now = Time.unscaledTime;
            _expired.Clear();
            foreach (var pair in _taken)
                if (pair.Key == null || !pair.Key.IsSpawned || now >= pair.Value) _expired.Add(pair.Key);
            foreach (var item in _expired)
            {
                _taken.Remove(item);
                if (item != null) SetHidden(item, false); else Hidden.Remove(item);
            }

            _nearby.Clear();
            _rank.Clear();
            _bearing.Clear();
            for (int i = 0; i < candidates.Count; i++)
            {
                var item = candidates[i];
                if (item == null || _taken.ContainsKey(item)) continue;
                _nearby.Add(item);
                _bearing[item] = bearings[i];
                _rank[item] = Interact.Rank(bearings[i], (item.transform.position - position).magnitude);
            }
            Sync(_rows, _nearby, i => i == null || !_rank.TryGetValue(i, out float r) ? float.MaxValue : r, ref _selected);
            if (wheel != 0 && _rows.Count > 1)
            {
                _selected = Step(_selected, _rows.Count, wheel);
                _wheelUntil = now + WheelHoldSeconds;
            }
            else if (now >= _wheelUntil) FollowView();
            return Current;
        }

        // The selection moves to the row nearest the middle of the view once it beats the current one by a margin.
        void FollowView()
        {
            int best = -1;
            float bestRank = float.MaxValue;
            for (int i = 0; i < _rows.Count; i++)
                if (_rank.TryGetValue(_rows[i], out float r) && r < bestRank) { bestRank = r; best = i; }
            if (best < 0 || best == _selected) return;
            float current = _selected >= 0 && _selected < _rows.Count && _rank.TryGetValue(_rows[_selected], out float c) ? c : float.MaxValue;
            if (bestRank + FollowMargin < current) _selected = best;
        }

        // The bearing off the view of the selected pickup (degrees); 180 when none.
        public float CurrentBearing => Current != null && _bearing.TryGetValue(Current, out float r) ? r : 180f;

        // F on the selected pickup: asks the server and takes it off the list (and out of sight) right away.
        public static void Take(Combatant combatant, LootItem item)
        {
            if (combatant == null || item == null || !item.IsSpawned) return;
            combatant.CmdPickup(item.NetworkObject);
            if (Instance == null) return;
            Instance._taken[item] = Time.unscaledTime + TakenSeconds;
            SetHidden(item, true);
        }

        // Hides what F asked for at once. Only renderers that were drawn are switched off and back on: LootView keeps the
        // box off behind a weapon or optic model, and showing it again would draw it over the model.
        static readonly Dictionary<LootItem, List<Renderer>> Hidden = new Dictionary<LootItem, List<Renderer>>();

        static void SetHidden(LootItem item, bool hidden)
        {
            if (hidden)
            {
                if (Hidden.ContainsKey(item)) return;
                var drawn = new List<Renderer>();
                foreach (var r in item.GetComponentsInChildren<Renderer>(true))
                    if (!r.forceRenderingOff) { r.forceRenderingOff = true; drawn.Add(r); }
                Hidden[item] = drawn;
            }
            else if (Hidden.TryGetValue(item, out var drawn))
            {
                Hidden.Remove(item);
                foreach (var r in drawn) if (r != null) r.forceRenderingOff = false;
            }
        }

        void LateUpdate()
        {
            var me = Combatant.LocalPlayer;
            _visible = _rows.Count > 1 && !DoorFirst && me != null && !me.Dead.Value && UiFocus.Current == UiScreen.None
                && !ResultScreen.BlocksGameplay && TeamHud.ReviveCandidate(me) == null;
            DoorFirst = false; // the player sets it again next frame
            if (_root == null)
            {
                if (!_visible) return;
                Build();
            }
            if (_root.activeSelf != _visible) _root.SetActive(_visible);
            if (_visible) Show();
        }

        void Show()
        {
            // A window of MaxRows around the selection when there are more.
            int first = Mathf.Clamp(_selected - MaxRows / 2, 0, Mathf.Max(0, _rows.Count - MaxRows));
            int hash = first * 397 + _selected;
            for (int i = 0; i < _rows.Count; i++) hash = unchecked(hash * 31 + _rows[i].GetInstanceID() * 7 + _rows[i].Amount.Value);
            if (hash == _shownHash) return;
            _shownHash = hash;
            int shown = Mathf.Min(MaxRows, _rows.Count - first);
            float top = (shown * (RowHeight + RowGap) - RowGap) * 0.5f;
            for (int i = 0; i < MaxRows; i++)
            {
                var row = _rowViews[i];
                bool used = i < shown;
                row.Go.SetActive(used);
                if (!used) continue;
                var item = _rows[first + i];
                bool selected = first + i == _selected;
                ((RectTransform)row.Go.transform).anchoredPosition = new Vector2(Offset.x, top - i * (RowHeight + RowGap));
                string name = InventoryBackend.LootName(item);
                row.Label.text = name + ((LootKind)item.Kind.Value == LootKind.Weapon ? "" : "  x" + item.Amount.Value);
                row.Label.color = selected ? UiKit.Text : UiKit.TextDim;
                row.Icon.Set(InventoryBackend.LootIconKey(item), name);
                row.Background.color = selected ? UiKit.Selected : UiKit.Panel;
                foreach (var edge in row.Frame) edge.enabled = selected;
                row.Key.enabled = selected;
            }
        }

        void Build()
        {
            var canvas = UiKit.Canvas("PickupListCanvas", 5, false);
            canvas.transform.SetParent(transform, false);
            var center = new Vector2(0.5f, 0.5f);
            var root = UiKit.Rect("PickupList", canvas.transform, center, center, Vector2.zero, Vector2.zero);
            _root = root.gameObject;
            _rowViews = new Row[MaxRows];
            for (int i = 0; i < MaxRows; i++)
            {
                var rect = UiKit.Rect("Row" + i, root, center, new Vector2(0f, 1f), Vector2.zero, new Vector2(RowWidth, RowHeight));
                var row = new Row { Go = rect.gameObject, Background = UiKit.Box(rect, UiKit.Panel) };
                row.Frame = UiKit.Frame(rect, 2f, UiKit.Accent);
                var keyRect = UiKit.Rect("Key", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(28f, 28f));
                row.Key = UiKit.Label(keyRect, "F", 18, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
                row.Icon = InventoryItemIcon.Build(UiKit.Rect("Icon", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(38f, 0f), new Vector2(34f, 34f)));
                row.Label = UiKit.Label(UiKit.Rect("Label", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(80f, 0f), new Vector2(RowWidth - 88f, RowHeight)), "", 19, UiKit.Text);
                _rowViews[i] = row;
            }
            _root.SetActive(false);
        }
    }
}
