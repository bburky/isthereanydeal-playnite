using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;

namespace IsthereanydealCollectionSync
{
    internal static class Common
    {
        internal static ILogger logger = LogManager.GetLogger();

        /// <summary>
        /// You MUST NOT use it without <paramref name="args" />. For that purpose use <see cref="ResourceProvider.GetString(string)"/>
        /// </summary>
        /// <param name="key"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        internal static string Localized(string key, params object[] args)
        {
            return string.Format(ResourceProvider.GetString(key), args);
        }
    }
}
