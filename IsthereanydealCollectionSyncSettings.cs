using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Data;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealCollectionSyncSettings : ObservableObject
    {
        private bool importModeReplace = false; // true replace, false ignore // Old
        private bool removeFromWaitlist = false;
        private bool redeemCollection = false;

        public bool ImportModeReplace 
        { 
            get => importModeReplace;
            set => SetValue(ref importModeReplace, value); 
        } // Old

        public ItadApiCredential Credential { get; set; }

        private string[] tags;
        public string[] Tags
        {
            get => tags;
            set => SetValue(ref tags, value);
        }

        private string note;

        public string Note {
            get => note;
            set => SetValue(ref note, value);
        }

        public bool RemoveFromWaitlist 
        { 
            get => removeFromWaitlist; 
            set => SetValue(ref removeFromWaitlist, value); 
        }

        public bool RedeemCollection 
        { 
            get => redeemCollection;
            set => SetValue(ref redeemCollection, value); 
        }

        public int SelectedCategoryId { get; set; } = 0;
    }

    public class IsthereanydealCollectionSyncSettingsViewModel : ObservableObject, ISettings
    {
        private readonly IsthereanydealCollectionSync plugin;
        private IsthereanydealCollectionSyncSettings editingClone { get; set; }

        public ObservableCollection<ItadApiCategory> Categories
        {
            get; 
            private set;
        } = new ObservableCollection<ItadApiCategory>();

        private static object _lock = new object();

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

            BindingOperations.EnableCollectionSynchronization(Categories, _lock);
        }

        public string PluginPath => plugin.GetPluginUserDataPath();

        public bool IsUserLoggedIn => plugin.client.IsUserLoggedIn();

        public RelayCommand<object> LoginCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                plugin.client.Login();
            });
        }

        public void OnModelChanged(object sender, EventArgs args) {
            Categories.Clear();

            foreach (var cat in plugin.client.Categories)
            {
                Categories.Add(cat);
            }
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