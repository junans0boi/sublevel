using System;
using System.Collections.Generic;
using Game.Core;
using Game.Net;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Game.Gameplay
{
    // One combatant as the spectate rules see it.
    public struct SpectateCandidate
    {
        public int Id;
        public int Team;   // Teams.None (0) in solo
        public bool Alive;
    }

    // The camera of a dead local player (ADR-094): first a few seconds framing the killer from where the player fell
    // (the death cam), then, on request, following a living combatant over the shoulder with their health and weapon
    // at the bottom of the screen. It only moves Camera.main after PlayerController has placed it (LateUpdate).
    // ResultScreen drives it.
    public class SpectateCamera : MonoBehaviour
    {
        public enum Mode { Off, DeathCam, Follow }

        const float DeathCamDistance = 4.5f;     // metres in front of the killer
        const float DeathCamFov = 45f;
        const float FollowPitch = -8f;            // degrees: a little down, the combatant's pitch is not replicated
        const float BarWidth = 560f;

        public Mode Current { get; private set; } = Mode.Off;
        public Combatant Target { get; private set; }
        public Combatant Killer { get; private set; }
        public string TargetText => _name != null ? _name.text : "";
        public string WeaponText => _weapon != null ? _weapon.text : "";
        public bool BarShown => _bar != null && _bar.activeSelf;

        readonly PlayerCameraRig _rig = new PlayerCameraRig();
        readonly List<SpectateCandidate> _candidates = new List<SpectateCandidate>();
        Vector3 _deathEye;
        // The death cam's own turn and zoom: PlayerController puts the camera back on the body every frame.
        Quaternion _camRotation;
        Vector3 _camPosition;
        float _camFov = -1f;
        GameObject _bar;
        Text _name, _weapon, _hp;
        RectTransform _hpFill;
        float _shownHp = float.NaN;
        int _shownWeapon = int.MinValue, _shownMag = int.MinValue;

        // ---- Rules (pure, EditMode tested) ----

        // The first combatant to watch: a living teammate; otherwise the killer, or whoever killed the killer, and so
        // on down the chain; otherwise anyone alive. -1 when nobody is left.
        public static int FirstTarget(IReadOnlyList<SpectateCandidate> all, int selfId, int selfTeam, int killerId, Func<int, int> killerOf)
        {
            if (selfTeam != Teams.None)
            {
                int mate = Step(all, selfId, selfTeam, -1, 1);
                if (mate >= 0) return mate;
            }
            // The guard stops a chain that loops back on itself.
            for (int id = killerId, guard = 0; id >= 0 && guard <= all.Count; id = killerOf(id), guard++)
                if (id != selfId && IsAlive(all, id)) return id;
            return Step(all, selfId, Teams.None, -1, 1);
        }

        // Left/right: the next living combatant in `dir` (+1/-1) after `current`, among the teammates while any
        // lives, otherwise among everyone. -1 when nobody is left; `current` itself when it is the only one.
        public static int Cycle(IReadOnlyList<SpectateCandidate> all, int selfId, int selfTeam, int current, int dir)
        {
            int team = selfTeam != Teams.None && Step(all, selfId, selfTeam, -1, 1) >= 0 ? selfTeam : Teams.None;
            return Step(all, selfId, team, current, dir);
        }

        static int Step(IReadOnlyList<SpectateCandidate> all, int selfId, int team, int current, int dir)
        {
            int n = all.Count, start = -1;
            for (int i = 0; i < n; i++)
                if (all[i].Id == current) start = i;
            for (int k = 1; k <= n; k++)
            {
                int i = start < 0 ? (dir > 0 ? k - 1 : n - k) : ((start + dir * k) % n + n) % n;
                var c = all[i];
                if (c.Alive && c.Id != selfId && (team == Teams.None || c.Team == team)) return c.Id;
            }
            return -1;
        }

        static bool IsAlive(IReadOnlyList<SpectateCandidate> all, int id)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i].Id == id) return all[i].Alive;
            return false;
        }

        // ---- Driving ----

        // Frames the killer (null: the own body) from the local player's eyes as they fell.
        public void BeginDeathCam(Combatant me, Combatant killer)
        {
            Current = Mode.DeathCam;
            Killer = killer;
            Target = null;
            if (me != null) _deathEye = me.EyePosition;
            var cam = Camera.main;
            if (cam != null && _camFov < 0f)
            {
                _camRotation = cam.transform.rotation;
                _camPosition = cam.transform.position;
                _camFov = cam.fieldOfView;
            }
            ShowBar(false);
        }

        // Starts following; false when there is nobody to watch.
        public bool BeginFollow()
        {
            var me = Combatant.LocalPlayer;
            int id = FirstTarget(Candidates(), me != null ? me.ObjectId : -1, me != null ? me.TeamId.Value : Teams.None,
                MatchState.Instance != null && me != null ? MatchState.Instance.KillerOf(me.ObjectId) : -1, KillerOf);
            Target = Find(id);
            if (Target == null) return false;
            Current = Mode.Follow;
            ShowBar(true);
            return true;
        }

        public void Next() => Switch(1);
        public void Previous() => Switch(-1);

        // Back to the death cam view (the result panel goes over it) or off.
        public void Stop(bool keepDeathCam)
        {
            Current = keepDeathCam ? Mode.DeathCam : Mode.Off;
            Target = null;
            ShowBar(false);
            if (Current != Mode.Off) return;
            ZoneBanner.SpectatorFeet = null;
            _camFov = -1f;
        }

        void Switch(int dir)
        {
            if (Current != Mode.Follow) return;
            var me = Combatant.LocalPlayer;
            var next = Find(Cycle(Candidates(), me != null ? me.ObjectId : -1, me != null ? me.TeamId.Value : Teams.None,
                Target != null ? Target.ObjectId : -1, dir));
            if (next != null) Target = next;
        }

        const float KillEntryWaitSeconds = 1f;
        Combatant _deadSeen;
        float _deadSeenAt;

        static bool KillKnown(int id) => MatchState.Instance != null && MatchState.Instance.TryGetDeath(id, out _);

        // True for the first KillEntryWaitSeconds after the watched one was seen dead.
        bool WaitingForKill(Combatant dead)
        {
            if (_deadSeen != dead) { _deadSeen = dead; _deadSeenAt = Time.unscaledTime; }
            return Time.unscaledTime - _deadSeenAt < KillEntryWaitSeconds;
        }

        static int KillerOf(int id) => MatchState.Instance != null ? MatchState.Instance.KillerOf(id) : -1;

        List<SpectateCandidate> Candidates()
        {
            _candidates.Clear();
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null) continue;
                _candidates.Add(new SpectateCandidate { Id = c.ObjectId, Team = c.TeamId.Value, Alive = !c.Dead.Value });
            }
            return _candidates;
        }

        static Combatant Find(int id)
        {
            if (id < 0) return null;
            var all = Combatant.All;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ObjectId == id) return all[i];
            return null;
        }

        void Awake() => BuildBar();

        void OnDestroy()
        {
            if (Current != Mode.Off) ZoneBanner.SpectatorFeet = null;
        }

        void LateUpdate() => Place();

        // Also called by tests and captures, whose coroutines run before LateUpdate (after PlayerController's view).
        public void Place()
        {
            // The autoplay capture runs its own spectator camera.
            if (Current == Mode.Off || MatchAutoplay.Active) return;
            var cam = Camera.main;
            if (cam == null) return;

            if (Current == Mode.Follow)
            {
                // The watched one died: their teammate, their killer, the next killer down the chain, or anyone.
                // On a client the kill entry (who did it) can arrive a moment after the health says dead: look at the body meanwhile.
                bool waiting = Target != null && Target.Dead.Value && !KillKnown(Target.ObjectId) && WaitingForKill(Target);
                if ((Target == null || Target.Dead.Value) && !waiting)
                {
                    var me = Combatant.LocalPlayer;
                    int killer = Target != null ? KillerOf(Target.ObjectId) : -1;
                    Target = Find(FirstTarget(Candidates(), me != null ? me.ObjectId : -1, me != null ? me.TeamId.Value : Teams.None, killer, KillerOf));
                    if (Target == null)
                    {
                        Stop(true);
                        return;
                    }
                }
                ReadSwitchKeys();
                if (Current != Mode.Follow || Target == null) return;
                _rig.Place(cam.transform, Target.transform, Target.EyePosition, Target.transform.eulerAngles.y, FollowPitch,
                    new ViewState { Aim = AimMode.Hip }, Time.unscaledDeltaTime);
                cam.fieldOfView = GameData.Rules.Aim.Hip.Fov;
                ZoneBanner.SpectatorFeet = Target.transform.position;
                TickBar();
                return;
            }

            // Death cam: flies from where the player fell to a spot a few metres in front of the killer (on the
            // victim's side, short of any wall) and looks them in the face. A straight look from the victim's eyes
            // often showed only the cover between them.
            Vector3 look, wantedAt;
            var self = Combatant.LocalPlayer;
            if (Killer != null)
            {
                look = Killer.transform.position + Vector3.up * 1.4f;
                var back = _deathEye - look;
                back.y = 0f;
                back = back.sqrMagnitude > 1e-4f ? back.normalized : -Killer.transform.forward;
                wantedAt = Clear(look, look + back * DeathCamDistance + Vector3.up * 0.6f, Killer.transform);
            }
            else
            {
                // Nobody did it (the zone, a fall): look down on the own body.
                look = self != null ? self.transform.position : _deathEye;
                wantedAt = Clear(look + Vector3.up, look + new Vector3(0f, 3.5f, -3f), self != null ? self.transform : null);
            }
            float ease = 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime);
            _camPosition = Vector3.Lerp(_camPosition, wantedAt, ease);
            var to = look - _camPosition;
            if (to.sqrMagnitude > 1e-4f) _camRotation = Quaternion.Slerp(_camRotation, Quaternion.LookRotation(to, Vector3.up), ease * 2f);
            cam.transform.SetPositionAndRotation(_camPosition, _camRotation);
            _camFov = Mathf.Lerp(_camFov, DeathCamFov, ease);
            cam.fieldOfView = _camFov;
            ZoneBanner.SpectatorFeet = look;
        }

        static readonly RaycastHit[] ClearHits = new RaycastHit[16];

        // `to`, or short of the first thing between `from` and it (not `ignore`'s own colliders).
        static Vector3 Clear(Vector3 from, Vector3 to, Transform ignore)
        {
            var d = to - from;
            float length = d.magnitude;
            if (length < 1e-3f) return to;
            int count = Physics.SphereCastNonAlloc(from, 0.25f, d / length, ClearHits, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = length;
            for (int i = 0; i < count; i++)
            {
                var h = ClearHits[i];
                if (h.distance > 0f && (ignore == null || !h.collider.transform.IsChildOf(ignore))) best = Mathf.Min(best, h.distance);
            }
            return from + d / length * Mathf.Max(0.8f, best - 0.1f);
        }

        void ReadSwitchKeys()
        {
            // Scripted input (tests, captures) never touches the devices; screens over the view keep the clicks.
            if (PlayerController.InputOverride != null || UiFocus.Current != UiScreen.None) return;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool next = (mouse != null && mouse.leftButton.wasPressedThisFrame) || (keyboard != null && keyboard.rightArrowKey.wasPressedThisFrame);
            bool previous = (mouse != null && mouse.rightButton.wasPressedThisFrame) || (keyboard != null && keyboard.leftArrowKey.wasPressedThisFrame);
            if (next) Next();
            else if (previous) Previous();
        }

        // ---- The watched combatant's essentials ----

        void ShowBar(bool on)
        {
            if (_bar != null && _bar.activeSelf != on) _bar.SetActive(on);
            _shownHp = float.NaN;
            _shownWeapon = _shownMag = int.MinValue;
            if (on && Target != null) _name.text = NameOf(Target);
        }

        static string NameOf(Combatant c)
        {
            if (c.HasTeam && !string.IsNullOrEmpty(c.TeamName.Value)) return c.TeamName.Value;
            // A killer's name came with the kill feed.
            string name = MatchState.Instance != null ? MatchState.Instance.NameOfId(c.ObjectId) : "";
            if (!string.IsNullOrEmpty(name)) return name;
            return c.IsPlayer ? "플레이어" : "생존자";
        }

        Combatant _barFor;

        void TickBar()
        {
            if (_barFor != Target)
            {
                _barFor = Target;
                ShowBar(true);
            }
            float hp = Target.Hp.Value;
            if (hp != _shownHp)
            {
                _shownHp = hp;
                _hpFill.anchorMax = new Vector2(Mathf.Clamp01(hp / 100f), 1f);
                _hp.text = Mathf.CeilToInt(hp).ToString();
            }
            int weapon = Target.ActiveWeaponId, mag = Target.ActiveMag;
            if (weapon == _shownWeapon && mag == _shownMag) return;
            _shownWeapon = weapon;
            _shownMag = mag;
            var def = Target.ActiveWeapon;
            _weapon.text = def == null ? "맨손" : Melee.CanStrike(def) ? def.Name : def.Name + "   " + mag;
        }

        void BuildBar()
        {
            var canvas = UiKit.Canvas("SpectateCanvas", 85, false);
            _bar = canvas.gameObject;
            _bar.transform.SetParent(transform, false);
            var bottom = new Vector2(0.5f, 0f);
            var panel = UiKit.Rect("Bar", _bar.transform, bottom, bottom, new Vector2(0f, 36f), new Vector2(BarWidth, 128f));
            UiKit.Box(panel, UiKit.Panel);
            UiKit.Frame(panel, 1f, UiKit.SteelDim);
            var top = new Vector2(0.5f, 1f);
            UiKit.Label(UiKit.Rect("Caption", panel, top, top, new Vector2(0f, -10f), new Vector2(BarWidth - 32f, 22f)), "관전 중", 16, UiKit.Accent, TextAnchor.MiddleLeft, FontStyle.Bold);
            _name = UiKit.Label(UiKit.Rect("Name", panel, top, top, new Vector2(0f, -32f), new Vector2(BarWidth - 32f, 32f)), "", 26, UiKit.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            _weapon = UiKit.Label(UiKit.Rect("Weapon", panel, top, top, new Vector2(0f, -32f), new Vector2(BarWidth - 32f, 32f)), "", 20, UiKit.Text, TextAnchor.MiddleRight);
            var track = UiKit.Rect("Hp", panel, top, top, new Vector2(-24f, -72f), new Vector2(BarWidth - 80f, 12f));
            UiKit.Box(track, UiKit.PanelLight);
            _hpFill = UiKit.Fill("Fill", track);
            UiKit.Box(_hpFill, UiKit.Health);
            _hp = UiKit.Label(UiKit.Rect("HpText", panel, top, top, new Vector2(BarWidth / 2f - 40f, -64f), new Vector2(48f, 28f)), "", 20, UiKit.Text, TextAnchor.MiddleRight, FontStyle.Bold);
            UiKit.Label(UiKit.Rect("Hint", panel, top, top, new Vector2(0f, -96f), new Vector2(BarWidth - 32f, 24f)),
                "← → / 클릭 — 대상 변경    Enter — 다시 시작    Esc — 메뉴", 16, UiKit.TextDim, TextAnchor.MiddleCenter);
            _bar.SetActive(false);
        }
    }
}
