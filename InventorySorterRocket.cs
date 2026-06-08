using System.Collections.Generic;
using System.Reflection;
using Rocket.API;
using Rocket.Core.Plugins;
using Rocket.Unturned.Chat;
using Rocket.Unturned.Player;
using SDG.Unturned;
using UnityEngine;

[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace InventorySorter.Rocket
{
    public sealed class InventorySorterPlugin : RocketPlugin
    {
    }

    public sealed class CommandSort : IRocketCommand
    {
        public AllowedCaller AllowedCaller { get { return AllowedCaller.Player; } }
        public string Name { get { return "sort"; } }
        public string Help { get { return "Sorts your inventory or a looked-at storage."; } }
        public string Syntax { get { return "[inventory|storage]"; } }
        public List<string> Aliases { get { return new List<string>(); } }
        public List<string> Permissions { get { return new List<string>(); } }

        public void Execute(IRocketPlayer caller, string[] command)
        {
            UnturnedPlayer rocketPlayer = caller as UnturnedPlayer;
            if (rocketPlayer == null || rocketPlayer.Player == null)
            {
                return;
            }

            if (command.Length == 0)
            {
                Say(rocketPlayer, "/sort inventory - Sort your inventory.");
                Say(rocketPlayer, "/sort storage - Sort the storage you are looking at.");
                return;
            }

            string target = command[0].ToLowerInvariant();
            if (target == "inventory")
            {
                int count;
                SortResult result = InventorySortEngine.SortInventory(
                    rocketPlayer.Player,
                    out count);
                SendResult(rocketPlayer, result, count, "inventory");
                return;
            }

            if (target == "storage")
            {
                InteractableStorage storage =
                    InventorySortEngine.GetLookedAtStorage(rocketPlayer.Player);
                if (!CanSortStorage(rocketPlayer.Player, storage))
                {
                    Say(rocketPlayer,
                        "Look at a storage you can access.");
                    return;
                }

                int count;
                SortResult result = InventorySortEngine.SortStorage(
                    storage,
                    out count);
                SendResult(rocketPlayer, result, count, "storage");
                return;
            }

            Say(rocketPlayer, "Usage: /sort [inventory|storage]");
        }

        private static bool CanSortStorage(
            Player player,
            InteractableStorage storage)
        {
            return storage != null &&
                   storage.checkInteractable() &&
                   (storage.checkStore(
                        player.channel.owner.playerID.steamID,
                        player.quests.groupID) ||
                    (storage.isOpen && storage.opener == player));
        }

        private static void SendResult(
            UnturnedPlayer player,
            SortResult result,
            int count,
            string target)
        {
            if (result == SortResult.Success)
            {
                Say(player, "Sorted " + count + " items in your " + target + ".");
            }
            else if (result == SortResult.NothingToSort)
            {
                Say(player, "There are not enough items to sort.");
            }
            else if (result == SortResult.CouldNotPack)
            {
                Say(player, "Could not find a safe layout. Nothing was changed.");
            }
            else
            {
                Say(player, "Sorting failed and the original layout was restored.");
            }
        }

        private static void Say(UnturnedPlayer player, string message)
        {
            UnturnedChat.Say(player, message, Color.yellow);
        }
    }
}
