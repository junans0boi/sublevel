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

        // Recorded door sounds (ADR-252, names door_fs_*) when the folder has them, else every clip in it (the synthesised ones).
        static AudioClip[] Recorded(string folder)
        {
            var all = Resources.LoadAll<AudioClip>(folder);
            var recorded = System.Array.FindAll(all, c => c.name.StartsWith("door_fs_", System.StringComparison.Ordinal));
            return recorded.Length > 0 ? recorded : all;
        }

        static void OnMoved(Door door, bool opening)
        {
            var clips = opening ? (_open ??= Recorded("Doors/open")) : (_close ??= Recorded("Doors/close"));
            if (clips.Length == 0 || door.Audio == null) return;
            door.Audio.PlayOneShot(clips[Random.Range(0, clips.Length)], 0.9f * GameSettings.EffectsVolume);
        }
    }
}
