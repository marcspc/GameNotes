using System;
using System.Threading.Tasks;

namespace GameNotes.Services
{
    /// <summary>
    /// Used in v1. GitHub sync is disabled by default, and this class
    /// guarantees no network operation ever happens: every method is a
    /// no-op and returns "LocalOnly" or a neutral result.
    ///
    /// Once Phase 2 is implemented, GameNotesPlugin will decide at runtime
    /// which ISyncProvider to inject (this one or GitHubSyncProvider) based
    /// on GameNotesSettings.GitHubSyncEnabled, without the rest of the code
    /// changing.
    /// </summary>
    public class LocalOnlySyncProvider : ISyncProvider
    {
        public bool IsConfigured => false;

        public Task<SyncStatus> GetStatusAsync(Guid gameId, string noteId)
            => Task.FromResult(SyncStatus.LocalOnly);

        public Task<SyncResult> PushNoteAsync(Guid gameId, string noteId)
            => Task.FromResult(new SyncResult { Success = true, Status = SyncStatus.LocalOnly, Message = "Sync is disabled." });

        public Task<SyncResult> PullNoteAsync(Guid gameId, string noteId)
            => Task.FromResult(new SyncResult { Success = true, Status = SyncStatus.LocalOnly, Message = "Sync is disabled." });

        public Task<SyncResult> SyncAllAsync()
            => Task.FromResult(new SyncResult { Success = true, Status = SyncStatus.LocalOnly, Message = "Sync is disabled." });

        public Task DisconnectAsync() => Task.CompletedTask;
    }
}
