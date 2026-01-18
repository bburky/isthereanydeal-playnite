using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealCollectionSync : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public IsthereanydealCollectionSyncSettingsViewModel Settings { get; }
        public readonly IsthereanydealClient client;

        public override Guid Id { get; } = Guid.Parse("1f1c327f-8896-47de-950c-c92dc9fab556");

        public IsthereanydealCollectionSync(IPlayniteAPI api) : base(api)
        {
            Settings = new IsthereanydealCollectionSyncSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
            client = new IsthereanydealClient(this, Settings);
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                MenuSection = "@" + Localized("LOCIsThereAnyDealCollectionSync"),
                Description = "Import all games",
                Action = (itemArgs) =>
                {
                    Import(PlayniteApi.Database.Games);
                }
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = Localized("LOCIsThereAnyDealCollectionSyncImportMenu"),
                Action = (itemArgs) =>
                {
                    Import(itemArgs.Games);
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
            client.RemoveCategoryFromDatabase();
        }

        public static string Localized(string key)
        {
            return ResourceProvider.GetString(key);
        }

        public static string Localized(string key, params object[] args)
        {
            return string.Format(ResourceProvider.GetString(key), args);
        }

        private void Import(ICollection<Game> games)
        {
            string dialogText = Localized("LOCIsThereAnyDealCollectionSyncImportMessageMultiple", games.Count);

            if (games.Count == 1)
            {
                dialogText = Localized("LOCIsThereAnyDealCollectionSyncImportMessageSingle", games.First().Name);
            }

            PlayniteApi.Dialogs.ActivateGlobalProgress(new Func<GlobalProgressActionArgs, Task>(async (progressArgs) =>
            {
                try
                {
                    //TODO: globalProgressActionArgs.CancelToken.IsCancellationRequested and add true to GlobalProgressOptions

                    if (!client.IsUserLoggedIn())
                    {
                        PlayniteApi.Dialogs.ShowErrorMessage(Localized("LOCIsThereAnyDealCollectionSyncErrorMessageNotLoggedIn"), Localized("LOCIsThereAnyDealCollectionSyncErrorCaption"));
                        return;
                    }

                    ImportResult importResult = await client.Import(games);


                    var resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportMixed", importResult.ImportedGames.Count,
                        importResult.SkippedGames,
                        importResult.FailedGames.Count); 

                    if (games.Count == 1)
                    {
                        var game = games.First();

                        if (importResult.FailedGames.HasItems())
                        {
                            resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportFailureSingle", game.Name);
                        }
                        else if (importResult.SkippedGames.HasItems())
                        {
                            resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportSkippedSingle", game.Name);
                        }
                        else
                        {
                            resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportSucceedSingle", game.Name);
                        }
                    }
                    else
                    {
                        if (importResult.FailedGames.Count == games.Count)
                        {
                            resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportFailureMultiple", games.Count);
                        }
                        else if (importResult.SkippedGames.Count == games.Count)
                        {
                            resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportSkippedMultiple", games.Count);
                        }
                        else if (importResult.ImportedGames.Count == games.Count)
                        {
                            resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportSucceedMultiple", games.Count);
                        }
                    }

                    PlayniteApi.Dialogs.ShowMessage(resultDialogText, ("LOCIsThereAnyDealCollectionSync"));
                }
                catch (Exception ex)
                {
                    PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, Localized("LOCIsThereAnyDealCollectionSyncErrorCaption"));
                }
            }), new GlobalProgressOptions(dialogText));
        }
    }
}