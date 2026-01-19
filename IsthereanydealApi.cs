using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using static IsthereanydealCollectionSync.ItadApi;
using static IsthereanydealCollectionSync.ItadOauthConstants;

namespace IsthereanydealCollectionSync
{
    class OauthCodeExchange
    {
        private readonly ILogger logger = LogManager.GetLogger();
        private readonly string state;
        private readonly Pkce pkce;
        private string code;
        internal string LoginUrl { get; }

        internal OauthCodeExchange()
        {
            pkce = new Pkce();
            state = RandomString.GetUrlSafeString(32);

            LoginUrl = $"{HOST_URL}oauth/authorize/?client_id={CLIENT_ID}&redirect_uri={Uri.EscapeDataString(REDIRECT_URI)}&response_type=code&code_challenge_method=S256&code_challenge={pkce.CodeChallenge}&state={state}&scope=user_info coll_write coll_read wait_write wait_read";
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

            if (parts is null || parts.Length != 2 || parts[0] != HOST_URL)
            {
                return false;
            }

            var queryParams = HttpUtility.ParseQueryString(parts[1]);
            var state = queryParams.Get("state");

            if (this.state != state)
            {
                logger.Error("Redirect URL state mismatched");

                return false;
            }

            code = queryParams.Get("code");

            return true;
        }

        async internal Task GetTokens(ItadApi api)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ITADException("OAuth code is null");
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

            HttpResponseMessage response = await Client.PostAsync($"{HOST_URL}oauth/token/", content);

            await ThrowOnBadHttpStatus(response);

            var credential = await TryParse<ItadApiCredential>(response, "Failed to parse OAuth tokens from ITAD response");

            api.Credential = credential;
        }
    }

    /* Classes that prefixes "ItadApi" are those to be serialize
     * and deserialized.
     * 
     * Playnite API did not document lots of the behavoir about
     * serialization. This comment will try to document more about
     * it. Note this only applies to JSON and is subjected to 
     * change in the future.
     * 
     * - To-be-deserialized members must be public or otherwise ignored.
     * - Member name must match exactly. This means naming convention might be broken.
     * - TryFromJson returns true as long as the input is in JSON format. This has the following implication.
     *   - You can't guarantee all members are deserialized
     *     to by the serializer.
     *   - Members that the serializer failed to deserialize 
     *     to will be default-initialized.
    */
    public class ItadApiCredential
    {
        public string access_token;
        public string refresh_token;
    }

    public class ItadApiUserInfo
    {
        public string username;
    }

    // https://docs.isthereanydeal.com/#tag/Collection-Copies/operation/collection-copies-v1-get
    public class ItadApiCopy
    {
        public int id;

        public class Game
        {
            public string id;
        }
        public Game game;

        public class Shop
        {
            public int id;
        }
        public Shop shop;

        /// <summary>
        /// Compare this copy's shop and <paramref name="shop"/>
        /// </summary>
        /// <param name="shop"></param>
        /// <returns>
        /// True if shop matches or if it doesn't have a shop and <paramref name="shop"/> is null. Otherwise false.
        /// </returns>
        public bool MatchShop(ItadShop? shop)
        {
            return this.shop is null && shop is null ||
                !(this.shop is null) && !(shop is null) &&
                this?.shop.id == (int)shop;
        }
    }

    public class ItadWaitlistItem
    {
        public string id;
    }

    public class ItadApiAddCopyInput
    {
        public bool redeemed; // Required by ITAD
        public string gameId; // Required by ITAD
        public ItadShop? shop = null;
        public string note = null;
        public ICollection<string> tags = null;

        public ItadApiAddCopyInput(string ItadGameId, bool redeemed)
        {
            this.gameId = ItadGameId;
            this.redeemed = redeemed;
        }
    }

    public class ItadApiUpdateCopyInput
    {
        public int id; // required by ITAD
        public bool? redeemed = null;
        public ItadShop? shop = null;
        public string note = null;
        public ICollection<string> tags = null;

        public ItadApiUpdateCopyInput(int ItadCopyId)
        {
            id = ItadCopyId;
        }
    }

    // The number is shopId which was gotten from https://api.isthereanydeal.com/service/shops/v1
    // It should be null for library that cannot be mapped
    // to ITAD shop.
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
    }

    public class ItadShopExtension
    {
        /// <summary>
        /// Map GameSource to ItadShop.
        /// </summary>
        /// <param name="source"></param>
        /// <returns>ItadShop or null if source cannot map to shops on ITAD</returns>
        public static ItadShop? FromGameSource(GameSource source)
        {
            switch (source?.Name)
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
                    return null;
            }
        }
    }

    public class ItadApi
    {
        private readonly Settings settings;
        public ItadApiCredential Credential
        {
            get => settings.Credential;
            set => settings.Credential = value;
        }

        internal ItadApi(Settings settings)
        {
            this.settings = settings;
        }

        async internal Task RefreshTokens()
        {
            var parameters = new Dictionary<string, string>
                {
                    { "grant_type", "refresh_token" },
                    { "client_id", CLIENT_ID },
                    { "client_secret", CLIENT_SECRET },
                    { "refresh_token", Credential.refresh_token },
                };

            var content = new FormUrlEncodedContent(parameters);

            HttpResponseMessage response = await Client.PostAsync($"{HOST_URL}oauth/token/", content);

            await ThrowOnBadHttpStatus(response);

            Credential = await TryParse<ItadApiCredential>(response, "Failed to parse OAuth tokens from ITAD response");
        }

        internal async Task<string> GetUsername()
        {
            var response = await GetAsync($"{API_URL}user/info/v2");
            await ThrowOnBadHttpStatus(response);
            var userInfo = await TryParse<ItadApiUserInfo>(response, "Failed to parse user info");

            return userInfo.username;
        }

        /// <summary>
        /// Look up ITAD game IDs by their names
        /// </summary>
        /// <param name="gameNames">An array of game names</param>
        /// <returns>A dictionary of game names and their ITAD game IDs</returns>
        internal async Task<IDictionary<string, string>> LookUpGameId(ICollection<string> gameNames)
        {
            var response = await Client.PostAsync($"{API_URL}lookup/id/title/v1", JsonContentOf(gameNames));
            await ThrowOnBadHttpStatus(response);
            var res = Serialization.FromJsonStream<Dictionary<string, string>>(await response.Content.ReadAsStreamAsync());

            return res;
        }

        internal async Task AddCopies(ICollection<ItadApiAddCopyInput> games)
        {
            var response = await PostJsonAsync($"{API_URL}collection/copies/v1", games);
            await ThrowOnBadHttpStatus(response);
        }

        internal async Task<ICollection<ItadApiCopy>> GetCopies()
        {
            var response = await GetAsync($"{API_URL}collection/copies/v1");
            await ThrowOnBadHttpStatus(response);
            var copies = await TryParse<ItadApiCopy[]>(response, "Failed to parse copies");

            return copies;
        }

        internal async Task UpdateCopies(ICollection<ItadApiUpdateCopyInput> games)
        {
            var response = await PatchJsonAsync($"{API_URL}collection/copies/v1", games);
            await ThrowOnBadHttpStatus(response);
        }

        internal async Task<ICollection<string>> GetWaitlist()
        {
            var response = await GetAsync($"{API_URL}waitlist/games/v1");
            await ThrowOnBadHttpStatus(response);

            var waitlist = await TryParse<ItadWaitlistItem[]>(response, "Failed to parse waitlist");
            var res = waitlist.Select(w => w.id).ToArray();

            return res;
        }

        internal async Task AddToWaitlist(ICollection<string> gameIds)
        {
            var response = await PutJsonAsync($"{API_URL}waitlist/games/v1", gameIds);
            await ThrowOnBadHttpStatus(response);
        }

        private async Task<HttpResponseMessage> GetAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);

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

        private async Task<HttpResponseMessage> PutJsonAsync<T>(string url, T payload)
        where T : class
        {
            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = JsonContentOf(payload)
            };

            return await AuthorizeAndSend(request);
        }

        private async Task<HttpResponseMessage> PatchJsonAsync<T>(string url, T payload)
        where T: class
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = JsonContentOf(payload)
            };

            return await AuthorizeAndSend(request);
        }

        private async Task<HttpResponseMessage> AuthorizeAndSend(HttpRequestMessage request)
        {
            request.Headers.Add("Authorization", $"Bearer {Credential.access_token}");
            var response = await Client.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                await RefreshTokens();
                request.Headers.Remove("Authorization");
                request.Headers.Add("Authorization", $"Bearer {Credential.access_token}");
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

        internal async static Task ThrowOnBadHttpStatus(HttpResponseMessage response)
        {
            try
            {
                response.EnsureSuccessStatusCode();
            }
            catch (Exception e)
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                throw new ITADException($"Request response is not OK [{response.StatusCode:d} {response.StatusCode}] \"{responseContent}\"", e);
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
        internal static readonly HttpClient Client = new HttpClient()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
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