using Game.Core;
using UnityEngine;

namespace Game.Gameplay
{
    // One thing lying on the range's item table; the range puts it out when the server starts and again when it was
    // taken and the player has walked away.
    public class TrainingLoot : MonoBehaviour
    {
        public LootEntry Entry = new LootEntry();
    }
}
