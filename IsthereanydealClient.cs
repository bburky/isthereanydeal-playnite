using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace IsthereanydealCollectionSync
{
    using static Common;

    public class IsthereanydealClient
    {
        public event EventHandler OnAuthenticated;

        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly Plugin plugin;
        public ItadApi Api { get; private set; }
        internal Database Database { get => DatabaseProxy.Database; }
        internal Category Category { get; private set; }
        internal string Username { get; private set; }
        public Settings Settings { get; set; }
        private DatabaseProxy DatabaseProxy { get; }

        public IsthereanydealClient(Plugin plugin, Settings settings)
        {
            this.plugin = plugin;
            Settings = settings;
            Api = new ItadApi(settings);
            DatabaseProxy = DatabaseProxy.LoadOrInit(plugin);

            _ = InitUsername();
            logger.Debug("Client initialized");
        }

        private async Task InitUsername()
        {
            try
            {
                logger.Info("Getting username");
                Username = await Api.GetUsername();

                OnAuthenticated?.Invoke(this, EventArgs.Empty);
            }
            catch (ITADException ex)
            {
                logger.Error(ex, $"Failed to get username");
            }
        }

        public bool IsUserLoggedIn()
        {
            return !(Username is null);
        }

        public void Login()
        {
            logger.Info("Start login");
            var oauth = new OauthCodeExchange();
            using (var webView = plugin.PlayniteApi.WebViews.CreateView(500, 700))
            {
                webView.LoadingChanged += async (s, e) =>
                {
                    string address = webView.GetCurrentAddress();
                    logger.Debug($"WebView: \"{address}\"");

                    try
                    {
                        if (oauth.TryInitCode(address))
                        {
                            await oauth.GetTokens(Api);
                            Username = await Api.GetUsername();
                            OnAuthenticated?.Invoke(this, EventArgs.Empty);
                            webView.Close();
                        }
                    }
                    catch (ITADException err)
                    {
                        webView.Close();
                        plugin.PlayniteApi.Dialogs.ShowErrorMessage(ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncAuthenticationError"), ResourceProvider.GetString("LOCIsThereAnyDealCollectionSyncErrorCaption"));
                        logger.Error(err, $"Error in WebView during authentication");
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
        public async Task<ImportResult> Import(ICollection<Game> games)
        {
            logger.Info($"Importing {games.Count} games");
            var lookUpGameIdTask = Api.LookUpGameId(games.Select(game => game.Name).ToArray());
            var getCopiesTask = Api.GetCopies();
            RemoveCategoryFromDatabase(plugin.PlayniteApi, Category);

            Task<ICollection<string>> getWaitlistTask = null;

            if (!Settings.RemoveFromWaitlist)
            {
                logger.Info($"Plan to remove games from waitlist");
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
                
                string loggerEntry = $"{game.Name}/{game.Source}/{shop?.ToString() ?? "null"}";
                logger.Debug(loggerEntry);

                if (Settings.SkipSteam && shop == ItadShop.Steam ||
                    Settings.SkipGog && shop == ItadShop.Gog ||
                    game.Source is null && Settings.SkipNoSource)
                {
                    importResult.SkippedGames.Add(game);
                    continue;
                }

                if (gameIds.TryGetValue(game.Name, out string gameItadId) && !(gameItadId is null))
                {
                    logger.Debug($"{loggerEntry}/{gameItadId}");

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

            logger.Info($"Imported({importResult.ImportedGames.Count})\nSkipped({importResult.SkippedGames.Count})\nFailed({importResult.FailedGames})");

            if (toBeAddedCopies.HasItems())
            {
                logger.Info("Plan to add copy");
                copiesTasks.Add(Api.AddCopies(toBeAddedCopies));
            }

            if (toBeUpdatedCopies.HasItems())
            {
                logger.Info("Plan to update copy");
                copiesTasks.Add(Api.UpdateCopies(toBeUpdatedCopies));
            }

            var resultTask = Task.WhenAll(copiesTasks);

            if (!Settings.RemoveFromWaitlist && waitlist.HasItems())
            {
                resultTask = resultTask.ContinueWith(async (task) =>
                {
                    // ITAD removes games upon collection, so
                    // re-adding them back
                    logger.Info("Removing games from waitlist");
                    await Api.AddToWaitlist(waitlist);
                }, TaskContinuationOptions.OnlyOnRanToCompletion).Unwrap();
            }

            await resultTask;
            logger.Info("Completed import web requests");

            if (importResult.FailedGames.HasItems())
            {
                if (Settings.FilterFaileds)
                {
                    logger.Info($"Start applying category to failed games");

                    if (Category is null)
                    {
                        logger.Info("Creating new category");
                        Category = new Category(Database.CategoryName);
                        Database.CategoryId = Category.Id;
                        _ = Task.Run(DatabaseProxy.Save);
                    }

                    if (!plugin.PlayniteApi.Database.Categories.Contains(Category))
                    {
                        logger.Info("Adding category to Playnite"); plugin.PlayniteApi.Database.Categories.Add(Category);
                    }

                    foreach (var game in importResult.FailedGames) {
                        AddCategory(game, Category);
                    }
                }
            }

            logger.Info("Completed Import");
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
    }

    public class ImportResult
    {
        public ICollection<Game> FailedGames { get; set; } = new List<Game>();
        public ICollection<Game> SkippedGames { get; set; } = new List<Game>();
        public ICollection<Game> ImportedGames { get; set; } = new List<Game>();
    }
}
