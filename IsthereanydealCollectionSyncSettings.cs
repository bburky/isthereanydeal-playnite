using Playnite.SDK;
using Playnite.SDK.Data;
using System.Collections.Generic;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealCollectionSyncSettings : ObservableObject
    {
        private ImportMode importMode = ImportMode.Skip;
        private bool removeFromWaitlist = true;
        private string[] tags;
        private string note;
        private bool skipSteam = true;
        private bool skipGog = true;
        private bool skipNoSource = false;
        private bool syncDuplicateHider = true;
        private bool redeemEpic = false;
        private bool syncHidden = false;
        private bool filterFaileds = true;

        public ImportMode ImportMode 
        { 
            get => importMode;
            set => SetValue(ref importMode, value); 
        }

        public ItadApiCredential Credential { get; set; }

        public string[] Tags
        {
            get => tags;
            set => SetValue(ref tags, value);
        }

        public string Note {
            get => note;
            set => SetValue(ref note, value);
        }

        public bool RemoveFromWaitlist 
        { 
            get => removeFromWaitlist; 
            set => SetValue(ref removeFromWaitlist, value); 
        }

        public bool SkipSteam
        {
            get => skipSteam;
            set => SetValue(ref skipSteam, value);
        }

        public bool SkipGog
        {
            get => skipGog;
            set => SetValue(ref skipGog, value);
        }

        public bool SkipNoSource
        {
            get => skipNoSource;
            set => SetValue(ref skipNoSource, value);
        }

        public bool SyncDuplicateHider
        {
            get => syncDuplicateHider;
            set => SetValue(ref syncDuplicateHider, value);
        }

        public bool RedeemEpic
        {
            get => redeemEpic;
            set => SetValue(ref redeemEpic, value);
        }

        public bool SyncHidden
        {
            get => syncHidden;
            set => SetValue(ref syncHidden, value);
        }

        public bool FilterFaileds
        {
            get => filterFaileds;
            set => SetValue(ref filterFaileds, value);
        }
    }

    public enum ImportMode
    {
        Skip,
        Replace,
    }

    public class IsthereanydealCollectionSyncSettingsViewModel : ObservableObject, ISettings
    {
        private readonly IsthereanydealCollectionSync plugin;
        private IsthereanydealCollectionSyncSettings editingClone;
        private IsthereanydealCollectionSyncSettings settings;

        public IsthereanydealCollectionSyncSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                OnPropertyChanged();
            }
        }

        public IsthereanydealCollectionSyncSettingsViewModel(IsthereanydealCollectionSync plugin)
        {
            this.plugin = plugin;
            var savedSettings = plugin.LoadPluginSettings<IsthereanydealCollectionSyncSettings>();
            if (savedSettings != null)
            {
                Settings = savedSettings;
            }
            else
            {
                Settings = new IsthereanydealCollectionSyncSettings();
            }
        }

        public bool IsUserLoggedIn => plugin.client.IsUserLoggedIn();

        public RelayCommand<object> LoginCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                plugin.client.Login();
            });
        }

        public void BeginEdit()
        {
            editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = editingClone;
        }

        public void EndEdit()
        {
            Settings.Credential = plugin.client.Api.Credential;
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}