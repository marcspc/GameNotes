using System;
using System.ComponentModel;
using Playnite.SDK;
using Playnite.SDK.Data;

namespace GameNotes
{
    /// <summary>
    /// Minimal INotifyPropertyChanged base class, kept local instead of
    /// depending on Playnite.SDK's own ObservableObject: its namespace has
    /// moved between SDK versions, so owning this tiny piece ourselves
    /// avoids the plugin breaking on a future SDK update.
    /// </summary>
    public abstract class LocalObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected void SetValue<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class GameNotesSettings : LocalObservableObject
    {
        // --- v1 ---
        private bool _confirmBeforeDelete = true;
        public bool ConfirmBeforeDelete
        {
            get => _confirmBeforeDelete;
            set => SetValue(ref _confirmBeforeDelete, value);
        }

        // --- Reserved for Phase 2 (GitHub). Saved to the settings JSON
        // already so we never have to migrate the settings file when sync
        // is enabled, but in v1 the settings UI shows these disabled and
        // GameNotesPlugin ignores GitHubSyncEnabled == true (see
        // LocalOnlySyncProvider). ---

        private bool _gitHubSyncEnabled = false;
        public bool GitHubSyncEnabled
        {
            get => _gitHubSyncEnabled;
            set => SetValue(ref _gitHubSyncEnabled, value);
        }

        private string _gitHubRepoUrl = "";
        public string GitHubRepoUrl
        {
            get => _gitHubRepoUrl;
            set => SetValue(ref _gitHubRepoUrl, value);
        }

        private bool _encryptionEnabled = false;
        public bool EncryptionEnabled
        {
            get => _encryptionEnabled;
            set => SetValue(ref _encryptionEnabled, value);
        }
    }

    public class GameNotesSettingsViewModel : LocalObservableObject, ISettings
    {
        private readonly GameNotesPlugin _plugin;
        private GameNotesSettings _editingClone;

        private GameNotesSettings _settings;
        public GameNotesSettings Settings
        {
            get => _settings;
            set => SetValue(ref _settings, value);
        }

        public GameNotesSettingsViewModel(GameNotesPlugin plugin)
        {
            _plugin = plugin;
            var saved = plugin.LoadPluginSettings<GameNotesSettings>();
            Settings = saved ?? new GameNotesSettings();
        }

        public void BeginEdit()
        {
            _editingClone = Serialization.GetClone(Settings);
        }

        public void CancelEdit()
        {
            Settings = _editingClone;
        }

        public void EndEdit()
        {
            _plugin.SavePluginSettings(Settings);
        }

        public bool VerifySettings(out System.Collections.Generic.List<string> errors)
        {
            errors = new System.Collections.Generic.List<string>();
            if (Settings.GitHubSyncEnabled)
            {
                // GitHub sync isn't implemented yet in v1: we warn instead of
                // failing silently.
                errors.Add("GitHub sync is coming in a future version. This checkbox has no effect yet.");
            }
            return true; // don't block saving, just warn.
        }
    }
}
