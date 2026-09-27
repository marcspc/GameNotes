using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using GameNotes.Services;
using GameNotes.UI;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;

namespace GameNotes
{
    public class GameNotesPlugin : GenericPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();

        public NotesStorageService Storage { get; }

        /// <summary>
        /// Phase 2: this will decide at runtime between LocalOnlySyncProvider
        /// and GitHubSyncProvider based on settings. In v1 it's always
        /// local-only, so "never upload without consent" and "no telemetry"
        /// hold "by construction": there is no code path that reaches the network.
        /// </summary>
        public ISyncProvider Sync { get; private set; }

        private GameNotesSettingsViewModel _settingsViewModel;

        public GameNotesPlugin(IPlayniteAPI api) : base(api)
        {
            Storage = new NotesStorageService(GetPluginUserDataPath());
            Sync = new LocalOnlySyncProvider();
            _settingsViewModel = new GameNotesSettingsViewModel(this);

            Properties = new GenericPluginProperties
            {
                HasSettings = true
            };
        }

        public override Guid Id { get; } = Guid.Parse("8f1c7a2e-9b3a-4e9a-9c2f-6a5b2f7c9d10");

        // ---------- Game context menu entry ----------
        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            // If more than one game is selected, we don't offer the option,
            // to avoid accidentally operating on several games at once.
            if (args.Games == null || args.Games.Count != 1)
            {
                yield break;
            }

            var game = args.Games[0];
            yield return new GameMenuItem
            {
                Description = "Notes",
                MenuSection = "Game Notes",
                Action = _ => OpenNotesWindow(game)
            };
        }

        // ---------- Sidebar quick access ----------
        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = "Game Notes",
                Icon = new System.Windows.Controls.TextBlock { Text = "📝", FontSize = 18 },
                Type = SiderbarItemType.Button,
                Activated = () =>
                {
                    var game = PlayniteApi.MainView.SelectedGames?.Count == 1
                        ? PlayniteApi.MainView.SelectedGames[0]
                        : null;

                    if (game == null)
                    {
                        PlayniteApi.Dialogs.ShowMessage(
                            "Select a single game in your library to see its notes.",
                            "Game Notes");
                        return;
                    }

                    OpenNotesWindow(game);
                }
            };
        }

        private void OpenNotesWindow(Game game)
        {
            try
            {
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMinimizeButton = false,
                });
                window.Title = $"Notes — {game.Name}";
                window.Width = 1100;
                window.Height = 720;
                window.Content = new NotesWindowControl(this, game);
                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error opening the notes window.");
                PlayniteApi.Dialogs.ShowErrorMessage(
                    "Could not open the notes window. Check the Playnite log for details.",
                    "Game Notes");
            }
        }

        public override ISettings GetSettings(bool firstRunSettings) => _settingsViewModel;

        public override UserControl GetSettingsView(bool firstRunSettings) => new GameNotesSettingsView();

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            // Nothing to do on startup in v1: there's nothing to sync and no
            // connection to open.
        }
    }
}
