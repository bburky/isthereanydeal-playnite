using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace IsthereanydealCollectionSync
{
    public class IsthereanydealCollectionSync : GenericPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        public IsthereanydealCollectionSyncSettingsViewModel Settings { get; }
        public readonly IsthereanydealClient client;
        public readonly IPlayniteAPI playniteApi;

        public override Guid Id { get; } = Guid.Parse("1f1c327f-8896-47de-950c-c92dc9fab556");

        public IsthereanydealCollectionSync(IPlayniteAPI api) : base(api)
        {
            Settings = new IsthereanydealCollectionSyncSettingsViewModel(this);
            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
            client = new IsthereanydealClient(this, Settings);
            playniteApi = api;
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            yield return new GameMenuItem
            {
                Description = Localized("LOCIsThereAnyDealCollectionSyncImportMenu"),
                Action = (itemArgs) =>
                {
                    string dialogText = Localized("LOCIsThereAnyDealCollectionSyncImportMessageMultiple");
                    
                    if (itemArgs.Games.Count == 1)
                    {
                        dialogText = Localized("LOCIsThereAnyDealCollectionSyncImportMessageSingle", itemArgs.Games[0].Name);
                    }

                    PlayniteApi.Dialogs.ActivateGlobalProgress(new Func<GlobalProgressActionArgs, Task>(async (progressArgs) =>
                    {
                        try
                        {
                            //TODO: globalProgressActionArgs.CancelToken.IsCancellationRequested and add true to GlobalProgressOptions

                            if (!client.IsUserLoggedIn())
                            {
                                PlayniteApi.Dialogs.ShowErrorMessage(Localized("LOCIsThereAnyDealCollectionSyncErrorMessageNotLoggedIn"), Localized("LOCIsThereAnyDealCollectionSyncErrorCaption"));
                                return;
                            }
                            var failedGames = await client.Import(itemArgs.Games);

                            var resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportSucceedMultiple", itemArgs.Games.Count);

                            if (itemArgs.Games.Count == 1)
                            {
                                if (failedGames.HasItems())
                                {
                                    resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportFailureSingle", failedGames[0].Name);
                                }
                                else
                                {
                                    resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportSucceedSingle", itemArgs.Games[0].Name);
                                }
                            }
                            else
                            {
                                if (failedGames.Count == itemArgs.Games.Count)
                                {
                                    resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportFailureMultiple", itemArgs.Games.Count);
                                }
                                else if (failedGames.HasItems())
                                {
                                    resultDialogText = Localized("LOCIsThereAnyDealCollectionSyncImportMixed", itemArgs.Games.Count - failedGames.Count, failedGames.Count);
                                }
                            }

                            PlayniteApi.Dialogs.ShowMessage(resultDialogText, ("LOCIsThereAnyDealCollectionSyncErrorCaption"));
                        }
                        catch (Exception ex)
                        {
                            PlayniteApi.Dialogs.ShowErrorMessage(ex.Message, Localized("LOCIsThereAnyDealCollectionSyncErrorCaption"));
                        }
                    }), new GlobalProgressOptions(dialogText));
                }
            };
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new IsthereanydealCollectionSyncSettingsView();
        }

        public override void OnApplicationStopped(OnApplicationStoppedEventArgs args)
        {
            client.RemoveCategoryFromDatabase();
            base.OnApplicationStopped(args);
        }

        public static string Localized(string key)
        {
            return ResourceProvider.GetString(key);
        }

        public static string Localized(string key, params object[] args)
        {
            return string.Format(ResourceProvider.GetString(key), args);
        }
    }
}