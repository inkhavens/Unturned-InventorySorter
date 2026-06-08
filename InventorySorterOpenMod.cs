using System;
using System.Reflection;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using OpenMod.API.Commands;
using OpenMod.API.Permissions;
using OpenMod.API.Plugins;
using OpenMod.Core.Commands;
using OpenMod.Unturned.Plugins;
using OpenMod.Unturned.Users;
using SDG.Unturned;

[assembly: PluginMetadata(
    "InventorySorter.OpenMod",
    DisplayName = "Inventory Sorter",
    Author = "InventorySorter")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace InventorySorter.OpenMod
{
    public sealed class InventorySorterOpenModPlugin : OpenModUnturnedPlugin
    {
        private readonly IPermissionRegistry permissionRegistry;

        public InventorySorterOpenModPlugin(
            IPermissionRegistry permissionRegistry,
            IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            this.permissionRegistry = permissionRegistry;
        }

        protected override UniTask OnLoadAsync()
        {
            permissionRegistry.RegisterPermission(
                this,
                "commands.sort",
                "Allows use of /sort.",
                PermissionGrantResult.Grant);
            return UniTask.CompletedTask;
        }
    }

    [Command("sort")]
    [CommandDescription("Sorts your inventory or a looked-at storage.")]
    [CommandSyntax("[inventory|storage]")]
    [CommandActor(typeof(UnturnedUser))]
    public sealed class CommandSort : CommandBase
    {
        public CommandSort(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
        }

        public override async Task ExecuteAsync()
        {
            await UniTask.SwitchToMainThread();

            UnturnedUser user = Context.Actor as UnturnedUser;
            if (user == null || user.Player == null || user.Player.Player == null)
            {
                return;
            }

            if (Context.Parameters.Count == 0)
            {
                await PrintAsync("/sort inventory - Sort your inventory.");
                await PrintAsync(
                    "/sort storage - Sort the storage you are looking at.");
                return;
            }

            string target = Context.Parameters[0].ToLowerInvariant();
            Player player = user.Player.Player;

            if (target == "inventory")
            {
                int count;
                SortResult result = InventorySortEngine.SortInventory(
                    player,
                    out count);
                await SendResult(result, count, "inventory");
                return;
            }

            if (target == "storage")
            {
                InteractableStorage storage =
                    InventorySortEngine.GetLookedAtStorage(player);
                if (!CanSortStorage(player, storage))
                {
                    await PrintAsync(
                        "Look at a storage you can access.");
                    return;
                }

                int count;
                SortResult result = InventorySortEngine.SortStorage(
                    storage,
                    out count);
                await SendResult(result, count, "storage");
                return;
            }

            await PrintAsync("Usage: /sort [inventory|storage]");
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

        private async Task SendResult(
            SortResult result,
            int count,
            string target)
        {
            if (result == SortResult.Success)
            {
                await PrintAsync(
                    "Sorted " + count + " items in your " + target + ".");
            }
            else if (result == SortResult.NothingToSort)
            {
                await PrintAsync("There are not enough items to sort.");
            }
            else if (result == SortResult.CouldNotPack)
            {
                await PrintAsync(
                    "Could not find a safe layout. Nothing was changed.");
            }
            else
            {
                await PrintAsync(
                    "Sorting failed and the original layout was restored.");
            }
        }
    }
}
