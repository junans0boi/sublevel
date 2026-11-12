using FishNet.Object;
using FishNet.Object.Synchronizing;

namespace Game.Net
{
    // M0-6 smoke fixture: proves server-authoritative SyncVar replication through a production assembly.
    public class SmokeNetObject : NetworkBehaviour
    {
        public readonly SyncVar<int> Counter = new SyncVar<int>();
    }
}
