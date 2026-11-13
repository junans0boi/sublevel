using System;
using System.Reflection;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.AI
{
    // The server calls a bot makes where Combatant only offers an owner request (Cmd*), which a bot cannot send:
    // it has no owning connection. Each call does exactly what the request does.
    //
    // ADAPTER: slot switching and medkits have no public server method on Combatant yet. When Combatant gets
    // `public bool ServerSelectSlot(int slot)` and `public bool ServerUseMedkit()` (and optionally
    // `public string ServerDisplayName { get; set; }` for the kill feed), they are found by name and used.
    // Until then the same steps as CmdSelectSlot / CmdUseMedkit run through the combatant's private loadout.
    // Everything is looked up once; the calls themselves are rare (a weapon switch, a medkit).
    public static class BotServerActions
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static readonly MethodInfo SelectSlotMethod = typeof(Combatant).GetMethod("ServerSelectSlot", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null);
        static readonly MethodInfo UseMedkitMethod = typeof(Combatant).GetMethod("ServerUseMedkit", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        static readonly PropertyInfo DisplayNameProperty = typeof(Combatant).GetProperty("ServerDisplayName", BindingFlags.Instance | BindingFlags.Public);
        static readonly FieldInfo LoadoutField = typeof(Combatant).GetField("_loadout", Any);
        static readonly FieldInfo HealthField = typeof(Combatant).GetField("_health", Any);
        static readonly MethodInfo PushMethod = typeof(Combatant).GetMethod("Push", Any, null, Type.EmptyTypes, null);
        static readonly object[] OneArg = new object[1];
        static bool _warned;

        // Same checks and effect as Combatant.CmdPickup.
        public static bool Pickup(Combatant bot, LootItem item)
        {
            if (bot == null || item == null || !bot.IsServerInitialized || bot.Dead.Value || !item.IsSpawned) return false;
            if ((item.transform.position - bot.transform.position).sqrMagnitude > Combatant.PickupRange * Combatant.PickupRange) return false;
            if (!bot.ServerGive(item.ToEntry())) return false;
            item.Despawn();
            return true;
        }

        // Same effect as Combatant.CmdSelectSlot.
        public static bool SelectSlot(Combatant bot, int slot)
        {
            if (bot == null || !bot.IsServerInitialized || bot.Dead.Value) return false;
            if (SelectSlotMethod != null)
            {
                OneArg[0] = slot;
                return !(SelectSlotMethod.Invoke(bot, OneArg) is bool done) || done;
            }
            if (!(LoadoutOf(bot) is Loadout loadout) || !loadout.SelectSlot(slot)) return false;
            PushMethod.Invoke(bot, null);
            return true;
        }

        // Same effect as Combatant.CmdUseMedkit.
        public static bool UseMedkit(Combatant bot)
        {
            if (bot == null || !bot.IsServerInitialized || bot.Dead.Value) return false;
            if (UseMedkitMethod != null) return !(UseMedkitMethod.Invoke(bot, null) is bool done) || done;
            if (!(LoadoutOf(bot) is Loadout loadout) || !(HealthField.GetValue(bot) is Health health)) return false;
            if (!loadout.TryUseMedkit(health)) return false;
            PushMethod.Invoke(bot, null);
            return true;
        }

        // The name the match shows for this combatant, where Combatant offers a place for it.
        public static bool SetDisplayName(Combatant bot, string name)
        {
            if (bot == null || DisplayNameProperty == null || !DisplayNameProperty.CanWrite) return false;
            DisplayNameProperty.SetValue(bot, name);
            return true;
        }

        static object LoadoutOf(Combatant bot)
        {
            if (LoadoutField != null && HealthField != null && PushMethod != null) return LoadoutField.GetValue(bot);
            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("[Bot] Combatant has neither ServerSelectSlot/ServerUseMedkit nor the private loadout this adapter knows; bots cannot switch weapons or heal.");
            }
            return null;
        }
    }
}
