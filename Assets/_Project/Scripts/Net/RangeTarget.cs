using UnityEngine;

namespace Game.Net
{
    // A target of the waiting island's shooting range (ADR-501): the TrainingDummy's body (a 1.8 m capsule) standing down a booth's
    // lane. Only a marker on the collider; the server's bullet hook (WaitingRoom.Intercept) scores what hits it. Built by WorldBuilder.
    public sealed class RangeTarget : MonoBehaviour
    {
        public int Booth;
    }
}
