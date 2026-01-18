using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealClient
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IsthereanydealCollectionSync plugin;
        public ItadApi Api { get; private set; }
        internal string Username { get; private set; }
        private readonly IsthereanydealCollectionSyncSettingsViewModel viewModel;
        private IsthereanydealCollectionSyncSettings Settings { get => viewModel.Settings; }
        private readonly Database database;
        public Category Category { get; private set; }

        public IsthereanydealClient(IsthereanydealCollectionSync plugin, IsthereanydealCollectionSyncSettingsViewModel settings)
        {
            this.plugin = plugin;
            Api = new ItadApi(settings.Settings.Credential);
            viewModel = settings;
            database = Database.LoadOrInit(plugin);

            if (database.CategoryId != Guid.Empty)
            {
                try
                {
                    plugin.PlayniteApi.Database.Categories.Remove(database.CategoryId);
                }
                catch
                {

                }
            }

            _ = InitUsername();
        }

        private async Task InitUsername()
        {
            try
            {
                Username = await Api.GetUsername();
                logger.Info($"Logged in as {Username}");
            }
            catch (ITADException e)
            {
                logger.Error(e, $"Failed to get username");
            }
        }

        public bool IsUserLoggedIn()
        {
            return !(Username is null);
        }

        public void Login()
        {
            var oauth = new OauthCodeExchange();
            using (var webView = plugin.PlayniteApi.WebViews.CreateView(500, 700))
            {
                webView.LoadingChanged += async (s, e) =>
                {
                    string address = webView.GetCurrentAddress();

                    try
                    {
                        if (oauth.TryInitCode(address))
                        {
                            Api = await oauth.GetTokens();
                            Username = await Api.GetUsername();
                            webView.Close();
                        }
                    }
                    catch (ITADException err)
                    {
                        webView.Close();
                        plugin.PlayniteApi.Dialogs.ShowErrorMessage(ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncAuthenticationError"), ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncErrorCaption"));
                        logger.Error(err, $"Error in WebView during authentication:\n{err.Message}");
                    }
                };

                webView.Navigate(oauth.LoginUrl);
                webView.OpenDialog();
            }
        }

        /// <summary>
        /// Synchronize games to ITAD.
        /// </summary>
        /// <param name="games"></param>
        /// <returns>List of games that failed to synchronize.</returns>
        async public Task<ImportResult> Import(ICollection<Game> games)
        {
            var lookUpGameIdTask = Api.LookUpGameId(games.Select(game => game.Name).ToArray());
            var getCopiesTask = Api.GetCopies();
            RemoveCategoryFromDatabase();

            Task<ICollection<string>> getWaitlistTask = null;

            if (!Settings.RemoveFromWaitlist)
            {
                getWaitlistTask = Api.GetWaitlist();
            }

            IDictionary<string, string> gameIds = await lookUpGameIdTask;
            ICollection<ItadApiCopy> existingCopies = await getCopiesTask;
            var importResult = new ImportResult();
            var copiesTasks = new List<Task>();
            var toBeAddedCopies = new List<ItadApiAddCopyInput>();
            var toBeUpdatedCopies = new List<ItadApiUpdateCopyInput>();
            var waitlist = getWaitlistTask is null ? null : await getWaitlistTask;

            foreach (Game game in games)
            {
                ItadShop? shop = ItadShopExtension.FromGameSource(game.Source);

                if (Settings.SkipSteam && shop == ItadShop.Steam ||
                    Settings.SkipGog && shop == ItadShop.Gog ||
                    game.Source is null && Settings.SkipNoSource)
                {
                    importResult.SkippedGames.Add(game);
                    continue;
                }

                if (gameIds.TryGetValue(game.Name, out string gameItadId) && !(gameItadId is null))
                {
                    var copy = existingCopies
                        .Where(c =>
                            c.game.id == gameItadId &&
                            (c.shop is null ||
                            c.MatchShop(shop))
                        )
                        .OrderByDescending(c => c.shop is null)
                        .FirstOrDefault();

                    if (copy is null)
                    {
                        var toBeAddedCopy = new ItadApiAddCopyInput(gameItadId, false)
                        {
                            shop = shop,
                            note = Settings.Note,
                            tags = Settings.Tags,
                        };

                        if (shop == ItadShop.Epic)
                        {
                            toBeAddedCopy.redeemed = Settings.RedeemEpic;
                        }

                        toBeAddedCopies.Add(toBeAddedCopy);
                        importResult.ImportedGames.Add(game);

                        continue;
                    }

                    if (Settings.ImportMode == ImportMode.Skip)
                    {
                        importResult.SkippedGames.Add(game);
                        continue;
                    }

                    var toBeUpdatedCopy = new ItadApiUpdateCopyInput(copy.id)
                    {
                        shop = shop,
                        note = Settings.Note,
                        tags = Settings.Tags,
                    };

                    if (shop == ItadShop.Epic)
                    {
                        toBeUpdatedCopy.redeemed = Settings.RedeemEpic;
                    }

                    toBeUpdatedCopies.Add(toBeUpdatedCopy);
                    importResult.ImportedGames.Add(game);
                }
                else
                {
                    importResult.FailedGames.Add(game);
                }
            }

            if (toBeAddedCopies.HasItems())
            {
                var copyInput = toBeAddedCopies.ToArray();
                var task = Api.AddCopies(copyInput);
                copiesTasks.Add(task);
            }

            if (toBeUpdatedCopies.HasItems())
            {
                var copyInput = toBeUpdatedCopies.ToArray();
                var task = Api.UpdateCopies(toBeUpdatedCopies);
                copiesTasks.Add(task);
            }

            var resultTask = Task.WhenAll(copiesTasks);

            if (!Settings.RemoveFromWaitlist && waitlist.HasItems())
            {
                resultTask = resultTask.ContinueWith(async (task) =>
                {
                    // ITAD removes games upon collection, so
                    // re-adding them back
                    await Api.AddToWaitlist(waitlist);
                }, TaskContinuationOptions.OnlyOnRanToCompletion).Unwrap();
            }

            await resultTask;

            if (importResult.FailedGames.HasItems())
            {
                if (Category is null)
                {
                    Category = new Category(database.CategoryName);
                    database.CategoryId = Category.Id;
                    _ = Task.Run(database.Save);
                    plugin.PlayniteApi.Database.Categories.Add(Category);
                }

                foreach (var game in importResult.FailedGames) {
                    AddCategory(game);
                }
            }

            return importResult;
        }

        //async public Task<ImportJSONGameCopy> getCopyForGame(Game game)
        //{
        //    if (shops == null)
        //    {
        //        using (var client = new HttpClient())
        //        {
        //            var currentShops = await client.GetStringAsync("https://api.isthereanydeal.com/service/shops/v1");
        //            shops = Serialization.FromJson<List<ShopsJSON>>(currentShops).ToDictionary(s => s.title, s => s.id);
        //        }
        //    }
        //    if (pluginNames == null)
        //    {
        //        pluginNames = plugin.PlayniteApi.Addons.Plugins.OfType<LibraryPlugin>().ToDictionary(p => p.Id, p => p.Name);
        //    }

        //    string source = game.Source?.Name;
        //    if (source == null)
        //    {
        //        // Fall back to the plugin name if the source is missing
        //        // Some older library plugins may have failed to set the source and this old data may still be in the library
        //        if (!pluginNames.TryGetValue(game.PluginId, out source))
        //        {
        //            source = null;
        //        }
        //    }
        //    if (source == null)
        //    {
        //        // Manually added games have neither source nor plugin info. No info to put into the ITAD copies data.
        //        return new ImportJSONGameCopy
        //        {
        //            note = "Playnite",
        //            redeemed = true,
        //        };
        //    }

        //    // Normalize the source to match the ITAD shop names
        //    // All ITAD shops with a corresponding Playnite library addon are below, some names match so there is no change.
        //    if (source == "Amazon" || source == "Amazon Games" )
        //    {
        //        source = "Amazon";
        //    }
        //    else if (source == "Battle.net")
        //    {
        //        source = "Blizzard";
        //    }
        //    else if (source == "EA app" || source == "Origin")
        //    {
        //        source = "EA Store";
        //    }
        //    else if (source == "Epic")
        //    {
        //        source = "Epic Game Store";
        //    }
        //    else if (source == "GOG")
        //    {
        //        source = "GOG";
        //    }
        //    else if (source == "Humble")
        //    {
        //        source = "Humble Store";
        //    }
        //    else if (source == "itch.io")
        //    {
        //        source = "Itch.io";
        //    }
        //    else if (source == "Steam")
        //    {
        //        source = "Steam";
        //    }
        //    else if (source == "Ubisoft Connect" || source == "Uplay")
        //    {
        //        source = "Ubisoft Store";
        //    }
        //    else if (source == "Indiegala")
        //    {
        //        source = "IndieGala Store";
        //    }
        //    else if (source == "Xbox") // TODO is this still accurate?
        //    {
        //        source = "Microsoft Store";
        //    }
        //    else if (source == "Fanatical")
        //    {
        //        source = "Fanatical";
        //    }

        //    if (shops.TryGetValue(source, out var id))
        //    {
        //        return new ImportJSONGameCopy
        //        {
        //            shop = id,
        //            redeemed = true,
        //        };
        //    }

        //    return new ImportJSONGameCopy
        //    {
        //        note = source,
        //        redeemed = true,
        //    };
        // }

        public void AddCategory(Game game)
        {
            if (game.CategoryIds is null)
            {
                game.CategoryIds = new List<Guid> { Category.Id };
            }
            else
            {
                game.CategoryIds.AddMissing(Category.Id);
            }
        }

        public void RemoveCategoryFromDatabase()
        {
            if (Category is null)
            {
                return;
            }

            // IntelliSense IS LYING!
            // If you try to remove thing that is not
            // in the collection, it throws
            // NullReferenceException.
            try
            {
                plugin.PlayniteApi.Database.Categories.Remove(Category);
            }
            catch
            {

            }
        }
    }

    public class ImportResult
    {
        public ICollection<Game> FailedGames { get; set; } = new List<Game>();
        public ICollection<Game> SkippedGames { get; set; } = new List<Game>();
        public ICollection<Game> ImportedGames { get; set; } = new List<Game>();
    }
}
