using System;
using System.Reflection;
using Game.Core;
using Game.Net;
using UnityEngine;

namespace Game.AI
{
    // The server calls a bot makes where Combatant only offers an owner request (Cmd*), which a bot cannot send:
    // it has no owning connection. Each call does exactly what the request does.
    public static class BotServerActions
    {
        static readonly PropertyInfo DisplayNameProperty = typeof(Combatant).GetProperty("ServerDisplayName", BindingFlags.Instance | BindingFlags.Public);

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
        public static bool SelectSlot(Combatant bot, int slot) => bot != null && bot.ServerSelectSlot(slot);

        // Same effect as Combatant.CmdUseMedkit.
        public static bool UseMedkit(Combatant bot) => bot != null && bot.ServerUseMedkit();

        // The name the match shows for this combatant, where Combatant offers a place for it.
        public static bool SetDisplayName(Combatant bot, string name)
        {
            if (bot == null || DisplayNameProperty == null || !DisplayNameProperty.CanWrite) return false;
            DisplayNameProperty.SetValue(bot, name);
            return true;
        }
    }
}
