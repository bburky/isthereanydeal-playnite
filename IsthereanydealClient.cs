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
        internal ItadApiCategory[] Categories { get; private set; }
        public event EventHandler InfoUpdate;
        private readonly IsthereanydealCollectionSyncSettings settings;

        public IsthereanydealClient(IsthereanydealCollectionSync plugin, IsthereanydealCollectionSyncSettingsViewModel settings)
        {
            this.plugin = plugin;
            InfoUpdate += settings.OnModelChanged;
            Api = new ItadApi(settings.Settings.credential);
            this.settings = settings.Settings;

            Task.WhenAll(
                InitUsername(), InitCategories()
            ).ContinueWith((task) =>
            {
                if (!(task.Exception is null))
                {
                    InfoUpdate?.Invoke(this, EventArgs.Empty);
                }
            });
        }

        private async Task InitUsername()
        {
            try
            {
                Username = await Api.GetUsername();
                logger.Info($"Logged in as {Username}");
            }
            catch (ITADException err)
            {
                LogInitError("username", err);
            }
        }

        private async Task InitCategories()
        {
            try
            {
                Categories = await Api.GetCategories();
                logger.Info($"Found {Categories.Count()} categories");
            }
            catch (ITADException err)
            {
                LogInitError("categories", err);
            }
        }

        private void LogInitError(string field, ITADException err)
        {
            logger.Error(err, $"Failed to get {field}. User need to restart Code exchange.");
        }

        public bool IsUserLoggedIn()
        {
            return !(Username is null);
        }

        public void Login()
        {
            var oauth = new OauthCodeExchange();
            using (var webView = plugin.playniteApi.WebViews.CreateView(500, 700))
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
                            Categories = await Api.GetCategories();
                            InfoUpdate?.Invoke(this, EventArgs.Empty);
                            webView.Close();
                        }
                    }
                    catch (ITADException err)
                    {
                        webView.Close();
                        plugin.PlayniteApi.Dialogs.ShowErrorMessage($"An error occured during authentication:\n{err.Message}", "Failed to authenticate IsThereAnyDeal");
                        logger.Error(err, $"An error occured during authentication:\n{err.Message}");
                    }
                };

                webView.Navigate(oauth.LoginUrl);
                webView.OpenDialog();
            }
        }

        async public Task Import(List<Game> games)
        {
            var lookUpTask = Api.LookUpGameId(games.Select(game => game.Name).ToArray());
            var gamesGroupedByShop = games
                .Select(game => new { 
                    game.Name, 
                    Source = ItadShopExtension.FromGameSource(game.Source) 
                })
                .GroupBy(game => game.Source, game => game.Name);

            foreach (var shop_games in gamesGroupedByShop)
            {
                System.Diagnostics.Debug.WriteLine($"({shop_games.Key}, {shop_games.Count()})");

                foreach (var game in shop_games)
                {
                    System.Diagnostics.Debug.WriteLine($"{game}");
                }

                System.Diagnostics.Debug.WriteLine("");
            }

            Dictionary<string, string> gameIds = await lookUpTask;

            var tasks = new List<Task>();

            foreach (var shopGames in gamesGroupedByShop)
            {
                ItadShop shop = shopGames.Key;

                var itadCopies = shopGames.Select(gameId =>
                    {
                        if (gameIds.TryGetValue(gameId, out string gameItadId))
                        {
                            return new
                            {
                                Copy = new ItadApiCopyInput(gameItadId, false, shop),
                                Id = gameItadId
                            };
                        }

                        return null;
                    })
                    .Where(copy => !(copy is null))
                    .ToArray();

                var addCopiesTask = Api.AddCopies(itadCopies.Select(copy => copy.Copy).ToArray());

                if (settings.RemoveFromWaitlist)
                {
                    addCopiesTask = addCopiesTask.ContinueWith(async (task) => {
                        await Api.DeleteFromWaitList(itadCopies.Select(copy => copy.Id).ToArray());
                      });
                }

                tasks.Add(addCopiesTask);
            }

            await Task.WhenAll(tasks);
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

        public class ITADException : Exception
        {
            public ITADException(string message) : base(message) { }
        }
    }
}
