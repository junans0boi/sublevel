using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // Member colours of a team (ADR-072): one per number, the same on every view. Our own palette.
    public static class TeamColours
    {
        static readonly Color[] Slots =
        {
            new Color(0.98f, 0.80f, 0.22f, 1f), // 1 yellow
            new Color(0.36f, 0.72f, 1.00f, 1f), // 2 blue
            new Color(0.46f, 0.86f, 0.42f, 1f), // 3 green
            new Color(0.95f, 0.46f, 0.76f, 1f), // 4 pink
        };

        public static Color Of(int slot) => slot >= 1 && slot <= Slots.Length ? Slots[slot - 1] : UiKit.Text;
    }

    // Everything the local player sees of its team (ADR-072), in team matches only: the member list with health
    // bars (bottom left, above the status), name tags over teammates, teammates and their pings on the compass, the
    // minimap and the full map, the knocked and revive overlays, and the F-held revive itself (the owner's side).
    // It also shares the local map marker with the team. Installs itself next to every match like the kill feed;
    // it reads the HUD's compass and minimap and the map screen only to hang its markers on them.
    public class TeamHud : MonoBehaviour
    {
        const int SortOrder = 2; // above the HUD and the kill feed
        const float Margin = 36f, ListBottom = 36f + HudStatus.Height + 14f;
        const float RowWidth = 300f, RowHeight = 34f, RowGap = 4f;
        const float TagLift = 0.55f;
        const float EnemyPingRadius = 3f; // a ping this close to an enemy marks it
        const float RequestSeconds = 0.3f;
        const float CompassMemberSize = 14f;
        const float MinimapEdge = Minimap.ViewSize * 0.5f - 8f;

        static readonly Color Rim = new Color(0.04f, 0.05f, 0.06f, 0.95f);

        public static TeamHud Instance { get; private set; }

        // ---- Read-only state for tests ----
        public bool Active => _canvas != null && _canvas.enabled;
        public int MembersShown => _members.Count;
        public string MemberText(int i) => i >= 0 && i < _rows.Count && _rows[i].Root.activeSelf ? _rows[i].Name.text : "";
        public TeamPingView PingView => _pings;
        public bool KnockedShown => _knockedBand != null && _knockedBand.activeSelf;
        public bool RevivePromptShown => _prompt != null && _prompt.activeSelf;
        public string PromptText => RevivePromptShown ? _promptLabel.text : "";
        public bool ReviveBarShown => _reviveBar != null && _reviveBar.activeSelf;
        public int TagsShown
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _views.Count; i++) if (_views[i].Tag.gameObject.activeSelf) n++;
                return n;
            }
        }
        public int CompassMembersShown
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _views.Count; i++) if (_views[i].Compass != null && _views[i].Compass.gameObject.activeSelf) n++;
                return n;
            }
        }
        public int MinimapMembersShown
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _views.Count; i++) if (_views[i].Minimap != null && _views[i].Minimap.gameObject.activeSelf) n++;
                return n;
            }
        }

        // The knocked teammate F would revive now: nearest within reach, nobody else on it. Null when none (or the
        // local combatant cannot revive). PlayerController asks before an F tap picks something up.
        public static Combatant ReviveCandidate(Combatant me)
        {
            if (me == null || !me.HasTeam || !me.Standing || me.Reviving || me.IsCarrying) return null;
            float best = TeamRules.Current.ReviveRangeMeters;
            best *= best;
            Combatant found = null;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !me.IsTeammateOf(c) || !c.Knocked.Value || c.Dead.Value || c.BeingRevived || c.BeingCarried) continue;
                float sqr = (c.transform.position - me.transform.position).sqrMagnitude;
                if (sqr > best) continue;
                best = sqr;
                found = c;
            }
            return found;
        }

        // The knocked teammate the carry key would lift now: nearest within CarryRangeMeters that nobody revives or carries.
        public static Combatant CarryCandidate(Combatant me)
        {
            if (me == null || !me.HasTeam || !me.Standing || me.Reviving || me.IsCarrying) return null;
            float best = TeamRules.Current.CarryRangeMeters;
            best *= best;
            Combatant found = null;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !me.IsTeammateOf(c) || !c.Knocked.Value || c.Dead.Value || c.BeingRevived || c.BeingCarried) continue;
                float sqr = (c.transform.position - me.transform.position).sqrMagnitude;
                if (sqr > best) continue;
                best = sqr;
                found = c;
            }
            return found;
        }

        sealed class Row
        {
            public GameObject Root;
            public Image Number;
            public Text NumberText, Name, State;
            public RectTransform Fill;
            public Image FillImage;
            public float ShownFill = -1f;
            public string ShownName;
            public int ShownState = -1;
        }

        // Markers of one teammate (never the local player).
        sealed class View
        {
            public Combatant Of;
            public RectTransform Tag;
            public Text TagName, TagState;
            public RectTransform Compass, Minimap, Map;
            public string ShownTag;
            public int ShownTagState = -1;
        }

        Canvas _canvas;
        Transform _root;
        TeamPingView _pings;
        readonly List<Combatant> _members = new List<Combatant>();
        readonly List<Row> _rows = new List<Row>();
        readonly List<View> _views = new List<View>();
        int _seenCount = -1, _seenTeam = -1;
        float _nextRefresh;

        GameObject _knockedBand, _prompt, _reviveBar, _waitNotice;
        Text _knockedHint, _promptLabel, _reviveLabel;
        RectTransform _bleedFill, _reviveFill;
        Image _bleedFillImage;
        int _shownBleed = -1, _shownReviveTenths = -1;

        RectTransform _compassStrip, _minimapClip, _mapArea;
        float _nextRevive;
        int _pingVersion = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            MatchState.Started -= Install;
            MatchState.Started += Install;
        }

        static void Install(MatchState state)
        {
            if (Instance != null || state == null) return;
            var go = new GameObject("TeamHud");
            // Under the match object so it goes away with the scene.
            go.transform.SetParent(state.transform, false);
            go.AddComponent<TeamHud>();
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
            _canvas.enabled = false;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            var me = Combatant.LocalPlayer;
            bool team = me != null && me.HasTeam;
            bool drawn = team && UiFocus.Current != UiScreen.Inventory && UiFocus.Current != UiScreen.Map;
            if (_canvas.enabled != drawn) _canvas.enabled = drawn;
            if (!team)
            {
                SetViewsShown(false);
                return;
            }

            RefreshMembers(me);
            TickReviveInput(me);
            SharePing(me);
            TickRows();
            TickOverlays(me);
            TickMarkers(me);
        }

        // ---- Members ----

        void RefreshMembers(Combatant me)
        {
            var all = Combatant.All;
            if (all.Count == _seenCount && me.TeamId.Value == _seenTeam && Time.time < _nextRefresh) return;
            _seenCount = all.Count;
            _seenTeam = me.TeamId.Value;
            _nextRefresh = Time.time + 0.5f;
            _members.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c != null && (c == me || me.IsTeammateOf(c))) _members.Add(c);
            }
            _members.Sort((a, b) => a.TeamSlot.Value.CompareTo(b.TeamSlot.Value));

            while (_rows.Count < _members.Count) _rows.Add(BuildRow(_rows.Count));
            for (int i = 0; i < _rows.Count; i++)
            {
                bool on = i < _members.Count;
                if (_rows[i].Root.activeSelf != on) _rows[i].Root.SetActive(on);
                _rows[i].ShownName = null;
                _rows[i].ShownState = -1;
                _rows[i].ShownFill = -1f;
            }

            // One marker set per teammate; the ones for combatants no longer on the team go away.
            for (int i = _views.Count - 1; i >= 0; i--)
            {
                if (_views[i].Of != null && _members.Contains(_views[i].Of) && _views[i].Of != me) continue;
                DestroyView(_views[i]);
                _views.RemoveAt(i);
            }
            for (int i = 0; i < _members.Count; i++)
            {
                var c = _members[i];
                if (c == me || FindView(c) != null) continue;
                _views.Add(BuildView(c));
            }
        }

        View FindView(Combatant c)
        {
            for (int i = 0; i < _views.Count; i++) if (_views[i].Of == c) return _views[i];
            return null;
        }

        void TickRows()
        {
            float knockedMax = Mathf.Max(1f, TeamRules.Current.KnockedHp);
            for (int i = 0; i < _members.Count && i < _rows.Count; i++)
            {
                var c = _members[i];
                var row = _rows[i];
                if (c == null) continue;
                string name = c.TeamName.Value;
                if (name != row.ShownName)
                {
                    row.ShownName = name;
                    row.Name.text = string.IsNullOrEmpty(name) ? "—" : name;
                    int slot = c.TeamSlot.Value;
                    row.NumberText.text = HudDigits.IntText(slot);
                    row.Number.color = TeamColours.Of(slot);
                }
                int state = c.Dead.Value ? 2 : c.Knocked.Value ? 1 : 0;
                if (state != row.ShownState)
                {
                    row.ShownState = state;
                    row.State.text = state == 2 ? "사망" : state == 1 ? "기절" : "";
                    row.State.color = state == 2 ? UiKit.TextDim : UiKit.Danger;
                    row.FillImage.color = state == 1 ? UiKit.Danger : UiKit.Health;
                    row.Name.color = state == 2 ? UiKit.TextDim : UiKit.Text;
                }
                float fill = state == 2 ? 0f : Mathf.Clamp01(c.Hp.Value / (state == 1 ? knockedMax : 100f));
                if (fill != row.ShownFill)
                {
                    row.ShownFill = fill;
                    row.Fill.anchorMax = new Vector2(fill, 1f);
                }
            }
        }

        // ---- Revive (owner side) and the shared ping ----

        void TickReviveInput(Combatant me)
        {
            var player = PlayerController.Local;
            bool held = player != null && player.LastInput.InteractHeld && UiFocus.Current == UiScreen.None && !ResultScreen.BlocksGameplay;
            if (player != null && player.LastInput.CarryPressed && UiFocus.Current == UiScreen.None && !ResultScreen.BlocksGameplay && Time.time >= _nextRevive)
            {
                var lift = CarryCandidate(me);
                if (me.IsCarrying) me.CmdEndCarry();
                else if (lift != null) me.CmdBeginCarry(lift.NetworkObject);
                _nextRevive = Time.time + RequestSeconds;
            }
            if (me.Reviving)
            {
                if (!held && Time.time >= _nextRevive)
                {
                    me.CmdCancelRevive();
                    _nextRevive = Time.time + RequestSeconds;
                }
                return;
            }
            if (!held || Time.time < _nextRevive) return;
            var candidate = ReviveCandidate(me);
            if (candidate == null) return;
            _nextRevive = Time.time + RequestSeconds;
            me.CmdBeginRevive(candidate.NetworkObject);
        }

        // The map marker (a tap on the ping button, a click on the map) goes to the team as a "go" ping, or as "enemy
        // spotted" when an enemy stands on the spot. Clearing it tells nobody: a team ping fades by itself.
        void SharePing(Combatant me)
        {
            if (MapMarker.Version == _pingVersion) return;
            _pingVersion = MapMarker.Version;
            if (!MapMarker.Has) return;
            var at = MapMarker.Position;
            me.CmdPing((byte)Pings.Upgrade(PingKind.Go, EnemyNear(me, at)), at);
        }

        // An enemy (alive, not a teammate) within a few metres of a spot.
        public static bool EnemyNear(Combatant me, Vector3 at)
        {
            float best = EnemyPingRadius * EnemyPingRadius;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || c == me || c.Dead.Value || me.IsTeammateOf(c)) continue;
                if ((c.transform.position - at).sqrMagnitude <= best) return true;
            }
            return false;
        }

        // ---- Overlays: knocked, revive prompt and bar, waiting for the team ----

        void TickOverlays(Combatant me)
        {
            bool knocked = me.Knocked.Value && !me.Dead.Value;
            Show(_knockedBand, knocked);
            if (knocked)
            {
                bool helped = me.BeingRevived;
                bool carried = me.BeingCarried;
                int seconds = Mathf.CeilToInt(me.BleedSecondsLeft);
                int key = helped ? -2 : carried ? -3 - seconds : seconds;
                if (key != _shownBleed)
                {
                    _shownBleed = key;
                    string bleed = "출혈 " + HudDigits.IntText(seconds) + "초";
                    _knockedHint.text = helped ? "팀원이 부활시키는 중" : carried ? "팀원이 업고 있습니다 — " + bleed : bleed + " — 팀원의 부활을 기다리세요";
                }
                _bleedFill.anchorMax = new Vector2(helped ? me.ReviveProgress.Value : Mathf.Clamp01(me.Hp.Value / Mathf.Max(1f, TeamRules.Current.KnockedHp)), 1f);
                _bleedFillImage.color = helped ? TeamColours.Of(me.TeamSlot.Value) : UiKit.Danger;
            }
            else _shownBleed = -1;

            var candidate = ReviveCandidate(me);
            bool carrying = me.IsCarrying;
            bool prompt = (candidate != null || carrying) && UiFocus.Current == UiScreen.None;
            Show(_prompt, prompt);
            if (prompt)
            {
                string carryKey = KeyBinds.Name(BindAction.Carry);
                string text = carrying ? carryKey + " 내려놓기" : "F 길게 눌러 부활 · " + carryKey + " 업기 · " + candidate.TeamName.Value;
                if (_promptLabel.text != text) _promptLabel.text = text;
            }

            bool reviving = me.Reviving;
            Show(_reviveBar, reviving);
            if (reviving)
            {
                _reviveFill.anchorMax = new Vector2(Mathf.Clamp01(me.ReviveProgress.Value), 1f);
                int tenths = Mathf.CeilToInt(TeamRules.Current.ReviveSeconds * (1f - me.ReviveProgress.Value) * 10f);
                if (tenths != _shownReviveTenths)
                {
                    _shownReviveTenths = tenths;
                    _reviveLabel.text = "부활 중  " + (tenths / 10f).ToString("0.0") + "초";
                }
            }

            // Dead while the team fights on: the result comes when the team is decided.
            var state = MatchState.Instance;
            bool waiting = me.Dead.Value && state != null && state.PlacementOf(me) == 0;
            Show(_waitNotice, waiting);
        }

        // ---- Markers on the views ----

        void TickMarkers(Combatant me)
        {
            EnsureHosts();
            var player = PlayerController.Local;
            var cam = player != null ? player.Camera : Camera.main;
            float scale = _canvas.scaleFactor;
            var from = me.transform.position;
            float heading = player != null ? player.Yaw : 0f;
            bool hud = UiFocus.Current == UiScreen.None;
            for (int i = 0; i < _views.Count; i++)
            {
                var v = _views[i];
                var c = v.Of;
                bool alive = c != null && !c.Dead.Value;
                var colour = TeamColours.Of(c != null ? c.TeamSlot.Value : 0);
                var at = alive ? c.transform.position : Vector3.zero;

                TickTag(v, cam, scale, hud && alive);
                if (v.Compass != null)
                {
                    Show(v.Compass.gameObject, alive);
                    if (alive) v.Compass.anchoredPosition = new Vector2(CompassStrip.MarkerOffset(heading, Minimap.Bearing(from, at), out _), -9f);
                }
                if (v.Minimap != null)
                {
                    Show(v.Minimap.gameObject, alive);
                    if (alive) v.Minimap.anchoredPosition = ClampToMinimap(Minimap.ToView(at, from));
                }
                if (v.Map != null)
                {
                    Show(v.Map.gameObject, alive);
                    if (alive) v.Map.anchoredPosition = MapPixels(at);
                }
            }
            _pings.Tick(me, cam, scale, heading, from, hud, Time.time);
        }

        void TickTag(View v, Camera cam, float scale, bool show)
        {
            var c = v.Of;
            Vector2 screen = default;
            bool visible = show && cam != null && OnScreen(cam, c.EyePosition + Vector3.up * TagLift, scale, out screen);
            Show(v.Tag.gameObject, visible);
            if (!visible) return;
            v.Tag.anchoredPosition = screen;
            string name = HudDigits.IntText(c.TeamSlot.Value) + "  " + c.TeamName.Value;
            if (name != v.ShownTag)
            {
                v.ShownTag = name;
                v.TagName.text = name;
                v.TagName.color = TeamColours.Of(c.TeamSlot.Value);
            }
            int state = c.Knocked.Value ? (c.BeingRevived ? 2 : 1) : 0;
            if (state != v.ShownTagState)
            {
                v.ShownTagState = state;
                v.TagState.text = state == 2 ? "부활 중" : state == 1 ? "기절" : "";
            }
        }

        internal static bool OnScreen(Camera cam, Vector3 world, float scale, out Vector2 canvas)
        {
            canvas = default;
            if (scale <= 0f) return false;
            var p = cam.WorldToScreenPoint(world);
            if (p.z <= 0f || p.x < 0f || p.y < 0f || p.x > Screen.width || p.y > Screen.height) return false;
            canvas = new Vector2((p.x - Screen.width * 0.5f) / scale, (p.y - Screen.height * 0.5f) / scale);
            return true;
        }

        internal static Vector2 ClampToMinimap(Vector2 at)
        {
            float reach = Mathf.Max(Mathf.Abs(at.x), Mathf.Abs(at.y));
            return reach > MinimapEdge ? at * (MinimapEdge / reach) : at;
        }

        internal static Vector2 MapPixels(Vector3 world)
        {
            var uv = MapImage.WorldToUv(world);
            return new Vector2(Mathf.Clamp01(uv.x) * MapScreen.MapPx, Mathf.Clamp01(uv.y) * MapScreen.MapPx);
        }

        // The compass, minimap and map are built by others; markers are hung on them once they exist.
        void EnsureHosts()
        {
            bool changed = false;
            var hud = Hud.Instance;
            if (_compassStrip == null && hud != null && hud.Compass != null && hud.Compass.Strip != null)
            {
                _compassStrip = hud.Compass.Strip;
                changed = true;
            }
            if (_minimapClip == null && hud != null && hud.Minimap != null && hud.Minimap.Root != null)
            {
                var clip = hud.Minimap.Root.Find("Clip");
                _minimapClip = clip != null ? (RectTransform)clip : hud.Minimap.Root;
                changed = true;
            }
            if (_mapArea == null && MapScreen.Instance != null && MapScreen.Instance.MapArea != null)
            {
                _mapArea = MapScreen.Instance.MapArea;
                changed = true;
            }
            if (!changed) return;
            for (int i = 0; i < _views.Count; i++) AttachHosted(_views[i]);
            _pings.SetHosts(_compassStrip, _minimapClip, _mapArea);
        }

        // ---- Building ----

        void Build()
        {
            _canvas = UiKit.Canvas("TeamHudCanvas", SortOrder, false);
            _canvas.transform.SetParent(transform, false);
            _root = _canvas.transform;
            _pings = new TeamPingView(_root);
            var centre = new Vector2(0.5f, 0.5f);

            // Knocked: a band under the centre with the bleed bar.
            var band = UiKit.Rect("Knocked", _root, new Vector2(0.5f, 0.32f), centre, Vector2.zero, new Vector2(620f, 96f));
            UiKit.Box(band, UiKit.Panel);
            UiKit.Frame(band, 1f, UiKit.Danger);
            UiKit.Label(UiKit.Rect("Title", band, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(600f, 40f)),
                "기절", 34, UiKit.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            _knockedHint = UiKit.Label(UiKit.Rect("Hint", band, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(600f, 26f)),
                "", 20, UiKit.Text, TextAnchor.MiddleCenter);
            var track = UiKit.Rect("Track", band, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(560f, 8f));
            UiKit.Box(track, UiKit.PanelLight);
            _bleedFill = UiKit.Fill("Fill", track);
            _bleedFill.anchorMax = new Vector2(1f, 1f);
            _bleedFillImage = UiKit.Box(_bleedFill, UiKit.Danger);
            _knockedBand = band.gameObject;
            _knockedBand.SetActive(false);

            // Revive prompt and progress, under the crosshair.
            var prompt = UiKit.Rect("RevivePrompt", _root, new Vector2(0.5f, 0.42f), centre, Vector2.zero, new Vector2(520f, 36f));
            UiKit.Box(prompt, UiKit.Panel);
            _promptLabel = UiKit.Label(UiKit.Fill("Text", prompt), "", 22, UiKit.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            _prompt = prompt.gameObject;
            _prompt.SetActive(false);

            var bar = UiKit.Rect("ReviveBar", _root, new Vector2(0.5f, 0.38f), centre, Vector2.zero, new Vector2(360f, 44f));
            UiKit.Box(bar, UiKit.Panel);
            _reviveLabel = UiKit.Label(UiKit.Rect("Text", bar, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -2f), new Vector2(340f, 26f)),
                "", 20, UiKit.Text, TextAnchor.MiddleCenter);
            var reviveTrack = UiKit.Rect("Track", bar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(330f, 6f));
            UiKit.Box(reviveTrack, UiKit.PanelLight);
            _reviveFill = UiKit.Fill("Fill", reviveTrack);
            UiKit.Box(_reviveFill, UiKit.Accent);
            _reviveBar = bar.gameObject;
            _reviveBar.SetActive(false);

            // Dead, team still in it: under the HUD's death band.
            var wait = UiKit.Rect("WaitForTeam", _root, new Vector2(0.5f, 0.5f), centre, new Vector2(0f, -70f), new Vector2(700f, 34f));
            UiKit.Label(wait, "팀원 생존 중 — 팀의 결과를 기다립니다", 24, UiKit.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            _waitNotice = wait.gameObject;
            _waitNotice.SetActive(false);
        }

        Row BuildRow(int index)
        {
            var row = new Row();
            var rect = UiKit.Rect("Member" + index, _root, Vector2.zero, Vector2.zero,
                new Vector2(Margin, ListBottom + (Teams.MaxSize - 1 - index) * (RowHeight + RowGap)), new Vector2(RowWidth, RowHeight));
            UiKit.Box(rect, UiKit.Panel);
            var number = UiKit.Rect("Number", rect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(26f, 26f));
            row.Number = UiKit.Box(number, UiKit.Text);
            row.NumberText = UiKit.Label(UiKit.Fill("Text", number), "", 18, Rim, TextAnchor.MiddleCenter, FontStyle.Bold);
            row.Name = UiKit.Label(UiKit.Rect("Name", rect, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(38f, -2f), new Vector2(170f, 20f)),
                "", 17, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            row.State = UiKit.Label(UiKit.Rect("State", rect, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-8f, -2f), new Vector2(80f, 20f)),
                "", 16, UiKit.Danger, TextAnchor.MiddleRight, FontStyle.Bold);
            var track = UiKit.Rect("Track", rect, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(38f, 5f), new Vector2(RowWidth - 46f, 6f));
            UiKit.Box(track, UiKit.PanelLight);
            row.Fill = UiKit.Fill("Fill", track);
            row.FillImage = UiKit.Box(row.Fill, UiKit.Health);
            row.Root = rect.gameObject;
            return row;
        }

        View BuildView(Combatant c)
        {
            var v = new View { Of = c };
            var centre = new Vector2(0.5f, 0.5f);
            var colour = TeamColours.Of(c.TeamSlot.Value);
            v.Tag = UiKit.Rect("Tag", _root, centre, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(220f, 44f));
            v.TagName = UiKit.Label(UiKit.Rect("Name", v.Tag, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(220f, 22f)),
                "", 18, colour, TextAnchor.MiddleCenter, FontStyle.Bold);
            Shadowed(v.TagName);
            v.TagState = UiKit.Label(UiKit.Rect("State", v.Tag, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(220f, 20f)),
                "", 16, UiKit.Danger, TextAnchor.MiddleCenter, FontStyle.Bold);
            Shadowed(v.TagState);
            v.Tag.gameObject.SetActive(false);
            AttachHosted(v);
            return v;
        }

        // The parts that live on the compass, minimap and map, made once each host exists.
        void AttachHosted(View v)
        {
            var colour = TeamColours.Of(v.Of != null ? v.Of.TeamSlot.Value : 0);
            string number = v.Of != null ? HudDigits.IntText(v.Of.TeamSlot.Value) : "";
            if (_compassStrip != null && v.Compass == null)
            {
                v.Compass = NumberChip("TeamMember", _compassStrip, CompassMemberSize, colour, number, 11);
            }
            if (_minimapClip != null && v.Minimap == null)
            {
                v.Minimap = NumberChip("TeamMember", _minimapClip, 13f, colour, number, 10);
            }
            if (_mapArea != null && v.Map == null)
            {
                v.Map = NumberChip("TeamMember", _mapArea, 22f, colour, number, 15, Vector2.zero);
            }
        }

        void DestroyView(View v)
        {
            foreach (var rect in new[] { v.Tag, v.Compass, v.Minimap, v.Map })
                if (rect != null) Destroy(rect.gameObject);
        }

        void SetViewsShown(bool on)
        {
            if (!on) _pings.HideAll();
            for (int i = 0; i < _views.Count; i++)
                foreach (var rect in new[] { _views[i].Tag, _views[i].Compass, _views[i].Minimap, _views[i].Map })
                    if (rect != null) Show(rect.gameObject, on);
        }

        // A coloured square with the member's number on a dark rim. Anchored at the host's centre unless given.
        static RectTransform NumberChip(string name, Transform parent, float size, Color colour, string number, int fontSize, Vector2? anchor = null)
        {
            var a = anchor ?? new Vector2(0.5f, 0.5f);
            var rect = UiKit.Rect(name, parent, a, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
            UiKit.Box(UiKit.Fill("Rim", rect, -1.5f), Rim);
            UiKit.Box(UiKit.Fill("Fill", rect), colour);
            UiKit.Label(UiKit.Fill("Number", rect), number, fontSize, Rim, TextAnchor.MiddleCenter, FontStyle.Bold);
            rect.gameObject.SetActive(false);
            return rect;
        }

        internal static void Shadowed(Text text)
        {
            var shadow = text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Rim;
            shadow.effectDistance = new Vector2(1.5f, -1.5f);
        }

        static void Show(GameObject go, bool on)
        {
            if (go != null && go.activeSelf != on) go.SetActive(on);
        }
    }
}
