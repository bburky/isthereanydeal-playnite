using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Markup;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealCollectionSyncSettings : ObservableObject
    {
        private bool importModeReplace = false; // true replace, false ignore
        private bool removeFromWaitlist = false;
        private string importGroup = "Playnite";
        public bool ImportModeReplace { get => importModeReplace; set => SetValue(ref importModeReplace, value); }
        public bool RemoveFromWaitlist { get => removeFromWaitlist; set => SetValue(ref removeFromWaitlist, value); }
        public string ImportGroup { get => importGroup; set => SetValue(ref importGroup, value); }
    }

    public class IsthereanydealCollectionSyncSettingsViewModel : ObservableObject, ISettings
    {
        private readonly IsthereanydealCollectionSync plugin;
        private IsthereanydealCollectionSyncSettings editingClone { get; set; }

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

        public bool IsUserLoggedIn
        {
            get
            {
                using (var view = plugin.PlayniteApi.WebViews.CreateOffscreenView())
                {
                    var client = new IsthereanydealClient(plugin, view);
                    return client.GetIsUserLoggedIn().GetAwaiter().GetResult();
                }
            }
        }
        public RelayCommand<object> LoginCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                using (var view = plugin.PlayniteApi.WebViews.CreateView(600, 800))
                {
                    var client = new IsthereanydealClient(plugin, view);
                    client.Login();
                }

                OnPropertyChanged(nameof(IsUserLoggedIn));
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
            plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}