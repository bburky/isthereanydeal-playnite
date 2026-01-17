using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using static IsthereanydealCollectionSync.Common;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealCollectionSync : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public IsthereanydealCollectionSyncSettingsViewModel Settings { get; }
        public readonly IsthereanydealClient client;
        public readonly IPlayniteAPI playniteApi;

        public override Guid Id { get; } = Guid.Parse("1f1c327f-8896-47de-950c-c92dc9fab556");

        public IsthereanydealCollectionSync(IPlayniteAPI api) : base(api)
        {
            Settings = new IsthereanydealCollectionSyncSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
            client = new IsthereanydealClient(this, Settings);
            playniteApi = api;
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "Add to IsThereAnyDeal Collection",
                Action = (itemArgs) =>
                {
                    PlayniteApi.Dialogs.ActivateGlobalProgress(new Func<GlobalProgressActionArgs, Task>(async (progressArgs) =>
                    {
                        try
                        {
                            //TODO: globalProgressActionArgs.CancelToken.IsCancellationRequested and add true to GlobalProgressOptions

                            if (!client.IsUserLoggedIn())
                            {
                                PlayniteApi.Dialogs.ShowErrorMessage("User not logged in.\n\nLog into IsThereAnyDeal in \"Add-ons...\" settings", "IsThereAnyDeal Collection Sync");
                                return;
                            }
                            var failedGames = await client.Import(itemArgs.Games);
                            PlayniteApi.Dialogs.ShowMessage($"Successfully added {itemArgs.Games.Count} games.\n{failedGames.Count} failed.");
                        }
                        catch (Exception ex)
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, "IsThereAnyDeal Collection Sync Error");
                        }
                    }), new GlobalProgressOptions($"Importing games into IsThereAnyDeal collection"));
                }
            };

            yield return new GameMenuItem
            {
                Description = "Debug Game Info",
                Action = (itemArgs) =>
                {
                    var game = itemArgs.Games[0];
                    PlayniteApi.Dialogs.ShowMessage(
                        $"Name = \"{game.Name}\"\n" +
                        $"Source =\"{game.Source}\"\n" +
                        $"SourceID = \"{game.SourceId}\""
                    );
                }
            };

            var categoryName = "FooCate";

            yield return new GameMenuItem
            {
                Description = $"Add category \"{categoryName}\"",
                Action = (itemArgs) =>
                {
                    foreach (var item in playniteApi.Database.Categories)
                    {
                        System.Diagnostics.Debug.WriteLine($"{item.Id} => \"{item.Name}\" ({categoryName == item.Name})");
                    }

                    foreach (var game in itemArgs.Games)
                    {
                        if (game.Categories is null)
                        {
                            game.CategoryIds = new List<Guid> { playniteApi.Database.Categories.First(cate => cate.Name == categoryName).Id };
                        }
                        else
                        {
                            game.Categories.Add(playniteApi.Database.Categories.First(cate => cate.Name == categoryName));
                        }
                    }
                }
            };

            yield return new GameMenuItem()
            {
                Description = $"Create a new category \"{categoryName}\"",
                Action = (itemArgs) =>
                {
                    var cate = new Playnite.SDK.Models.Category(categoryName);
                    playniteApi.Database.Categories.Add(cate);
                }
            };
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new IsthereanydealCollectionSyncSettingsView();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            RemoveCategoryFromDatabase(playniteApi, client.Category);

            base.OnApplicationStopped(args);
        }
    }
}