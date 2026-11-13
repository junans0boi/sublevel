using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // The creak and the thud of a door (G11, ADR-104): clips in Resources/Doors/{open,close}, played at the door.
    public static class DoorAudio
    {
        static AudioClip[] _open, _close;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Hook()
        {
            _open = _close = null;
            Door.Moved -= OnMoved;
            Door.Moved += OnMoved;
        }

        static void OnMoved(Door door, bool opening)
        {
            var clips = opening ? (_open ??= Resources.LoadAll<AudioClip>("Doors/open")) : (_close ??= Resources.LoadAll<AudioClip>("Doors/close"));
            if (clips.Length == 0 || door.Audio == null) return;
            door.Audio.PlayOneShot(clips[Random.Range(0, clips.Length)], 0.9f * GameSettings.EffectsVolume);
        }
    }
}
