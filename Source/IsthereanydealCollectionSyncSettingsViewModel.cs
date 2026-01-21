using Playnite.SDK;
using Playnite.SDK.Data;
using System.Collections.Generic;

namespace IsthereanydealCollectionSync
{
    public class Settings : ObservableObject
    {
        private ImportMode importMode = ImportMode.Skip;
        private bool removeFromWaitlist = true;
        private string[] tags;
        private string note;
        private bool skipNoSource = false;
        private bool syncDuplicateHider = true;
        private bool redeemEpic = false;
        private bool syncHidden = false;
        private bool filterFaileds = true;
        private bool autoRunOnLibraryUpdate = true;

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

        public bool AutoRunOnLibraryUpdate
        {
            get => autoRunOnLibraryUpdate;
            set => SetValue(ref autoRunOnLibraryUpdate, value);
        }
    }

    public enum ImportMode
    {
        Skip,
        Replace,
    }

    public class IsthereanydealCollectionSyncSettingsViewModel : ObservableObject, ISettings
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private readonly IsthereanydealCollectionSync plugin;
        private Settings editing;

        public Settings Settings
        {
            get => editing;
            set => SetValue(ref editing, value);
        }

        public IsthereanydealCollectionSyncSettingsViewModel(IsthereanydealCollectionSync plugin)
        {
            this.plugin = plugin;
            editing = Serialization.GetClone(plugin.client.Settings);
            plugin.client.Authenticated += (s, e) =>
            {
                OnPropertyChanged(nameof(IsUserLoggedIn));
            };

            logger.Debug("ViewModel is initialized");
        }

        public bool IsUserLoggedIn => plugin.client.IsUserLoggedIn();

        public RelayCommand<object> LoginCommand
        {
            get => new RelayCommand<object>((a) =>
            {
                plugin.client.Login();
            });
        }

        // Possible race condition!
        // BeginEdit() and first-time accessing
        // Settings is likely overlapped. Playnite
        // set DataContext right before BeginEdit()
        // when the user opens the settings for the
        // first time. When DataContext is set, WPF
        // emits OnDataContextChanged event which
        // I suspect causes race condition.
        //
        // In a nutshell. Cloning MUST be done at
        // the constructor, not here.
        public void BeginEdit()
        {
            
        }

        public void CancelEdit()
        {
            editing = Serialization.GetClone(plugin.client.Settings);
        }

        public void EndEdit()
        {
            plugin.client.Settings = editing;
            plugin.SavePluginSettings(editing);
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            return true;
        }
    }
}