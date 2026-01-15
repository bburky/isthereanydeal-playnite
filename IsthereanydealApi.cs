using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using static IsthereanydealCollectionSync.IsthereanydealClient;
using static IsthereanydealCollectionSync.ItadApi;
using static IsthereanydealCollectionSync.ItadOauthConstants;

namespace IsthereanydealCollectionSync
{
    class OauthCodeExchange
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private string State { get; }
        private readonly Pkce pkce;
        private string code;
        internal string LoginUrl { get; }

        internal OauthCodeExchange()
        {
            pkce = new Pkce();
            State = RandomString.GetUrlSafeString(32);

            LoginUrl = $"{HOST_URL}oauth/authorize/?client_id={CLIENT_ID}&redirect_uri={Uri.EscapeDataString(REDIRECT_URI)}&response_type=code&code_challenge_method=S256&code_challenge={pkce.CodeChallenge}&state={State}&scope=user_info coll_write coll_read";
        }

        /// <summary>
        /// Get authorization code from redirect URL
        /// </summary>
        internal bool TryInitCode(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }

            var parts = url.Split('?');

            if (parts is null || parts.Length != 2 || parts[0] != "https://isthereanydeal.com/")
            {
                return false;
            }

            var queryParams = HttpUtility.ParseQueryString(parts[1]);
            var state = queryParams.Get("state");

            if (State != state)
            {
                logger.Error("Redirect URL state mismatched");

                return false;
            }

            code = queryParams.Get("code");

            return true;
        }

        async internal Task<ItadApi> GetTokens()
        {
            logger.Debug("Getting OAuth tokens");

            if (code is null)
            {
                throw new ITADException("OAuth code is null. Is the user authenticated?");
            }

            var parameters = new Dictionary<string, string>
                {
                    { "grant_type", "authorization_code" },
                    { "client_id", CLIENT_ID },
                    { "code", code },
                    { "redirect_uri", REDIRECT_URI },
                    { "code_verifier", pkce.CodeVerifier }
                };

            var content = new FormUrlEncodedContent(parameters);

            HttpResponseMessage response = await ItadOauthConstants.Client.PostAsync($"{HOST_URL}oauth/token/", content);

            await ThrowOnBadHttpStatus(response, "Failed to exchange tokens");

            var credential = await TryParse< ItadApiCredential>(response, "Failed to parse OAuth tokens from ITAD response");

            return new ItadApi(credential);
        }
    }

    public class ItadApiCredential
    {
        public string access_token;
        public string refresh_token;

        // "Bearer"
        [DontSerialize]
        public string token_type;

        [DontSerialize]
        public int expires_in;
    }

    public class ItadApiUserInfo
    {
        public string username;
    }

    public class ItadApiCategory : ObservableObject
    {
#pragma warning disable IDE1006 // Naming Styles
        public int id { get; set; }
        public string title { get; set; }
        public bool @public { get; set; }
#pragma warning restore IDE1006 // Naming Styles
    }

    // https://docs.isthereanydeal.com/#tag/Collection-Copies/operation/collection-copies-v1-get
    public class ItadApiCopy
    {
        public int id;

        public class Game
        {
            public int id;
        }
        public Game game;

        public class Shop
        {
            public int id;
            public string name;
        }
        public Shop shop;

        public bool redeemed;

        public class Price
        {
            public int amount;
            public int amountInt;
            public string currency;
        }
        public Price price;

        public string note;
        public class Tag
        {
            public int id;
            public string tag;
        }
        public Tag[] tag;
        public string added;
    }

    public class ItadApiCopyInput
    {
        public bool redeemed; // required by ITAD
        public string gameId; // required by ITAD
        public ItadShop shop;
        private object price = null;
        public string note = null;
        public string[] tags = null;

        public ItadApiCopyInput(string ItadGameId, bool redeemed, ItadShop shop = ItadShop.Unknown)
        {
            this.gameId = ItadGameId;
            this.shop = shop;
        }
    }

    // The number is shopId which was gotten from https://api.isthereanydeal.com/service/shops/v1
    public enum ItadShop
    {
        Blizzard = 4,
        Ea = 52,
        Epic = 16,
        Gog = 35,
        HumbleBundle = 18,
        Indiegala = 42,
        Steam = 61,
        Ubisoft = 62,
        Xbox = 48, // Including Microsoft Store.

        // For manually added games or games from unsupported stores.
        Unknown = -1
    }

    public class ItadShopExtension
    {
        public static ItadShop FromGameSource(GameSource source)
        {
            switch (source.Name)
            {
                //case "Battle.net":
                //    return ItadShop.Blizzard;
                case "EA app":
                    return ItadShop.Ea;
                case "Epic":
                    return ItadShop.Epic;
                case "GOG":
                    return ItadShop.Gog;
                case "Humble Bundle":
                    return ItadShop.HumbleBundle;
                //case "Indiegala":
                //    return ItadShop.Indiegala;
                case "Steam":
                    return ItadShop.Steam;
                case "Ubisoft Connect":
                    return ItadShop.Ubisoft;
                case "Xbox":
                    return ItadShop.Xbox;
                default:
                    return ItadShop.Unknown;
            }
        }
    }

    public class ItadApi
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private ItadApiCredential credential;
        internal ItadApiCredential Credential => credential;

        internal ItadApi(ItadApiCredential credential)
        {
            this.credential = credential;
        }

        async internal Task RefreshTokens()
        {
            var parameters = new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "client_id", CLIENT_ID },
                    { "client_secret", CLIENT_SECRET },
                    { "refresh_token", credential.refresh_token },
                };

            var content = new FormUrlEncodedContent(parameters);

            HttpResponseMessage response = await ItadOauthConstants.Client.PostAsync($"{HOST_URL}oauth/token/", content);

            await ThrowOnBadHttpStatus(response, "Failed to refresh tokens");

            credential = await TryParse<ItadApiCredential>(response, "Failed to parse OAuth tokens from ITAD response");
        }

        internal async Task<string> GetUsername()
        {
            var response = await GetAsync($"{API_URL}user/info/v2");
            await ThrowOnBadHttpStatus(response, "Failed to get user info");
            var userInfo = await TryParse<ItadApiUserInfo>(response, "Failed to parse user info");

            return userInfo.username;
        }

        internal async Task<ItadApiCategory[]> GetCategories()
        {
            var response = await GetAsync($"{API_URL}collection/groups/v1");
            await ThrowOnBadHttpStatus(response, "Failed to get categories");
            var categories = await TryParse<ItadApiCategory[]>(response, "Failed to parse categories");

            return categories;
        }

        internal async Task<ItadApiCategory> CreateCategory(string title, bool isPublic = false)
        {
            var response = await PostAsync($"{API_URL}collection/groups/v1");
            await ThrowOnBadHttpStatus(response, $"Failed to create new {(isPublic ? "public" : "private")} category {title}");
            var category = await TryParse<ItadApiCategory>(response, "Failed to parse category");

            return category;
        }

        // TODO
        /// <summary>
        /// Look up ITAD game IDs by their names
        /// </summary>
        /// <param name="games">An array of game names</param>
        /// <returns>A dictionary of game IDs on the shop to their respective game IDs on ITAD</returns>
        internal async Task<Dictionary<string, string>> LookUpGameId(string[] games)
        {
            Dictionary<string, string> res = new Dictionary<string, string>();

            var response = await Client.PostAsync($"{API_URL}lookup/id/title/v1", JsonContentOf(games));
            await ThrowOnBadHttpStatus(response, $"Failed to look up game IDs");
            res = Serialization.FromJsonStream<Dictionary<string, string>>(await response.Content.ReadAsStreamAsync());

            return res;
        }

        internal async Task AddCopies(ItadApiCopyInput[] games)
        {
            var response = await PostJsonAsync($"{API_URL}collection/copies/v1", games);
            await ThrowOnBadHttpStatus(response, $"Failed to add copies");
            var category = await TryParse<ItadApiCategory>(response, "Failed to parse add copies");
        }

        internal async Task<ItadApiCopy[]> GetCopies()
        {
            var response = await GetAsync($"{API_URL}collection/copies/v1");
            await ThrowOnBadHttpStatus(response, $"Failed to get copies");
            var copies = await TryParse<ItadApiCopy[]>(response, "Failed to parse copies");

            return copies;
        }

        internal async Task DeleteFromWaitList(string[] gameIds)
        {
            var response = await DeleteJsonAsync($"{API_URL}waitlist/games/v1", gameIds);
            await ThrowOnBadHttpStatus(response, $"Failed to delete from waitlist");
        }

        internal async Task GetCollection()
        {
            var response = await GetAsync($"{API_URL}collection/games/v1");
            await ThrowOnBadHttpStatus(response, $"Failed to get collection");
            var category = await TryParse<ItadApiCategory>(response, "Failed to parse category");
        }

        private async Task<HttpResponseMessage> GetAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

            return await AuthorizeAndSend(request);
        }

        private async Task<HttpResponseMessage> PostAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            
            return await AuthorizeAndSend(request);
        }

        private async Task<HttpResponseMessage> PostJsonAsync<T>(string url, T payload)
        where T: class
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContentOf(payload)
            };

            return await AuthorizeAndSend(request);
        }

        private async Task<HttpResponseMessage> DeleteJsonAsync<T>(string url, T payload)
        where T: class
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, url)
            {
                Content = JsonContentOf(payload)
            };

            return await AuthorizeAndSend(request);
        }

        private async Task<HttpResponseMessage> AuthorizeAndSend(HttpRequestMessage request)
        {
            request.Headers.Add("Authorization", $"Bearer {credential.access_token}");
            var response = await Client.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await RefreshTokens();
                request.Headers.Remove("Authorization");
                request.Headers.Add("Authorization", $"Bearer {credential.access_token}");
                response = await Client.SendAsync(request);
            }

            return response;
        }

        internal async static Task<T> TryParse<T>(HttpResponseMessage response, string msg) 
            where T: class
        {
            string content = await response.Content.ReadAsStringAsync();

            if (!Serialization.TryFromJson(content, out T res))
            {
                throw new ITADException($"{msg}: {content}");
            }

            return res;
        }

        internal async static Task ThrowOnBadHttpStatus(HttpResponseMessage response, string msg)
        {
            if (!response.IsSuccessStatusCode)
            {
                string errorContent = await response.Content.ReadAsStringAsync();
                throw new ITADException($"{msg} [{response.StatusCode:d} {response.StatusCode}]: {errorContent}");
            }
        }
        private static StringContent JsonContentOf<T>(T data)
            where T : class
        {
            return new StringContent(Serialization.ToJson(data), Encoding.UTF8, "application/json");
        }
    }

    static class ItadOauthConstants
    {
        internal const string HOST_URL = "https://isthereanydeal.com/";
        internal const string API_URL = "https://api.isthereanydeal.com/";

        // Put your own ITAD app.
        internal const string CLIENT_ID = "3f4d9e8636de0604";
        internal const string CLIENT_SECRET = "53d0e6d7a082e5c7232242bc90d540adc5496af9";
        internal const string REDIRECT_URI = "https://isthereanydeal.com/";
        internal const string API_KEY = "97cf351a85f16263a977b5fb78876df2cfece7b0";

        // Use one HttpClient accross every class.
        internal static readonly HttpClient Client = new HttpClient();
    }

    class Pkce
    {
        public readonly string CodeVerifier;
        public readonly string CodeChallenge;

        public Pkce()
        {
            CodeVerifier = RandomString.GetUrlSafeString(32);
            CodeChallenge = GenerateCodeChallenge(CodeVerifier);
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
    }
    class RandomString
    {
        public static RandomNumberGenerator Rng { get; } = RandomNumberGenerator.Create();

        public static string GetUrlSafeString(int bytes)
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