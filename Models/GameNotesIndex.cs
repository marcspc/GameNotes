using System.Collections.Generic;

namespace GameNotes.Models
{
    /// <summary>
    /// Represents notes.json inside GameNotes/{GameId}/.
    /// One index per game, keyed by the Playnite game GUID, never by the
    /// game's display name.
    /// </summary>
    public class GameNotesIndex
    {
        /// <summary>Storage format version, for future migrations.</summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>The Playnite game Id (Game.Id, a Guid).</summary>
        public string GameId { get; set; }

        public List<NoteMeta> Notes { get; set; } = new List<NoteMeta>();

        // Reserved for Phase 2: overall sync state for this game.
        public string SyncState { get; set; } = "local-only";
    }
}
