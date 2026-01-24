using IsthereanydealCollectionSync.Models;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

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

            if (!viewModel.Settings.SyncHidden)
            {
                games = PlayniteApi.Database.Games.Where((game) => !game.Hidden).ToArray();
            }

            // The ITAD Profile sync API requires a known shop id
            // TODO: try to work with ITAD to add support for unknown shops
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

            try
            {
                var result = await client.ProfilesSyncCollection(games);
            }
            catch (SyncException ex)
            {
                logger.Warn(ex, ex.Message);
                if (background)
                {
                    // TODO: localize message
                    string text = $"{ResourceProvider.GetString("LOCIsThereAnyDealCollectionSync")}\n\n{ex.Message}";
                    PlayniteApi.Notifications.Add(notificationId.ToString(), text, NotificationType.Error);
                }
                else
                {
                    // TODO: localize message
                    PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, ResourceProvider.GetString("LOCIsThereAnyDealCollectionSync"));
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, ex.Message);
                // Unconditionally show dialog? Other Exception types are unexpected
                PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, ResourceProvider.GetString("LOCIsThereAnyDealCollectionSync"));
            }
        }
        internal void ClearNotifications()
        {
            PlayniteApi.Notifications.Remove(notificationId.ToString());
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
            // TODO: consider debouncing this. In theory well behaved plugins should batch updates, but we can debounce to be safe.
            if (viewModel.Settings.AutoRunOnLibraryUpdate)
            {
                SyncGames(PlayniteApi.Database.Games, true);
            }
        }
    }
}