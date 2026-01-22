using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;

namespace IsthereanydealCollectionSync
{
    internal static class Common
    {
        internal static ILogger logger = LogManager.GetLogger();

        // You need to check IPlayniteAPI.Database.Categories
        // has the category before calling it.
        internal static void AddCategory(IPlayniteAPI api, Game game, Category category)
        {
            if (game.CategoryIds is null)
            {
                game.CategoryIds = new List<Guid> { category.Id };
            }
            else
            {
                game.CategoryIds.AddMissing(category.Id);
            }

            api.Database.Games.Update(game);
        }

        internal static void RemoveCategoryFromDatabase(IPlayniteAPI api, Category category)
        {
            if (category is null)
            {
                return;
            }

            RemoveCategoryFromDatabase(api, category.Id);
        }

        internal static void RemoveCategoryFromDatabase(IPlayniteAPI api, Guid id)
        {
            logger.Info("Remove category from playnite");

            // Add check since both Remove(Category) and Remove(Guid) throw if category does not exist
            if (api.Database.Categories.Get(id) is null)
            {
                return;
            }

            try
            {
                api.Database.Categories.Remove(id);
            }
            catch
            {

            }
        }

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
