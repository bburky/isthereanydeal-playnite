using IsthereanydealCollectionSync.Models;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealCollectionSync : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IsthereanydealCollectionSyncSettingsViewModel viewModel;
        internal readonly IsthereanydealClient client;
        public override Guid Id { get; } = Guid.Parse("1f1c327f-8896-47de-950c-c92dc9fab556");
        private readonly Guid notificationId;

        public IsthereanydealCollectionSync(IPlayniteAPI api) : base(api)
        {
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };

            viewModel = new IsthereanydealCollectionSyncSettingsViewModel(this);
            client = new IsthereanydealClient(this, logger);
            notificationId = Guid.NewGuid();
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                MenuSection = "@",
                Description = ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncMainMenuImport"),
                Action = (itemArgs) =>
                {
                    SyncGames(PlayniteApi.Database.Games, false);
                }
            };
        }

        /// <summary>
        /// Import games to IsThereAnyDeal collection.<br/>
        /// If <paramref name="background"/> is true, import will run in background and send notification when finished.
        /// </summary>
        /// <param name="games">Games to import</param>
        /// <param name="background">Show message box (false) or send notification (true) during the process</param>
        public async void SyncGames(ICollection<Game> games, bool background)
        {
            logger.Info($"Start syncing games (background: {background})");

            // TODO show progress UI in Playnite. Can always use the background progress status type I think, this isn't blocking the user in Playnite.

            if (!viewModel.Settings.SyncHidden)
            {
                games = PlayniteApi.Database.Games.Where((game) => !game.Hidden).ToArray();
            }

            // The ITAD Profile sync API requires a known shop id
            // TODO: try to work with ITAD to add support for unknown shops
            // Maybe remove this settng entirely temporarily, and include a note in the settings UI to inform users
            //if (viewModel.Settings.SkipNoSource)
            if (true)
            {
                games = games.Where(g => ItadShopExtension.FromGameSource(g.Source) != ItadShop.Unknown).ToArray();
            }

            if (!games.HasItems())
            {
                logger.Info("No games to sync");
                return;
            }


            ProfilesSyncCollectionResponse result = null;
            try
            {
                result = await client.ProfilesSyncCollection(games);
            }
            catch (ITADException ex)
            {
                logger.Warn(ex, ex.Message);
                if (background)
                {
                    // TODO: localize message
                    string text = $"{ResourceProvider.GetString("LOCIsThereAnyDealCollectionSync")}\n\n{ex.Message}";
                    // TODO: distinguish between error types, and only make some of them open settings on click (if login is needed)
                    ShowNotification(text, NotificationType.Error, true);
                }
                else
                {
                    // TODO: localize message
                    PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, ResourceProvider.GetString("LOCIsThereAnyDealCollectionSync"));
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Unexpected error during ITAD collection sync");
                //TODO: decide if this should unconditionally show dialog? Other Exception types are unexpected, could trigger dialogs from background syncs though
                PlayniteApi.Dialogs.ShowErrorMessage($"Unexpected error during ITAD collection sync:\n{ex.Message}", ResourceProvider.GetString("LOCIsThereAnyDealCollectionSync"));
            }

            if (!background)
            {
                // TODO: localize
                PlayniteApi.Dialogs.ShowMessage($"IsThereAnyDeal profile synced successfully\n\n{result?.total} total games synced\n{result?.added} new games added\n{result?.removed} games removed", ResourceProvider.GetString("LOCIsThereAnyDealCollectionSync"));
            }
        }

        internal void ClearNotifications()
        {
            // Playnite doesn't care if we delete a non-existing notification
            PlayniteApi.Notifications.Remove(notificationId.ToString());
        }

        private void ShowNotification(string message, NotificationType type, bool openSettingsOnClick)
        {
            // Clear existing notification first (no use case for multiple notifications currently)
            ClearNotifications();

            NotificationMessage notification;
            if (openSettingsOnClick)
            {
                notification = new NotificationMessage(notificationId.ToString(), message, type, () =>
                {
                    this.OpenSettingsView();
                });
            }
            else
            {
                notification = new NotificationMessage(notificationId.ToString(), message, type);
            }

            PlayniteApi.Notifications.Add(notification);
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return viewModel;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new IsthereanydealCollectionSyncSettingsView();
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // TODO: debounce this. In theory Playnite itself and well behaved plugins should batch updates, but we can debounce to be safe. Shouldn't need to sync too often, maybe a debounce of 10 min even
            if (viewModel.Settings.AutoRunOnLibraryUpdate)
            {
                SyncGames(PlayniteApi.Database.Games, true);
            }
        }
    }
}