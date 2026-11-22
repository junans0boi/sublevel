using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The two wheels over the match (ADR-130, ADR-131): the marker wheel (team matches: hold the ping button a moment)
    // and the emote wheel (hold N). It reads the local player's last input, opens a wheel, and on letting go does what
    // the part under the cursor says. A wheel is the whole screen's business while it is open: PlayerController feeds the
    // mouse into it instead of the camera and drops firing. Installs itself next to every match like TeamHud.
    public class WheelHud : MonoBehaviour
    {
        const int SortOrder = 3; // above TeamHud
        const float EmoteHoldSeconds = 0.12f;

        public static WheelHud Instance { get; private set; }

        public RadialWheel MarkerWheel { get; private set; }
        public RadialWheel EmoteWheel { get; private set; }

        Canvas _canvas;
        float _pingHeld, _emoteHeld;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            MatchState.Started -= Install;
            MatchState.Started += Install;
        }

        static void Install(MatchState state)
        {
            if (Instance != null || state == null) return;
            var go = new GameObject("WheelHud");
            go.transform.SetParent(state.transform, false);
            go.AddComponent<WheelHud>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
            _canvas = UiKit.Canvas("WheelCanvas", SortOrder, false);
            _canvas.transform.SetParent(transform, false);
            var kinds = new string[Pings.KindCount];
            var kindColours = new Color[Pings.KindCount];
            for (int i = 0; i < kinds.Length; i++)
            {
                kinds[i] = Pings.Name((PingKind)i);
                kindColours[i] = TeamPingView.ColourOf((PingKind)i);
            }
            MarkerWheel = RadialWheel.Build(_canvas.transform, "MarkerWheel", "마커", kinds, kindColours);
            var emotes = new string[Emotes.Count];
            var emoteColours = new Color[Emotes.Count];
            for (int i = 0; i < emotes.Length; i++)
            {
                emotes[i] = Emotes.Name((Emote)(i + 1));
                emoteColours[i] = UiKit.Accent;
            }
            EmoteWheel = RadialWheel.Build(_canvas.transform, "EmoteWheel", "감정 표현", emotes, emoteColours);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            MarkerWheel?.Hide();
            EmoteWheel?.Hide();
        }

        void LateUpdate()
        {
            var me = Combatant.LocalPlayer;
            var player = PlayerController.Local;
            bool allowed = me != null && player != null && !me.Dead.Value && UiFocus.Current == UiScreen.None && !ResultScreen.BlocksGameplay && !SpawnProtection.InputBlocked;
            var input = allowed ? player.LastInput : default;

            // Marker wheel: team matches, on foot; held past the tap time it opens, letting go places the ping.
            bool markerOk = allowed && me.HasTeam && !me.Knocked.Value && !EmoteWheel.IsOpen;
            int kind = Tick(MarkerWheel, markerOk && input.PingHeld, ref _pingHeld, DeviceInput.WheelHoldSeconds);
            if (kind >= 0) PlaceMarker(me, player, (PingKind)kind);

            bool emoteOk = allowed && !me.Knocked.Value && !me.IsCarrying && !me.BeingCarried && !MarkerWheel.IsOpen;
            int part = Tick(EmoteWheel, emoteOk && input.EmoteHeld, ref _emoteHeld, EmoteHoldSeconds);
            if (part >= 0) player.StartEmote((Emote)(part + 1));
        }

        // Opens `wheel` once the button has been held `after` seconds; on release returns the chosen part (-1 none).
        // A returned value, not a callback: the two lambdas captured locals and allocated every frame.
        static int Tick(RadialWheel wheel, bool held, ref float heldFor, float after)
        {
            if (held)
            {
                heldFor += Time.unscaledDeltaTime;
                if (heldFor >= after) wheel.Show();
                return -1;
            }
            heldFor = 0f;
            if (!wheel.IsOpen) return -1;
            int part = wheel.Selected;
            wheel.Hide();
            return part;
        }

        // What the marker wheel does with a part: the ping at the crosshair, "enemy" when one stands on the spot.
        public static void PlaceMarker(Combatant me, PlayerController player, PingKind kind)
        {
            if (!player.TryPingPoint(out var point)) return;
            kind = Pings.Upgrade(kind, TeamHud.EnemyNear(me, point));
            // The plain "go" is the player's own map marker (drawn on the compass, minimap and in the world as before);
            // TeamHud shares it with the team when it changes.
            if (kind == PingKind.Go) MapMarker.Set(point);
            else me.CmdPing((byte)kind, point);
        }
    }
}
