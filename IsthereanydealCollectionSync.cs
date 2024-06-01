using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealCollectionSync : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private IsthereanydealCollectionSyncSettingsViewModel settings { get; set; }

        public override Guid Id { get; } = Guid.Parse("1f1c327f-8896-47de-950c-c92dc9fab556");

        public IsthereanydealCollectionSync(IPlayniteAPI api) : base(api)
        {
            settings = new IsthereanydealCollectionSyncSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = "Add to IsThereAnyDeal Collection",
                Action = (itemArgs) =>
                {
                    PlayniteApi.Dialogs.ActivateGlobalProgress(new Func<GlobalProgressActionArgs, Task>(async (progressArgs) =>
                    {
                        try
                        {
                            //TODO: globalProgressActionArgs.CancelToken.IsCancellationRequested and add true to GlobalProgressOptions

                            using (var view = PlayniteApi.WebViews.CreateOffscreenView())
                            {
                                var client = new IsthereanydealClient(this, view);
                                if (!await client.GetIsUserLoggedIn())
                                {
                                    PlayniteApi.Dialogs.ShowErrorMessage("User not logged in.\n\nLog into IsThereAnyDeal in add-ons extension settings", "IsThereAnyDeal Collection Sync");
                                    return;
                                }
                                var json = await client.generateImportJson(settings.Settings.ImportGroup, itemArgs.Games);
                                var result = await client.Import(json, settings.Settings.ImportModeReplace, settings.Settings.RemoveFromWaitlist);
                                PlayniteApi.Dialogs.ShowMessage(result, "IsThereAnyDeal Collection Sync");
                            }
                        }
                        catch (Exception ex)
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage(ex.ToString(), "IsThereAnyDeal Collection Sync Error");
                        }
                    }), new GlobalProgressOptions($"Importing games into Is There Any Deal collection"));
                }
            };
        }

        public override void OnGameInstalled(OnGameInstalledEventArgs args)
        {
            // Add code to be executed when game is finished installing.
        }

        public override void OnLibraryUpdated(OnLibraryUpdatedEventArgs args)
        {
            // Add code to be executed when library is updated.
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new IsthereanydealCollectionSyncSettingsView();
        }
    }
}