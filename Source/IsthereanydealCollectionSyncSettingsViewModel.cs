using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime;

namespace IsthereanydealCollectionSync
{
    public class Settings : ObservableObject
    {
        public bool SkipNoSource { get; set; } = false;
        public bool SyncHidden { get; set; } = false;
        public bool AutoRunOnLibraryUpdate { get; set; } = true;
    }

    public class IsthereanydealCollectionSyncSettingsViewModel : ObservableObject, ISettings
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IsthereanydealCollectionSync plugin;
        public Settings editingClone { get; set; }

        private Settings settings;
        public Settings Settings
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
            var savedSettings = LoadSavedSettings();
            if (savedSettings != null)
            {
                Settings = savedSettings;
            }
            else
            {
                Settings = new Settings();
            }
        }

        public bool IsUserLoggedIn
        {
            get
            {
                return plugin.client.GetIsUserLoggedIn().GetAwaiter().GetResult();
            }
        }

        public RelayCommand<object> LoginCommand
        {
            get => new RelayCommand<object>(async (a) =>
            {
                plugin.ClearNotifications();
                await plugin.client.Login();
                OnPropertyChanged(nameof(IsUserLoggedIn));
            });
        }
        public static RelayCommand<object> NavigateUrlCommand
        {
            get => new RelayCommand<object>((obj) =>
            {
                try
                {
                    if (obj is Uri uri)
                    {
                        Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                    }
                    else
                    {
                        logger.Error("Failed to open url.");
                    }
                }
                catch (Exception e) when (!Debugger.IsAttached)
                {
                    logger.Error(e, "Failed to open url.");
                }
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

        public Settings LoadSavedSettings()
        {
            return plugin.LoadPluginSettings<Settings>();
        }

        public virtual bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}