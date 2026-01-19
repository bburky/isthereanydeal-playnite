using Playnite.SDK.Data;
using Playnite.SDK.Plugins;
using System;
using System.IO;
using System.Text;

namespace IsthereanydealCollectionSync
{
	public class Database
	{

        public const string CategoryName = "ITAD Sync Failed";
        public Guid CategoryId { get; set; }
    }

    public class DatabaseProxy
    {
        private const string FILENAME = "IsThereAnyDealCollectionSyncDatabase.json";
        private readonly string filePath;
        public Database Database { get; private set; }

        private DatabaseProxy(string filePath)
        {
            this.filePath = filePath;
        }

        public static DatabaseProxy LoadOrInit(Plugin plugin)
        {
            string filePath = Path.Combine(plugin.GetPluginUserDataPath(), FILENAME);
            Serialization.TryFromJsonFile(filePath, out Database database);

            return new DatabaseProxy(filePath)
            {
                Database = database
            };
        }

        public void Save()
        {
            File.WriteAllText(filePath, Serialization.ToJson(Database), Encoding.UTF8);
        }
    }
}
