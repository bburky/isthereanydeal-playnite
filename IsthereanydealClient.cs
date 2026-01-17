using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;

namespace IsthereanydealCollectionSync
{
    using static Common;

    public class IsthereanydealClient
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IsthereanydealCollectionSync plugin;
        public ItadApi Api { get; private set; }
        internal string Username { get; private set; }
        internal ItadApiCategory[] Categories { get; private set; }
        public event EventHandler InfoUpdate;
        private readonly IsthereanydealCollectionSyncSettings settings;
        private readonly Database database;
        public Category Category { get; private set; }

        public IsthereanydealClient(IsthereanydealCollectionSync plugin, IsthereanydealCollectionSyncSettingsViewModel settings)
        {
            this.plugin = plugin;
            InfoUpdate += settings.OnModelChanged;
            Api = new ItadApi(settings.Settings.Credential);
            this.settings = settings.Settings;
            database = Database.LoadOrInit(plugin);

            if (!(database.CategoryId == Guid.Empty))
            {
                try
                {
                    plugin.PlayniteApi.Database.Categories.Remove(database.CategoryId);
                }
                catch
                {

                }
            }

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

        /// <summary>
        /// Synchronize games to ITAD.
        /// </summary>
        /// <param name="games"></param>
        /// <returns>List of games that failed to synchronize.</returns>
        async public Task<IList<Game>> Import(IList<Game> games)
        {
            var lookUpGameIdTask = Api.LookUpGameId(games.Select(game => game.Name).ToArray());
            var getCopiesTask = Api.GetCopies();

            Dictionary<string, string> gameIds = await lookUpGameIdTask;
            ItadApiCopy[] existingCopies = await getCopiesTask;
            var failedGames = new List<Game>();
            var copiesTasks = new List<Task>();
            var toBeAddedCopies = new List<AddCopyInput>();
            var toBeUpdatedCopies = new List<UpdateCopyInput>();

            foreach (Game game in games)
            {
                ItadShop? shop = ItadShopExtension.FromGameSource(game.Source);

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
                        var itadAddCopyInput = new ItadApiAddCopyInput(gameItadId, false)
                        {
                            shop = shop,
                            note = settings.Note,
                            tags = settings.Tags,
                        };

                        var toBeAddedCopy = new AddCopyInput
                        {
                            Game = game,
                            Copy = itadAddCopyInput,
                            Id = gameItadId,
                        };

                        toBeAddedCopies.Add(toBeAddedCopy);

                        continue;
                    }

                    if (settings.ImportMode == ImportMode.Ignore)
                    {
                        continue;
                    }

                    var itadUpdateCopyInput = new ItadApiUpdateCopyInput(copy.id)
                    {
                        shop = shop,
                        note = settings.Note,
                        tags = settings.Tags,
                    };

                    var toBeUpdatedCopy = new UpdateCopyInput
                    {
                        Game = game,
                        Copy = itadUpdateCopyInput,
                        Id = gameItadId,
                    };

                    toBeUpdatedCopies.Add(toBeUpdatedCopy);
                }
                else
                {
                    failedGames.Add(game);
                }
            }

            if (toBeAddedCopies.HasItems())
            {
                var copyInput = toBeAddedCopies.ToArray();
                var task = AddCopyAsync(copyInput);

                if (settings.RemoveFromWaitlist)
                {
                    task = task.ContinueWith(async (t) =>
                    {
                        var waitlist = copyInput.Select(c => new DeleteFromWaitlistInput(c)).ToArray();
                        await DeleteFromWaitlistAsync(waitlist);
                    }).Unwrap();
                }

                copiesTasks.Add(task);
            }

            if (toBeUpdatedCopies.HasItems())
            {
                var copyInput = toBeUpdatedCopies.ToArray();
                var task = UpdateCopyAsync(copyInput);

                if (settings.RemoveFromWaitlist)
                {
                    task = task.ContinueWith(async (t) =>
                    {
                        var waitlist = copyInput.Select(c => new DeleteFromWaitlistInput(c)).ToArray();
                        await DeleteFromWaitlistAsync(waitlist);
                    }).Unwrap();
                }

                copiesTasks.Add(task);
            }

            try
            {
                await Task.WhenAll(copiesTasks);
            }
            catch { }

            if (failedGames.HasItems())
            {
                if (Category is null)
                {
                    Category = new Category(database.CategoryName);
                    database.CategoryId = Category.Id;

                    _ = Task.Run(database.Save);
                }

                foreach (var game in failedGames) {
                    AddCategory(plugin.PlayniteApi, game, Category);
                }
            }

            return failedGames;
        }

        private class AddCopyInput
        {
            public Game Game;
            public ItadApiAddCopyInput Copy;
            public string Id;
        }

        private class UpdateCopyInput
        {
            public Game Game;
            public ItadApiUpdateCopyInput Copy;
            public string Id;
        }

        private class DeleteFromWaitlistInput
        {
            public Game Game;
            public string Id;

            public DeleteFromWaitlistInput() {}

            public DeleteFromWaitlistInput(AddCopyInput copy)
            {
                Game = copy.Game;
                Id = copy.Id;
            }

            public DeleteFromWaitlistInput(UpdateCopyInput copy)
            {
                Game = copy.Game;
                Id = copy.Id;
            }
        }

        async private Task AddCopyAsync(AddCopyInput[] itadCopies)
        {
            try
            {
                await Api.AddCopies(itadCopies.Select(copy => copy.Copy).ToArray());
            }
            catch
            {
                var exception = new ITADException("Failed to add copy");
                exception.Data["games"] = itadCopies.Select(copy => copy.Game).ToArray();

                throw exception;
            }
        }

        async private Task UpdateCopyAsync(UpdateCopyInput[] itadCopies)
        {
            try
            {
                await Api.UpdateCopies(itadCopies.Select(copy => copy.Copy).ToArray());
            }
            catch
            {
                var exception = new ITADException("Failed to update copy");
                exception.Data["games"] = itadCopies.Select(copy => copy.Game).ToArray();

                throw exception;
            }
        }

        async private Task DeleteFromWaitlistAsync(DeleteFromWaitlistInput[] waitlist)
        {
            try
            {
                await Api.DeleteFromWaitList(waitlist.Select(w => w.Id).ToArray());
            }
            catch
            {
                var exception = new ITADException("Failed to remove games from waitlist");
                exception.Data["games"] = waitlist.Select(w => w.Game).ToArray();

                throw exception;
            }
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
