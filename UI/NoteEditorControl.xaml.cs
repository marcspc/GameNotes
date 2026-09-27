using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using GameNotes.Models;
using Playnite.SDK.Models;

namespace GameNotes.UI
{
    /// <summary>
    /// Visual editor for a note. The user never sees raw HTML: they work on
    /// a normal RichTextBox and this class translates to/from the storage
    /// format (see <see cref="HtmlDocConverter"/>).
    /// </summary>
    public partial class NoteEditorControl : UserControl
    {
        private GameNotesPlugin _plugin;
        private Game _game;
        private NoteMeta _currentNote;

        private readonly DispatcherTimer _autosaveTimer;
        private bool _isLoadingDocument;
        private bool _dirty;

        public NoteEditorControl()
        {
            InitializeComponent();

            _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _autosaveTimer.Tick += (s, e) => { _autosaveTimer.Stop(); Save(); };

            DataObject.AddPastingHandler(Editor, OnPaste);
            Editor.Document.PageWidth = double.NaN; // fit the control's width
        }

        public void Initialize(GameNotesPlugin plugin, Game game)
        {
            _plugin = plugin;
            _game = game;
        }

        // ---------- Load / save ----------

        public void LoadNote(NoteMeta note)
        {
            FlushPendingSave();
            _currentNote = note;

            if (note == null)
            {
                Editor.Document = new FlowDocument();
                TitleBox.Text = "";
                Editor.IsEnabled = false;
                TitleBox.IsEnabled = false;
                return;
            }

            Editor.IsEnabled = true;
            TitleBox.IsEnabled = true;
            TitleBox.Text = note.DisplayName;

            _isLoadingDocument = true;
            try
            {
                var html = _plugin.Storage.LoadNoteContent(_game.Id, note);
                var gameDir = _plugin.Storage.GetGameDirFullPath(_game.Id);
                Editor.Document = HtmlDocConverter.FromHtml(html, gameDir);
                AttachTableHandlers(Editor.Document);
            }
            catch (Exception ex)
            {
                // A corrupted note must not break the window or other notes.
                Editor.Document = new FlowDocument(new Paragraph(new Run(
                    "[This note could not be loaded correctly. The original file was not modified.]")));
                Playnite.SDK.LogManager.GetLogger().Error(ex, $"Error loading note {note.Id}.");
            }
            finally
            {
                _isLoadingDocument = false;
            }

            SetSaveIndicator("Saved");
        }

        /// <summary>
        /// Tables loaded from disk must also trigger autosave when the user
        /// edits a cell or changes rows/columns.
        /// </summary>
        private void AttachTableHandlers(FlowDocument document)
        {
            foreach (var block in document.Blocks)
            {
                AttachTableHandlersToBlock(block);
            }
        }

        private void AttachTableHandlersToBlock(Block block)
        {
            if (block is BlockUIContainer buc && buc.Child is TableGridControl grid)
            {
                grid.ContentChanged += (s, args) =>
                {
                    _dirty = true;
                    SetSaveIndicator("Saving...");
                    _autosaveTimer.Stop();
                    _autosaveTimer.Start();
                };
            }
            else if (block is List list)
            {
                foreach (var item in list.ListItems)
                {
                    foreach (var inner in item.Blocks) AttachTableHandlersToBlock(inner);
                }
            }
        }

        private void OnEditorTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingDocument || _currentNote == null) return;
            _dirty = true;
            SetSaveIndicator("Saving...");
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }

        private void OnTitleLostFocus(object sender, RoutedEventArgs e)
        {
            if (_currentNote == null) return;
            var newTitle = TitleBox.Text?.Trim();
            if (string.IsNullOrEmpty(newTitle) || newTitle == _currentNote.DisplayName) return;

            _plugin.Storage.RenameNote(_game.Id, _currentNote.Id, newTitle);
            _currentNote.Title = newTitle;
        }

        public void FlushPendingSave()
        {
            if (_dirty)
            {
                _autosaveTimer.Stop();
                Save();
            }
        }

        private void Save()
        {
            if (_currentNote == null) return;

            try
            {
                var gameDir = _plugin.Storage.GetGameDirFullPath(_game.Id);
                var (html, imageIds) = HtmlDocConverter.ToHtml(Editor.Document, gameDir);
                _currentNote.ImageIds = imageIds;
                _plugin.Storage.SaveNoteContent(_game.Id, _currentNote, html);
                _dirty = false;
                SetSaveIndicator("Saved");
            }
            catch (Exception ex)
            {
                SetSaveIndicator("Error saving");
                Playnite.SDK.LogManager.GetLogger().Error(ex, $"Error saving note {_currentNote?.Id}.");
            }
        }

        private void SetSaveIndicator(string text) => SaveIndicator.Text = text;

        // ---------- Text formatting ----------

        private void OnBoldClick(object sender, RoutedEventArgs e) => EditingCommands.ToggleBold.Execute(null, Editor);
        private void OnItalicClick(object sender, RoutedEventArgs e) => EditingCommands.ToggleItalic.Execute(null, Editor);

        private void OnStrikeClick(object sender, RoutedEventArgs e)
        {
            var selection = Editor.Selection;
            if (selection.IsEmpty) return;

            var current = selection.GetPropertyValue(Inline.TextDecorationsProperty);
            bool isStrike = current != DependencyProperty.UnsetValue && current == TextDecorations.Strikethrough;
            selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
                isStrike ? null : TextDecorations.Strikethrough);
        }

        private void OnH1Click(object sender, RoutedEventArgs e) => ApplyParagraphStyle(22, FontWeights.Bold);
        private void OnH2Click(object sender, RoutedEventArgs e) => ApplyParagraphStyle(17, FontWeights.Bold);
        private void OnParagraphClick(object sender, RoutedEventArgs e) => ApplyParagraphStyle(13, FontWeights.Normal);

        private void ApplyParagraphStyle(double fontSize, FontWeight weight)
        {
            Editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, fontSize);
            Editor.Selection.ApplyPropertyValue(TextElement.FontWeightProperty, weight);
        }

        private void OnBulletListClick(object sender, RoutedEventArgs e) => EditingCommands.ToggleBullets.Execute(null, Editor);
        private void OnNumberedListClick(object sender, RoutedEventArgs e) => EditingCommands.ToggleNumbering.Execute(null, Editor);

        private void OnInsertLinkClick(object sender, RoutedEventArgs e)
        {
            var text = _plugin.PlayniteApi.Dialogs.SelectString(
                "Link URL (https://...)", "Insert link", "https://");
            if (string.IsNullOrWhiteSpace(text?.SelectedString)) return;

            var url = text.SelectedString.Trim();
            var selectionText = Editor.Selection.Text;
            var linkText = string.IsNullOrEmpty(selectionText) ? url : selectionText;

            var link = new Hyperlink(new Run(linkText)) { NavigateUri = TryUri(url) };
            link.RequestNavigate += (s, args) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args.Uri.ToString()) { UseShellExecute = true }); }
                catch { /* invalid link: don't break the note */ }
            };

            Editor.Selection.Text = "";
            Editor.CaretPosition.Paragraph.Inlines.Add(link);
        }

        private static Uri TryUri(string s) => Uri.TryCreate(s, UriKind.Absolute, out var u) ? u : new Uri("about:blank");

        // ---------- Images ----------

        private void OnInsertImageClick(object sender, RoutedEventArgs e)
        {
            var result = _plugin.PlayniteApi.Dialogs.SelectFile("Images|*.png;*.jpg;*.jpeg;*.webp");
            if (string.IsNullOrEmpty(result)) return;

            InsertImageFromFile(result);
        }

        private void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            // Ctrl+V with an image on the clipboard inserts it directly.
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Bitmap)) return;

            var bitmap = e.SourceDataObject.GetData(DataFormats.Bitmap) as BitmapSource;
            if (bitmap == null) return;

            InsertImageFromBitmap(bitmap);
            e.CancelCommand();
        }

        private void InsertImageFromFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var ext = Path.GetExtension(path).ToLowerInvariant();
            InsertImageBytes(bytes, ext);
        }

        private void InsertImageFromBitmap(BitmapSource bitmap)
        {
            using (var stream = new MemoryStream())
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
                InsertImageBytes(stream.ToArray(), ".png");
            }
        }

        /// <summary>
        /// The image is saved as a standalone file (never embedded as
        /// Base64), and the document only keeps a reference. Visually
        /// limited to the available editor width.
        /// </summary>
        private void InsertImageBytes(byte[] bytes, string extension)
        {
            if (_currentNote == null) return;

            var (imageId, relativePath) = _plugin.Storage.SaveImage(_game.Id, bytes, extension);
            var gameDir = _plugin.Storage.GetGameDirFullPath(_game.Id);
            var fullPath = Path.Combine(gameDir, relativePath);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.EndInit();

            var image = new Image
            {
                Source = bitmap,
                MaxWidth = 480,
                Stretch = Stretch.Uniform,
                Tag = relativePath, // used by HtmlDocConverter to rebuild data-image-id and src
            };

            var container = new BlockUIContainer(image);
            Editor.Document.Blocks.InsertAfter(Editor.CaretPosition.Paragraph ?? Editor.Document.Blocks.LastBlock, container);

            _dirty = true;
            SetSaveIndicator("Saving...");
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }

        // ---------- Tables ----------

        private void OnInsertTableClick(object sender, RoutedEventArgs e)
        {
            var table = new TableGridControl();
            table.InitializeNew(3, 3);
            table.ContentChanged += (s, args) =>
            {
                _dirty = true;
                SetSaveIndicator("Saving...");
                _autosaveTimer.Stop();
                _autosaveTimer.Start();
            };

            var container = new BlockUIContainer(table);
            var anchor = Editor.CaretPosition.Paragraph ?? Editor.Document.Blocks.LastBlock;
            Editor.Document.Blocks.InsertAfter(anchor, container);

            _dirty = true;
            SetSaveIndicator("Saving...");
            _autosaveTimer.Stop();
            _autosaveTimer.Start();
        }

        private void OnEditorPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                FlushPendingSave();
                e.Handled = true;
            }
        }
    }
}
