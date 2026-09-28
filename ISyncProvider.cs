using System;
using System.Threading.Tasks;

namespace GameNotes.Services
{
    public enum SyncStatus
    {
        /// <summary>GitHub disabled: the only valid state in v1.</summary>
        LocalOnly,
        Synced,          // Synced
        LocalChanges,    // Local changes
        RemoteChanges,   // Remote changes
        Conflict,        // Conflict
        Error            // Error
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public SyncStatus Status { get; set; }
    }

    /// <summary>
    /// Contract that the future GitHubSyncProvider (Phase 2) must implement.
    /// Defined now so that:
    ///   - The rest of the plugin (UI, storage) never depends on GitHub directly.
    ///   - Adding sync later is "implement this interface + a settings
    ///     toggle", without touching NotesStorageService or the editor UI.
    ///
    /// In v1 the only registered implementation is <see cref="LocalOnlySyncProvider"/>.
    /// </summary>
    public interface ISyncProvider
    {
        bool IsConfigured { get; }

        Task<SyncStatus> GetStatusAsync(Guid gameId, string noteId);

        /// <summary>Uploads local changes for a note. No-op if !IsConfigured.</summary>
        Task<SyncResult> PushNoteAsync(Guid gameId, string noteId);

        /// <summary>Downloads remote changes for a note. No-op if !IsConfigured.</summary>
        Task<SyncResult> PullNoteAsync(Guid gameId, string noteId);

        /// <summary>Manual full sync ("Sync now").</summary>
        Task<SyncResult> SyncAllAsync();

        /// <summary>Disconnects the account/repository without deleting local data.</summary>
        Task DisconnectAsync();
    }
}
