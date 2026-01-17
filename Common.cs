using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;

namespace IsthereanydealCollectionSync
{
    internal static class Common
    {
        internal static void AddCategory(IPlayniteAPI api, Game game, Category cate)
        {
            if (!api.Database.Categories.Contains(cate))
            {
                api.Database.Categories.Add(cate);
            }

            if (game.CategoryIds is null)
            {
                game.CategoryIds = new List<Guid> { cate.Id };
            }
            else
            {
                game.CategoryIds.AddMissing(cate.Id);
            }
        }

        internal static void RemoveCategoryFromDatabase(IPlayniteAPI api, Category cate)
        {
            if (cate is null)
            {
                return;
            }

            api.Database.Categories.Remove(cate);
        }
    }
}
