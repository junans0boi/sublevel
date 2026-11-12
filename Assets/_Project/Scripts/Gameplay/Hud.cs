using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.Gameplay
{
    // Temporary IMGUI HUD (ADR-033). Text is rebuilt only when the shown values change.
    public class Hud : MonoBehaviour
    {
        string _status = "", _prompt = "";
        int _hash = int.MinValue;
        LootItem _promptFor;
        GUIStyle _style, _centered;

        // Fixed rects only: skipping the layout pass avoids its per-frame allocations.
        void Awake() => useGUILayout = false;

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            var me = Combatant.LocalPlayer;
            if (me == null) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 18 };
                _centered = new GUIStyle(_style) { alignment = TextAnchor.MiddleCenter, fontSize = 22 };
            }

            int hash = Hash(me);
            if (hash != _hash)
            {
                _hash = hash;
                _status = BuildStatus(me);
            }
            GUI.Label(new Rect(20, Screen.height - 140, 700, 130), _status, _style);
            GUI.Label(new Rect(Screen.width / 2 - 10, Screen.height / 2 - 14, 20, 28), "+", _centered);

            if (me.Dead.Value)
            {
                GUI.Label(new Rect(0, Screen.height / 2 - 80, Screen.width, 40), "YOU DIED - press Enter to restart", _centered);
                return;
            }

            var loot = PlayerController.Local != null ? PlayerController.Local.NearbyLoot : null;
            if (loot != _promptFor)
            {
                _promptFor = loot;
                _prompt = loot != null ? "[F] " + LootLabel(loot) : "";
            }
            if (_prompt.Length > 0)
                GUI.Label(new Rect(0, Screen.height / 2 + 40, Screen.width, 30), _prompt, _centered);
        }

        static int Hash(Combatant c)
        {
            unchecked
            {
                int h = Mathf.CeilToInt(c.Hp.Value);
                h = h * 31 + c.ActiveSlot.Value;
                h = h * 31 + c.Slot0.Value; h = h * 31 + c.Slot1.Value;
                h = h * 31 + c.Mag0.Value; h = h * 31 + c.Mag1.Value;
                h = h * 31 + c.ReserveLight.Value; h = h * 31 + c.ReserveMedium.Value; h = h * 31 + c.ReserveShell.Value;
                h = h * 31 + c.Medkits.Value;
                h = h * 31 + (c.Reloading.Value ? 1 : 0);
                return h;
            }
        }

        static string BuildStatus(Combatant c)
        {
            return "HP " + Mathf.CeilToInt(c.Hp.Value) + "   Medkits " + c.Medkits.Value + " [H]\n"
                + SlotLine(c, 0, c.Slot0.Value, c.Mag0.Value) + "\n"
                + SlotLine(c, 1, c.Slot1.Value, c.Mag1.Value) + "\n"
                + "Ammo  light " + c.ReserveLight.Value + "  medium " + c.ReserveMedium.Value + "  shells " + c.ReserveShell.Value
                + (c.Reloading.Value ? "\nReloading..." : "");
        }

        static string LootLabel(LootItem item)
        {
            switch ((LootKind)item.Kind.Value)
            {
                case LootKind.Weapon: return GameData.Weapons.Find(item.WeaponId.Value)?.Name ?? "Weapon";
                case LootKind.Ammo: return AmmoName((AmmoType)item.Ammo.Value) + " x" + item.Amount.Value;
                default: return "Medkit";
            }
        }

        static string AmmoName(AmmoType type) =>
            type == AmmoType.Light ? "Light ammo" : type == AmmoType.Medium ? "Medium ammo" : "Shells";

        static string SlotLine(Combatant c, int slot, int weaponId, int mag)
        {
            string mark = c.ActiveSlot.Value == slot ? "> " : "  ";
            WeaponDef w = GameData.Weapons.Find(weaponId);
            if (w == null) return mark + (slot + 1) + ": -";
            return mark + (slot + 1) + ": " + w.Name + "  " + mag + " / " + c.ReserveFor(w.Ammo);
        }
    }
}
