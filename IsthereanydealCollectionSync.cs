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
    using static Common;

    public class IsthereanydealCollectionSync : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private IsthereanydealCollectionSyncSettingsViewModel viewModel;
        public readonly IsthereanydealClient client;
        private dynamic duplicateHider;
        public override Guid Id { get; } = Guid.Parse("1f1c327f-8896-47de-950c-c92dc9fab556");

        public IsthereanydealCollectionSync(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            var settings = LoadPluginSettings<Settings>();

            if (settings is null)
            {
                logger.Warn("No settings found or not loaded. Created new one.");
                settings = new Settings();
            }

            client = new IsthereanydealClient(this, settings);
            viewModel = new IsthereanydealCollectionSyncSettingsViewModel(this);
            logger.Info("Completed plugin initialization");
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                MenuSection = "@" + ResourceProvider.GetString("LOCIsThereAnyDealCollectionSync"),
                Description = ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncMainMenuImport"),
                Action = (itemArgs) =>
                {
                    ICollection<Game> games = PlayniteApi.Database.Games;
                    var syncHidden = client.Settings.SyncHidden;

                    if (!syncHidden)
                    {
                        games = PlayniteApi.Database.Games.Where((game) => !game.Hidden).ToArray();
                    }

                    logger.Info($"Start importing games from MainMenu (SyncHidden: {syncHidden})");
                    Import(games);
                }
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncGameMenuImport"),
                Action = (itemArgs) =>
                {
                    var hasDh = !(duplicateHider is null);
                    var syncDh = client.Settings.SyncDuplicateHider;
                    logger.Info($"Start importing games from GameMenu (DH: {hasDh}, SyncDH: {syncDh})");

                    if (hasDh && syncDh)
                    {
                        Import(itemArgs.Games.SelectMany(GetCopiesFromDuplicateHider).ToArray());
                    }
                    else
                    {
                        Import(itemArgs.Games);
                    }
                }
            };
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return viewModel;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new IsthereanydealCollectionSyncSettingsView();
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (client.Database.CategoryId != Guid.Empty)
            {
                try
                {
                    logger.Info("Remove category");
                    PlayniteApi.Database.Categories.Remove(client.Database.CategoryId);
                }
                catch
                {

                }
            }

            var duplicateHiderGuid = Guid.Parse("382f8003-8ed0-4e47-ae93-05b43c9c6c32");

            duplicateHider = PlayniteApi.Addons.Plugins.FirstOrDefault(p => p.Id == duplicateHiderGuid);

            if (!(duplicateHider is null))
            {
                logger.Info("Detected DuplicateHider");
            }
        }

        private List<Game> GetCopiesFromDuplicateHider(Game game)
        {
            if (duplicateHider is null)
            {
                return new List<Game> { game };
            }

            var games = duplicateHider.GetCopies(game);

            if (games is List<Game>)
            {
                return games;
            }
            else
            {
                return new List<Game> { game };
            }
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
                        logger.Info("User not logged in. Stop import.");
                        PlayniteApi.Dialogs.ShowErrorMessage(ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncErrorMessageNotLoggedIn"), ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncErrorCaption"));
                        return;
                    }

                    ImportResult importResult = await client.Import(games);

                    var resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportMixed", importResult.ImportedGames.Count,
                        importResult.SkippedGames.Count,
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

                    PlayniteApi.Dialogs.ShowMessage(resultDialogText, ResourceProvider.GetString("LOCIsThereAnyDealCollectionSync"));

                    if (importResult.FailedGames.HasItems() && client.Settings.FilterFaileds)
                    {
                        PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                        {
                            logger.Info("Filtering failed-to-sync games");
                            FilterPreset preset = new FilterPreset
                            {
                                Settings = new FilterPresetSettings
                                {
                                    Category = new IdItemFilterItemProperties(client.Category.Id)
                                }
                            };
                            PlayniteApi.MainView.ApplyFilterPreset(preset);
                        });
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Import failed");
                    PlayniteApi.Dialogs.ShowErrorMessage(ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncImportError"), ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncErrorCaption"));
                }
            }), new GlobalProgressOptions(dialogText));
        }
    }
}