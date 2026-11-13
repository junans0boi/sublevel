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
            // What the bag has no room for stays on the ground.
            if (!bot.ServerGive(item.ToEntry(), out int left)) return false;
            if (left > 0) item.Amount.Value = left;
            else item.Despawn();
            return true;
        }

        // Same effect as Combatant.CmdSelectSlot.
        public static bool SelectSlot(Combatant bot, int slot) => bot != null && bot.ServerSelectSlot(slot);

        // Same effect as Combatant.CmdUseMedkit.
        public static bool UseMedkit(Combatant bot) => bot != null && bot.ServerUseMedkit();

        // Heals the way a player's H does: the best-fitting timed item (bandage, kit, medkit) when the bot has the
        // component for it, else the instant medkit (ADR-116).
        public static bool Heal(Combatant bot)
        {
            if (bot == null) return false;
            var healing = bot.GetComponent<CombatantHealing>();
            // With the component every use is timed (the medkit too); a retry while one runs must not fall through to the instant kit.
            if (healing != null) return healing.Using || healing.ServerUse();
            return bot.ServerUseMedkit();
        }

        // The name the match shows for this combatant, where Combatant offers a place for it.
        public static bool SetDisplayName(Combatant bot, string name)
        {
            if (bot == null || DisplayNameProperty == null || !DisplayNameProperty.CanWrite) return false;
            DisplayNameProperty.SetValue(bot, name);
            return true;
        }
    }
}
