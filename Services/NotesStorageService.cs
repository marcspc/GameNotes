using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameNotes.Models;
using Newtonsoft.Json;
using Playnite.SDK;

namespace GameNotes.Services
{
    /// <summary>
    /// All local persistence lives here. On-disk layout:
    ///
    /// {PluginUserDataPath}/GameNotes/
    ///   ├── Game-{GameId}/
    ///   │     ├── notes.json
    ///   │     ├── note-{NoteId}.html
    ///   │     └── images/
    ///   │           ├── {ImageId}.png
    ///   │           └── ...
    ///
    /// Copying the whole "GameNotes" folder is a valid backup. GitHub is
    /// never touched from this class.
    /// </summary>
    public class NotesStorageService
    {
        private readonly string _rootPath;
        private readonly ILogger _logger = LogManager.GetLogger();
        private readonly object _ioLock = new object();

        public NotesStorageService(string pluginUserDataPath)
        {
            _rootPath = Path.Combine(pluginUserDataPath, "GameNotes");
            Directory.CreateDirectory(_rootPath);
        }

        private string GameDir(Guid gameId) => Path.Combine(_rootPath, "Game-" + gameId);
        private string ImagesDir(Guid gameId) => Path.Combine(GameDir(gameId), "images");
        private string IndexPath(Guid gameId) => Path.Combine(GameDir(gameId), "notes.json");

        // ---------- Index ----------

        public GameNotesIndex LoadIndex(Guid gameId)
        {
            lock (_ioLock)
            {
                var path = IndexPath(gameId);
                if (!File.Exists(path))
                {
                    return new GameNotesIndex { GameId = gameId.ToString() };
                }

                try
                {
                    var json = File.ReadAllText(path);
                    var index = JsonConvert.DeserializeObject<GameNotesIndex>(json);
                    // A corrupted index must not take down the rest of the addon.
                    return index ?? new GameNotesIndex { GameId = gameId.ToString() };
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Corrupted notes index for game {gameId}, starting a fresh in-memory index without deleting the existing files.");
                    // We do NOT delete notes.json: it's left for manual
                    // inspection, and we return an empty in-memory index
                    // instead of crashing Playnite.
                    return new GameNotesIndex { GameId = gameId.ToString() };
                }
            }
        }

        private void SaveIndex(Guid gameId, GameNotesIndex index)
        {
            lock (_ioLock)
            {
                Directory.CreateDirectory(GameDir(gameId));
                var path = IndexPath(gameId);
                var tmpPath = path + ".tmp";
                var json = JsonConvert.SerializeObject(index, Formatting.Indented);

                // Atomic write: write to a temp file first, then replace, to
                // minimize the risk of a half-written notes.json if Playnite
                // is closed mid-save.
                File.WriteAllText(tmpPath, json);
                if (File.Exists(path))
                {
                    File.Replace(tmpPath, path, null);
                }
                else
                {
                    File.Move(tmpPath, path);
                }
            }
        }

        // ---------- Notes ----------

        public NoteMeta CreateNote(Guid gameId, string title = null)
        {
            var index = LoadIndex(gameId);
            var note = new NoteMeta
            {
                Title = title ?? DefaultTitle(),
            };
            note.ContentFile = $"note-{note.Id}.html";
            index.Notes.Add(note);
            SaveIndex(gameId, index);

            var contentPath = Path.Combine(GameDir(gameId), note.ContentFile);
            File.WriteAllText(contentPath, "<p></p>");
            return note;
        }

        /// <summary>Default title format: "Note YY/MM/DD HH:mm:ss".</summary>
        private static string DefaultTitle()
        {
            return "Note " + DateTime.Now.ToString("yy/MM/dd HH:mm:ss");
        }

        public string LoadNoteContent(Guid gameId, NoteMeta note)
        {
            var path = Path.Combine(GameDir(gameId), note.ContentFile);
            if (!File.Exists(path))
            {
                return "<p></p>";
            }

            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Could not read content for note {note.Id}.");
                return "<p><i>[Could not load this note's content]</i></p>";
            }
        }

        public void SaveNoteContent(Guid gameId, NoteMeta note, string htmlContent)
        {
            lock (_ioLock)
            {
                Directory.CreateDirectory(GameDir(gameId));
                var path = Path.Combine(GameDir(gameId), note.ContentFile);
                var tmpPath = path + ".tmp";
                File.WriteAllText(tmpPath, htmlContent);
                if (File.Exists(path))
                {
                    File.Replace(tmpPath, path, null);
                }
                else
                {
                    File.Move(tmpPath, path);
                }

                var index = LoadIndex(gameId);
                var stored = index.Notes.FirstOrDefault(n => n.Id == note.Id);
                if (stored != null)
                {
                    stored.ModifiedUtc = DateTime.UtcNow;
                    stored.ImageIds = note.ImageIds;
                    SaveIndex(gameId, index);
                }
            }
        }

        public void RenameNote(Guid gameId, string noteId, string newTitle)
        {
            var index = LoadIndex(gameId);
            var note = index.Notes.FirstOrDefault(n => n.Id == noteId);
            if (note == null) return;

            // Renaming only touches Title, never Id or ContentFile.
            note.Title = newTitle;
            note.ModifiedUtc = DateTime.UtcNow;
            SaveIndex(gameId, index);
        }

        public void DeleteNote(Guid gameId, string noteId, bool deleteOrphanImages = true)
        {
            var index = LoadIndex(gameId);
            var note = index.Notes.FirstOrDefault(n => n.Id == noteId);
            if (note == null) return;

            index.Notes.Remove(note);
            SaveIndex(gameId, index);

            try
            {
                var contentPath = Path.Combine(GameDir(gameId), note.ContentFile);
                if (File.Exists(contentPath)) File.Delete(contentPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Could not delete content file for note {noteId}.");
            }

            if (deleteOrphanImages && note.ImageIds != null)
            {
                CleanupOrphanImages(gameId, index, note.ImageIds);
            }
        }

        /// <summary>Deletes images that are no longer used by any remaining note.</summary>
        private void CleanupOrphanImages(Guid gameId, GameNotesIndex indexAfterDelete, List<string> candidateImageIds)
        {
            var stillUsed = new HashSet<string>(indexAfterDelete.Notes.SelectMany(n => n.ImageIds ?? new List<string>()));
            foreach (var imgId in candidateImageIds)
            {
                if (stillUsed.Contains(imgId)) continue;
                foreach (var file in Directory.EnumerateFiles(ImagesDir(gameId), imgId + ".*"))
                {
                    try { File.Delete(file); }
                    catch (Exception ex) { _logger.Error(ex, $"Could not delete orphan image {file}."); }
                }
            }
        }

        // ---------- Images ----------

        /// <summary>
        /// Saves an image pasted by the user as a standalone file and
        /// returns the id + relative path to insert into the HTML.
        /// </summary>
        public (string imageId, string relativePath) SaveImage(Guid gameId, byte[] bytes, string extension)
        {
            Directory.CreateDirectory(ImagesDir(gameId));
            var imageId = Guid.NewGuid().ToString("N");
            var fileName = imageId + extension;
            var fullPath = Path.Combine(ImagesDir(gameId), fileName);
            File.WriteAllBytes(fullPath, bytes);
            return (imageId, "images/" + fileName);
        }

        public string GetGameDirFullPath(Guid gameId) => GameDir(gameId);
    }
}
