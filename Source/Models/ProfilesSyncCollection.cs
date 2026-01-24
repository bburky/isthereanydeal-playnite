using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

    // The number is shopId which was gotten from https://api.isthereanydeal.com/service/shops/v1
    // It should be 0 for library that cannot be mapped
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
        MicrosoftStore = 48,
        Unknown = 0, // Not supported by ITAD, will error if used in API
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
                case "Steam":
                    return ItadShop.Steam;
                case "Ubisoft Connect":
                    return ItadShop.Ubisoft;
                case "Xbox":
                    return ItadShop.MicrosoftStore;
                default:
                    return ItadShop.Unknown;
            }
        }
    }
}
