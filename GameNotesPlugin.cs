using System;
using System.Collections.Generic;
using System.Linq;
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
        private System.Windows.Controls.TextBlock _sidebarIcon;

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
            // Kept as a field so OnGameSelected can update it live: it's the
            // same Control instance the sidebar is already displaying, so
            // changing its Text updates the icon in place without needing
            // to re-register the sidebar item.
            _sidebarIcon = new System.Windows.Controls.TextBlock { Text = "📝", FontSize = 18 };

            yield return new SidebarItem
            {
                Title = "Game Notes",
                Icon = _sidebarIcon,
                Type = SiderbarItemType.Button,
                Activated = () =>
                {
                    var selected = PlayniteApi.MainView.SelectedGames?.ToList();
                    var game = selected?.Count == 1 ? selected[0] : null;

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

        /// <summary>
        /// There's no reliable, theme-independent way to show a "has notes"
        /// indicator inside the game details panel itself (GetGameViewControl
        /// only renders on themes that explicitly declare a slot for it, and
        /// the stock Playnite themes don't). This is the closest
        /// theme-independent equivalent: the sidebar icon itself reflects
        /// whether the currently selected game already has notes, updating
        /// every time the selection changes.
        /// </summary>
        public override void OnGameSelected(OnGameSelectedEventArgs args)
        {
            if (_sidebarIcon == null) return;

            var selected = args.NewValue;
            var hasSingleGame = selected != null && selected.Count == 1;

            if (!hasSingleGame)
            {
                _sidebarIcon.Text = "📝";
                _sidebarIcon.ToolTip = "Game Notes";
                return;
            }

            var game = selected[0];
            var hasNotes = Storage.LoadIndex(game.Id).Notes.Count > 0;
            _sidebarIcon.Text = hasNotes ? "📒" : "📝";
            _sidebarIcon.ToolTip = hasNotes
                ? $"{game.Name} already has notes — click to open"
                : $"No notes yet for {game.Name} — click to create one";
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
