using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace IsthereanydealCollectionSync
{
	internal class Database
	{
        public const string CategoryName = "ITAD Sync Failed";
        public Guid CategoryId { get; set; }
        public ItadApiCredential Credential { get; set; }
    }

    /// <summary>
    /// Manage persistent data that the user
    /// should not touch.
    /// </summary>
    public class DatabaseProxy
    {
        private static ILogger logger = LogManager.GetLogger();
        private const string FILENAME = "IsThereAnyDealCollectionSyncDatabase.json";
        private readonly string filePath;
        private Database Database { get; set; }
        private Category category;
        public Category Category {
            // Maybe generalize to one method if 
            // more items are needed in the future.
            get
            {
                if (category is null)
                {
                    category = new Category
                    {
                        Name = Database.CategoryName
                    };

                    Database.CategoryId = category.Id;
                    _ = Save();
                }

                return category;
            }
        }

        private DatabaseProxy(string filePath)
        {
            this.filePath = filePath;
        }

        public static DatabaseProxy LoadOrInit(Plugin plugin)
        {
            string filePath = Path.Combine(plugin.GetPluginUserDataPath(), FILENAME);
            if (!Serialization.TryFromJsonFile(filePath, out Database database))
            {
                logger.Warn("Failed to deserialize database. Creating new one.");
            }

            return new DatabaseProxy(filePath)
            {
                Database = database
            };
        }

        /// <summary>
        /// Update the database to match with Playnite.
        /// For fields that couldn't match, they
        /// remain null.
        /// </summary>
        /// <param name="playniteDb"></param>
        public void Sync(IGameDatabase playniteDb)
        {
            category = playniteDb.Categories.Get(Database.CategoryId);
        }

        public async Task Save()
        {
            logger.Info("Save database");

            try
            {
                using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
                {
                    await writer.WriteAsync(Serialization.ToJson(Database));
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to save database");
            }
        }
    }
}
