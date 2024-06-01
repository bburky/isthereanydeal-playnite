using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Documents;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealClient
    {
        private ILogger logger = LogManager.GetLogger();
        private IsthereanydealCollectionSync plugin;
        private IWebView webView;

        public static Dictionary<string, int> shops;
        private static Dictionary<Guid, string> pluginNames;

        public IsthereanydealClient(IsthereanydealCollectionSync plugin, IWebView webView)
        {
            this.plugin = plugin;
            this.webView = webView;
        }

        async public Task<bool> GetIsUserLoggedIn()
        {
            webView.NavigateAndWait(@"https://isthereanydeal.com/collection/import/");
            // The page isn't loaded immediately, you have to wait for JS to finish to be able to read webView.GetPageText()
            // However, there is some global JS variables populated from a <script> tag that can be read immediately
            var result = await webView.EvaluateScriptAsync("window?.g?.user?.isLoggedIn === true");            
            if (!result.Success)
            {
                throw new Exception($"Failed to import IsThereAnyDeal Collection: {result.Message}");
            }
            return (bool)result.Result;
        }

        public void Login()
        {
            webView.DeleteDomainCookies("isthereanydeal.com");
            webView.LoadingChanged += (s, e) =>
            {
                if (new []{ "https://isthereanydeal.com/collection/import/", "https://isthereanydeal.com/collection/import/#" }.Contains(webView.GetCurrentAddress()))
                {
                    webView.Close();
                }
            };
            webView.Navigate("https://isthereanydeal.com/collection/import/#/user:login");
            webView.OpenDialog();
        }

        async public Task<string> Import(string importJson, bool importModeReplace, bool removeFromWaitlist)
        {
            webView.NavigateAndWait("https://isthereanydeal.com/collection/import/");

            var mode = importModeReplace ? "replace" : "ignore";
            var waitlist = removeFromWaitlist ? "true" : "false";
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(importJson));
            var script = @"
            (async (b64, mode, waitlist) => {
                const formData = new FormData();
                formData.append('file', `data:application/json;base64,${b64}`);
                formData.append('mode', mode);
                if (waitlist) {
                    formData.append('wait_remove', 1);
                }
                const response = await fetch('https://isthereanydeal.com/collection/import/', {
                    'headers': {
                        'accept': 'application/json',
                        'itad-sessiontoken': g.user.token,
                    },
                    'body': formData,
                    'method': 'POST',
                    'mode': 'cors',
                    'credentials': 'include',
                });
                return await response.text();
            })" + $"('{b64}', '{mode}', {waitlist})";
            var result = await webView.EvaluateScriptAsync(script);
            if (!result.Success)
            {
                throw new Exception($"Failed to import IsThereAnyDeal Collection: {result.Message}");
            }
            return result.Result.ToString();
        }

        async public Task<string> generateImportJson(string importGroup, List<Game> games)
        {
            var import = new ImportJSON
            {
                version = "03",
                data = new List<ImportJSONData>
                {
                    new ImportJSONData
                    {
                        group = importGroup,
                        public_ = false,
                        games = await Task.WhenAll(games.Select(async game => new ImportJSONGame
                            {
                                title = game.Name,
                                copies = new List<ImportJSONGameCopy>
                                {
                                    await getCopyForGame(game),
                                }
                                // TODO: could look up game by steam app id, but this requires API authentication
                            }
                        )),
                    }
                }
            };
            var json = Serialization.ToJson(import);
            return json;
        }

        async public Task<ImportJSONGameCopy> getCopyForGame(Game game)
        {
            if (shops == null)
            {
                using (var client = new HttpClient())
                {
                    var currentShops = await client.GetStringAsync("https://api.isthereanydeal.com/service/shops/v1");
                    shops = Serialization.FromJson<List<ShopsJSON>>(currentShops).ToDictionary(s => s.title, s => s.id);
                }
            }
            if (pluginNames == null)
            {
                pluginNames = plugin.PlayniteApi.Addons.Plugins.OfType<LibraryPlugin>().ToDictionary(p => p.Id, p => p.Name);
            }

            string source = game.Source?.Name;
            if (source == null)
            {
                // Fall back to the plugin name if the source is missing
                // Some older library plugins may have failed to set the source and this old data may still be in the library
                if (!pluginNames.TryGetValue(game.PluginId, out source))
                {
                    source = null;
                }
            }
            if (source == null)
            {
                // Manually added games have neither source nor plugin info. No info to put into the ITAD copies data.
                return new ImportJSONGameCopy
                {
                    note = "Playnite",
                    redeemed = true,
                };
            }

            // Normalize the source to match the ITAD shop names
            if (source == "Amazon" || source == "Amazon Games" )
            {
                source = "Amazon";
            }
            else if (source == "Battle.net")
            {
                source = "Blizzard";
            }
            else if (source == "EA app" || source == "Origin")
            {
                source = "EA Store";
            }
            else if (source == "Epic")
            {
                source = "Epic Game Store";
            }
            else if (source == "GOG")
            {
                source = "GOG";
            }
            else if (source == "Humble")
            {
                source = "Humble Store";
            }
            else if (source == "itch.io")
            {
                source = "Itch.io";
            }
            else if (source == "Steam")
            {
                source = "Steam";
            }
            else if (source == "Ubisoft Connect" || source == "Uplay")
            {
                source = "Ubisoft Store";
            }
            else if (source == "Indiegala")
            {
                source = "IndieGala Store";
            }
            else if (source == "Xbox") // TODO is this still accurate?
            {
                source = "Microsoft Store";
            }

            if (shops.TryGetValue(source, out var id))
            {
                return new ImportJSONGameCopy
                {
                    shop = id,
                    redeemed = true,
                };
            }

            return new ImportJSONGameCopy
            {
                note = source,
                redeemed = true,
            };
        }

        public class ImportJSON
        {
            public string version;

            public List<ImportJSONData> data;
        }
        public class ImportJSONData
        {
            public string group;

            public ImportJSONGame[] games;

            [SerializationPropertyName("public")]
            public bool public_;
        }
        public class ImportJSONGame
        {
            public string id;

            public string title;

            public List<ImportJSONGameCopy> copies;
        }
        public class ImportJSONGameCopy
        {
            public int? shop;

            public string note;

            public int? added;

            public bool redeemed;
        }

        public class ShopsJSON
        {
            public int id;

            public string title;
        }
    }
}
