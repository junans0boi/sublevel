using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay
{
    // The places in a zone that gameplay refers to by role: convergence points, elevators, named landmarks.
    public class ZoneInfo : MonoBehaviour
    {
        [SerializeField] string _zoneId;
        [SerializeField] Transform[] _convergencePoints = new Transform[0];
        [SerializeField] Transform[] _elevators = new Transform[0];
        [SerializeField] Transform[] _landmarks = new Transform[0];

        public static ZoneInfo Current { get; private set; }

        public string ZoneId => _zoneId;
        public IReadOnlyList<Transform> ConvergencePoints => _convergencePoints;
        public IReadOnlyList<Transform> Elevators => _elevators;

        public Transform Landmark(string name)
        {
            foreach (var mark in _landmarks)
                if (mark != null && mark.name == name) return mark;
            return null;
        }

        void Awake() => Current = this;

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }
    }
}
