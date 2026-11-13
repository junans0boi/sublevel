using Game.Core;

namespace Game.Net
{
    // A component next to a Combatant that takes loot kinds the loadout does not know (armour, throwables, ...).
    // Combatant.ServerGive forwards those here; server only.
    public interface ILootReceiver
    {
        // True when the loot was taken (the item on the ground is then removed).
        bool ServerTryTake(LootEntry loot);
    }
}
