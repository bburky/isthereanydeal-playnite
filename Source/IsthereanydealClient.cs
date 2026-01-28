using IsthereanydealCollectionSync.Models;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.UI.WebControls;
using System.Windows;
using System.Windows.Media.Animation;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealClient
    {
        private const string CLIENT_ID = "5590176f3ebbc4d1";
        private const string SCOPE = "profiles coll_write";
        // Using a # URL fragment causes the final redirect to go to #login_complete?code=... (the query is inside the fragment), this prevents the code from going back to the server, which we don't actually need
        // #login_complete is also a nice sentinel to detect when login is complete
        private const string REDIRECT_URI = "https://isthereanydeal.com/#login_complete";
        private readonly string tokensPath;
        private readonly ILogger logger;
        private readonly Plugin plugin;
        private OauthToken token;

        public IsthereanydealClient(Plugin plugin, ILogger logger)
        {
            this.plugin = plugin;
            this.logger = logger;
            tokensPath = Path.Combine(plugin.GetPluginUserDataPath(), "tokens.json");
        }

        public async Task Login()
        {
            var pkce = new Pkce();

            try
            {
                if (File.Exists(tokensPath))
                {
                    File.Delete(tokensPath);
                }
            } catch (Exception ex)
            {
                throw new ITADException("Failed to delete old tokens file on login", ex);
            }

            var loginUrl = $"https://isthereanydeal.com/oauth/authorize/?client_id={CLIENT_ID}&response_type=code&code_challenge_method=S256&code_challenge={pkce.codeChallenge}&state={pkce.state}&scope={SCOPE}&redirect_uri={Uri.EscapeDataString(REDIRECT_URI)}";
            var brokenPostSteamLoginUrl = $"https://isthereanydeal.com/oauth/authorize/?client_id={CLIENT_ID}";
            var callbackUrl = String.Empty;
            using (var webView = plugin.PlayniteApi.WebViews.CreateView(600, 720))
            {
                webView.LoadingChanged += (s, e) =>
                {
                    var url = webView.GetCurrentAddress();

                    if (url.Equals(brokenPostSteamLoginUrl))
                    {
                        // Workaround this ITAD error, when returning from a redirect back to ITAD from Steam login:
                        // > App Authorization Error
                        // > The authorization grant type is not supported by the authorization server. (Check that all required parameters have been provided)
                        // It seems ITAD is sending an incomplete redirect URL to Steam(?) causing Steam to redirect back to ITAD with missing parameters.
                        // As a workaround, we just retry the login, which will work now that ITAD cookies are set after Steam login.
                        webView.Navigate(loginUrl);
                    }

                    if (url.Contains("#login_complete"))
                    {
                        callbackUrl = url;
                        webView.Close();
                    }
                };
                webView.DeleteDomainCookies("isthereanydeal.com");
                webView.Navigate(loginUrl);
                webView.OpenDialog();
            }

            if (!string.IsNullOrEmpty(callbackUrl))
            {
                try
                {
                    var uri = new Uri(callbackUrl);
                    // Query is inside the URL fragment because we used a # redirect URI
                    var query = uri.Fragment.Substring(uri.Fragment.IndexOf("?") + 1);
                    var queryParams = HttpUtility.ParseQueryString(query);
                    await Authenticate(queryParams.Get("state"), queryParams.Get("code"), pkce);
                }
                catch (Exception ex)
                {
                    throw new ITADException("Failed to authenticate", ex);
                }
            }
        }

        private async Task Authenticate(string state, string code, Pkce pkce)
        {
            using (var client = new HttpClient())
            {
                if (state != pkce.state)
                {
                    throw new ITADException("Failed to authenticate (state mismatch)");
                }

                var parameters = new Dictionary<string, string>
                {
                    { "grant_type", "authorization_code" },
                    { "client_id", CLIENT_ID },
                    { "code", code },
                    { "redirect_uri", REDIRECT_URI },
                    { "code_verifier", pkce.codeVerifier },
                };
                var content = new FormUrlEncodedContent(parameters);
                HttpResponseMessage tokenResponse = await client.PostAsync("https://isthereanydeal.com/oauth/token/", content);
                tokenResponse.EnsureSuccessStatusCode();
                var tokenResponseContent = await tokenResponse.Content.ReadAsStringAsync();
                token = Serialization.FromJson<OauthToken>(tokenResponseContent);
                if (token.refresh_token != null)
                {
                    // TODO: maybe encrypt like the official extensions do
                    using (FileStream fileStream = new FileStream(tokensPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                    {
                        byte[] data = Encoding.UTF8.GetBytes(tokenResponseContent);
                        await fileStream.WriteAsync(data, 0, data.Length);
                    }
                }
            }
        }

        private async Task RefreshTokens(HttpClient client)
        {
            try
            {
                var parameters = new Dictionary<string, string>
                    {
                        { "grant_type", "refresh_token" },
                        { "client_id", CLIENT_ID },
                        { "refresh_token", token.refresh_token },
                    };
                var content = new FormUrlEncodedContent(parameters);
                HttpResponseMessage tokenResponse = await client.PostAsync("https://isthereanydeal.com/oauth/token/", content);
                tokenResponse.EnsureSuccessStatusCode();
                var tokenResponseContent = await tokenResponse.Content.ReadAsStringAsync();
                token = Serialization.FromJson<OauthToken>(tokenResponseContent);
                if (token.refresh_token == null)
                {
                    throw new ITADException("Received invalid token");
                }
                // TODO: maybe encrypt like the official extensions do
                using (FileStream fileStream = new FileStream(tokensPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 4096, useAsync: true))
                {
                    byte[] data = Encoding.UTF8.GetBytes(tokenResponseContent);
                    await fileStream.WriteAsync(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                throw new ITADException("Login expired, please re-login", ex);
            }
        }

        private async Task<ProfilesLinkResponse> LinkProfile(HttpClient client)
        {
            try
            {
                // NOTE: Using a constant accountId, even though the docs suggest it should be unique per user, this _seems_ to work ok
                // TODO: However, the extension probably should let people optionally configure the accountId in case they have multiple Playnite installs on different computers syncing to ITAD?
                // TODO probably move this to ProfilesSyncCollection as a class
                var response = await Send<ProfilesLinkResponse>(
                    client,
                    HttpMethod.Put,
                    new StringContent("{\"accountId\": \"playnite\", \"accountName\": \"Playnite\" }"),
                    "https://api.isthereanydeal.com/profiles/link/v1");

                if (string.IsNullOrEmpty(response?.token))
                {
                    throw new ITADException("Received invalid profile token");
                }
                return response;
            }
            catch (Exception ex)
            {
                throw new ITADException("Failed to link profile", ex);
            }
        }

        internal async Task<ProfilesSyncCollectionResponse> ProfilesSyncCollection(ICollection<Game> games)
        {
            var gamesRequest = new List<ProfilesSyncCollectionGame>();
            foreach (var game in games)
            {
                ulong? playtime = null;
                if (game.Playtime > 0)
                {
                    playtime = game.Playtime / 60;
                }

                // As a fallback, use the Playnite database ID
                string id = $"playnite/{game.Id}";
                // Try to include the PluginId and GameId (e.g. steam appid) to create unique IDs per duplicate copy
                // We could use the Source plugin GUID, but this is actualy a user-visible string in ITAD so better to use the more friendly Source.Name
                if (!string.IsNullOrEmpty(game.Source?.Name) && !string.IsNullOrEmpty(game.GameId))
                {
                    id = $"{game.Source?.Name}/{game.GameId}";
                }

                gamesRequest.Add(new ProfilesSyncCollectionGame
                {
                    shop = ItadShopExtension.FromGameSource(game.Source), // SyncGames() ensures this will be valid
                    id = id,
                    title = game.Name,
                    playtime = playtime,
                    lastPlayed = game.LastActivity?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                });

                // Notes:
                // * ITAD will mostly try on its own to merge duplicates, by title I think. (TODO: perhaps we can provide the steam appid/etc in way that ITAD can use it though)
                // * ITAD's title lookup can sometimes do weird things: "New Game" becomes "Plumber Survivors" (yes, really)
            }

            using (var client = new HttpClient())
            {
                token = await getToken();
                var profileToken = await LinkProfile(client);

                try
                {
                    client.DefaultRequestHeaders.Add("ITAD-Profile", profileToken.token);
                    var bodyContent = Serialization.ToJson(gamesRequest);
                    var body = new StringContent(bodyContent, Encoding.UTF8, "application/json");
                    var response = await Send< ProfilesSyncCollectionResponse>(
                        client,
                        HttpMethod.Put,
                        body,
                        "https://api.isthereanydeal.com/profiles/sync/collection/v1");

                    return response;
                }
                catch (Exception ex)
                {
                    throw new ITADException("Failed to sync collection", ex);
                }
            }
        }

        private async Task<OauthToken> getToken()
        {
            try
            {
                var tokenContent = string.Empty;
                using (var reader = new StreamReader(tokensPath, Encoding.UTF8))
                {
                    tokenContent = await reader.ReadToEndAsync();
                }
                var oauthToken = Serialization.FromJson<OauthToken>(tokenContent);
                if (oauthToken?.refresh_token == null)
                {
                    throw new ITADException("Invalid token file");
                }
                return oauthToken;
            } catch (Exception ex)
            {
                throw new ITADException("User not logged in", ex);
            }
        }
        public async Task<bool> GetIsUserLoggedIn()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    token = await getToken();
                    await RefreshTokens(client);
                    return token != null;
                }
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Error checking GetIsUserLoggedIn");
                return false;
            }
        }

        /// <summary>
        /// Send a request with bearer authentication. It will
        /// refresh the tokens and send again if the initial
        /// response is "Unauthorized" (401). You do not need
        /// to set Authorization header before calling.
        /// </summary>
        /// <typeparam name="Response">The datatype of this request's response that will be deserialized into</typeparam>
        /// <param name="client">Required: Client to send request</param>
        /// <param name="method">Required: Request method</param>
        /// <param name="content">Nullable: Request body</param>
        /// <param name="uri">Required: Request URI</param>
        /// <returns>Response from <paramref name="uri"/>.</returns>
        private async Task<Response> Send<Response>(
            HttpClient client, // required
            HttpMethod method, // required
            HttpContent content, // nullable
            string uri) // required
        where Response: class
        {
            var msg = new HttpRequestMessage(method, uri)
            {
                Content = content
            };

            msg.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.access_token);
            var response = await client.SendAsync(msg);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await RefreshTokens(client);
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.access_token);
                msg = new HttpRequestMessage(method, uri)
                {
                    Content = content
                };
                response = await client.SendAsync(msg);
            }

            response.EnsureSuccessStatusCode();
            return Serialization.FromJsonStream<Response>(await response.Content.ReadAsStreamAsync());
        }
    }

    internal class Pkce
    {
        internal static RandomNumberGenerator Rng { get; } = RandomNumberGenerator.Create();
        internal readonly string codeVerifier;
        internal readonly string codeChallenge;
        internal readonly string state;

        public Pkce()
        {
            state = GetUrlSafeString(32);
            codeVerifier = GetUrlSafeString(32);
            codeChallenge = GenerateCodeChallenge(codeVerifier);
        }

        private static string GenerateCodeChallenge(string codeVerifier)
        {
            var hash = SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(codeVerifier));
            var code = new StringBuilder(Convert.ToBase64String(hash))
                .Replace("=", "")
                .Replace("+", "-")
                .Replace("/", "_");

            return code.ToString();
        }
        internal static string GetUrlSafeString(int bytes)
        {
            byte[] payload = new byte[bytes];
            Rng.GetBytes(payload);

            return new StringBuilder(Convert.ToBase64String(payload))
                .Replace("=", "")
                .Replace("+", "-")
                .Replace("/", "_")
                .ToString();
        }
    }
}
