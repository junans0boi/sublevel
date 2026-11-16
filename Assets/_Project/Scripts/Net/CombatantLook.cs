using FishNet.Broadcast;
using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Game.Net
{
    // A client's chosen look as text (PlayerAppearance.Serialize), sent once after it connects so the server can spawn
    // the matching Player_<model> prefab (ADR-255 seam: the UI track's PlayerAppearance API, animation track wiring).
    public struct LookBroadcast : IBroadcast
    {
        public string Look;
    }

    // The look a player was spawned with, replicated to every peer. The model is the spawned prefab itself; this text
    // carries the colour variant (CombatantLookView applies it on every side). Set by the server before Spawn.
    public class CombatantLook : NetworkBehaviour
    {
        public readonly SyncVar<string> Look = new SyncVar<string>("");

        public void ServerSet(string look) => Look.Value = look ?? "";
    }
}
