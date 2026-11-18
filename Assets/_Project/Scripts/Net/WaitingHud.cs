using FishNet.Managing;
using UnityEngine;

namespace Game.Net
{
    // Client: the countdown of the waiting island as one line of text (temporary; the board and the HUD of #418 replace it).
    public sealed class WaitingHud : MonoBehaviour
    {
        WaitingBroadcast _last;
        float _at = -10f;
        NetworkManager _nm;
        static GUIStyle _style;

        public static void Attach(NetworkManager nm)
        {
            if (nm == null || nm.GetComponent<WaitingHud>() != null) return;
            var hud = nm.gameObject.AddComponent<WaitingHud>();
            hud._nm = nm;
            nm.ClientManager.RegisterBroadcast<WaitingBroadcast>(hud.OnBroadcast);
        }

        void OnDestroy() { if (_nm != null) _nm.ClientManager.UnregisterBroadcast<WaitingBroadcast>(OnBroadcast); }

        void OnBroadcast(WaitingBroadcast message, FishNet.Transporting.Channel channel) { _last = message; _at = Time.unscaledTime; }

        void OnGUI()
        {
            if (!_last.Active || Time.unscaledTime - _at > 3f) return;
            if (_style == null) _style = new GUIStyle(GUI.skin.label) { fontSize = 28, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            int s = Mathf.CeilToInt(_last.SecondsLeft);
            GUI.Label(new Rect(0, 24, Screen.width, 80), $"Waiting  {s / 60}:{s % 60:00}   {_last.Total} / {_last.Target}", _style);
        }
    }
}
