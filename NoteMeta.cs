using System;
using Newtonsoft.Json;

namespace GameNotes.Models
{
    /// <summary>
    /// Metadata for a note. The actual HTML content lives in a separate file
    /// (note-{Id}.html) so the index can be read quickly even with large
    /// libraries, without loading every note's full content.
    /// </summary>
    public class NoteMeta
    {
        /// <summary>Stable internal ID. NEVER changes, not even on rename.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Display name, freely editable by the user.</summary>
        public string Title { get; set; }

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// File name of the content, relative to the game's folder.
        /// Defaults to "note-{Id}.html".
        /// </summary>
        public string ContentFile { get; set; }

        /// <summary>
        /// IDs of images referenced by this note. Used to clean up orphan
        /// images when the note is deleted.
        /// </summary>
        public System.Collections.Generic.List<string> ImageIds { get; set; }
            = new System.Collections.Generic.List<string>();

        // --- Fields reserved for Phase 2 (GitHub sync) ---
        // Included in the model now so the storage format never needs a
        // migration later. In v1 these always stay null / default and are
        // never used for anything.

        /// <summary>Content hash at the last successful sync (used to detect local changes).</summary>
        public string LastSyncedHash { get; set; }

        /// <summary>Timestamp of the last successful sync with the remote.</summary>
        public DateTime? LastSyncedUtc { get; set; }

        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(Title) ? Id : Title;
    }
}
