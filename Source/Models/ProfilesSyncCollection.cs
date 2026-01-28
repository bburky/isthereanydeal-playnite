using Playnite.SDK.Models;

namespace IsthereanydealCollectionSync.Models
{
    public class ProfilesSyncCollectionGame
    {
        public ItadShop shop;
        public string id;
        public string title;
        public ulong? playtime;
        public string lastPlayed;
    }

    public class ProfilesSyncCollectionResponse
    {
        public int total;
        public int added;
        public int removed;
    }

    // The number is shopId from https://api.isthereanydeal.com/service/shops/v1
    // ITAD has some old shops that are no longer documented but still work, these are included too
    // 0 is used for games that cannot be mapped to an ITAD shop, but this value will cause an error if used in the ITAD API
    public enum ItadShop
    {
        Unknown = 0, // Not supported by ITAD, this value will cause an error if used in API
        Amazon = 3, // Missing from API docs, discovered from old IATD collection JSON backup
        Blizzard = 4,
        Ea = 52,
        Discord = 12, // Missing from API docs, Discord Store is defunct now, so not including it.
        Epic = 16,
        Gog = 35,
        HumbleBundle = 37,
        Indiegala = 42,
        Itch = 44, // Missing from API docs, discovered from old IATD collection JSON backup
        MicrosoftStore = 48,
        Steam = 61,
        Ubisoft = 62,
    }

    public class ItadShopExtension
    {
        /// <summary>
        /// Map GameSource to ItadShop.
        /// </summary>
        /// <param name="source"></param>
        /// <returns>ItadShop or 0 if source cannot map to shops on ITAD</returns>
        public static ItadShop FromGameSource(GameSource source)
        {
            switch (source?.Name)
            {
                // This list is intended to include all known Source values used by Playnite addons.
                // Currently includes all known current and pervious values from the official PlayniteExtensions repository.
                // Adding additional Sources from unofficial addons is welcome.

                case "Amazon":
                    return ItadShop.Amazon;
                case "Bethesda":
                    // Bethesda plugin was removed from Playnite, not in ITAD shop API
                    return ItadShop.Unknown;
                case "Discord":
                    return ItadShop.Unknown; // we could return 12 but the store is defunct anyway
                case "itch.io":
                    return ItadShop.Itch;
                case "Legacy Games":
                    // Not supported by ITAD shop API
                    return ItadShop.Unknown;
                case "Battle.net":
                    return ItadShop.Blizzard;
                case "EA app":
                    return ItadShop.Ea;
                case "Epic":
                    return ItadShop.Epic;
                case "GOG":
                    return ItadShop.Gog;
                case "Humble":
                    return ItadShop.HumbleBundle;
                case "Indiegala":
                    return ItadShop.Indiegala;
                case "Origin":
                    // EA app is the new name but old libraries may still have "Origin" as source
                    return ItadShop.Ea;
                case "PlayStation":
                     // PlayStation plugin was removed from Playnite, not in ITAD shop API
                    return ItadShop.Unknown;
                case "Rockstar Games":
                    // Rockstar Games is not supported by ITAD shop API
                    return ItadShop.Unknown;
                case "Steam":
                    return ItadShop.Steam;
                case "Twitch":
                    // Amazon is the new name but old libraries may still have "Twitch" as source
                    return ItadShop.Amazon;
                case "Ubisoft Connect":
                    return ItadShop.Ubisoft;
                case "Uplay":
                    // Ubisoft Connect is the new name but old libraries may still have "Uplay" as source
                    return ItadShop.Ubisoft;
                case "Xbox":
                    return ItadShop.MicrosoftStore;
                default:
                    return ItadShop.Unknown;
            }
        }
    }
}
