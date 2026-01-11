using Playnite.SDK;
using Playnite.SDK.Data;
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

    public class ItadUserInfo
    {
        public string username;
    }

    // The field name must match JSON response so disabling naming style warning.
    // We only do it for this class because binding source must be a property. Fields can't be bound.
    public class ItadCategory : ObservableObject
    {
#pragma warning disable IDE1006 // Naming Styles
        public int id { get; set; }
        public string title { get; set; }
        public bool @public { get; set; }
#pragma warning restore IDE1006 // Naming Styles
    }

    public class ItadGame
    {
        public string name;
        public bool redeemed;
        public ItadShop shop;
    }

    // The number is shopId which was gotten from https://api.isthereanydeal.com/service/shops/v1
    public enum ItadShop
    {
        Blizzard = 4,
        Ea = 52,
        Epic = 16,
        Gog = 35,
        Indiegala = 42,
        Steam = 61,
        Ubisoft = 62,

        // For games from manually added unsupported stores.
        Unknown = -1, 

        Xbox = 48, 
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
            var userInfo = await TryParse<ItadUserInfo>(response, "Failed to parse user info");

            return userInfo.username;
        }

        internal async Task<ItadCategory[]> GetCategories()
        {
            var response = await GetAsync($"{API_URL}collection/groups/v1");
            await ThrowOnBadHttpStatus(response, "Failed to get categories");
            var categories = await TryParse<ItadCategory[]>(response, "Failed to parse categories");

            return categories;
        }

        internal async Task<ItadCategory> CreateCategory(string title, bool isPublic = false)
        {
            var response = await PostAsync($"{API_URL}collection/groups/v1");
            await ThrowOnBadHttpStatus(response, $"Failed to create new {(isPublic ? "public" : "private")} category {title}");
            var category = await TryParse<ItadCategory>(response, "Failed to parse category");

            return category;
        }

        // TODO
        internal async Task AddCopies(IEnumerable<ItadGame> games)
        {
            var response = await PostAsync($"{API_URL}collection/copies/v1");
            await ThrowOnBadHttpStatus(response, $"Failed to import new game");
            var category = await TryParse<ItadCategory>(response, "Failed to parse category");
        }

        private async Task<HttpResponseMessage> GetAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {credential.access_token}");

            return await RetryOnUnauthorized(request);

        }

        private async Task<HttpResponseMessage> PostAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {credential.access_token}");
            
            return await RetryOnUnauthorized(request);
        }

        private async Task<HttpResponseMessage> RetryOnUnauthorized(HttpRequestMessage request)
        {
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
    }

    static class ItadOauthConstants
    {
        internal const string HOST_URL = "https://isthereanydeal.com/";
        internal const string API_URL = "https://api.isthereanydeal.com/";

        // Put your own ITAD app.
        internal const string CLIENT_ID = "3f4d9e8636de0604";
        internal const string CLIENT_SECRET = "53d0e6d7a082e5c7232242bc90d540adc5496af9";
        internal const string REDIRECT_URI = "https://isthereanydeal.com/";

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