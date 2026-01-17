using System;
using System.IO;
using Playnite.SDK.Data;
using Playnite.SDK.Plugins;

namespace IsthereanydealCollectionSync
{
	public class Database
	{
        private const string FILENAME = "IsThereAnyDealCollectionSyncDatabase.json";
        private string filePath;

		public string CategoryName = "_IsThereAnyDealCollectionSync_FailedGame";
        public Guid CategoryId;

        private Database(Plugin plugin, string filePath)
        {
            this.filePath = filePath;
        }

        public static Database LoadOrInit(Plugin plugin)
        {
            string filePath = Path.Combine(plugin.GetPluginUserDataPath(), FILENAME);

            if (Serialization.TryFromJsonFile(filePath, out Database db))
            {
                return db;
            }
            else
            {
                return new Database(plugin, filePath);
            }
        }

        public void Save()
        {
            File.WriteAllText(filePath, Serialization.ToJson(this));
        }
    }
}
