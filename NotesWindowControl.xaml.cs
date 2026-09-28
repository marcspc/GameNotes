using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameNotes.Models;
using Playnite.SDK.Models;

namespace GameNotes.UI
{
    public partial class NotesWindowControl : UserControl, INotifyPropertyChanged
    {
        private readonly GameNotesPlugin _plugin;
        private readonly Game _game;

        public string GameTitle => _game.Name;
        public ObservableCollection<NoteMeta> Notes { get; } = new ObservableCollection<NoteMeta>();

        private NoteMeta _selectedNote;
        public NoteMeta SelectedNote
        {
            get => _selectedNote;
            set { _selectedNote = value; OnPropertyChanged(nameof(SelectedNote)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public NotesWindowControl(GameNotesPlugin plugin, Game game)
        {
            InitializeComponent();
            _plugin = plugin;
            _game = game;
            DataContext = this;

            Editor.Initialize(plugin, game);

            ReloadNotes();
        }

        private void ReloadNotes()
        {
            Notes.Clear();
            var index = _plugin.Storage.LoadIndex(_game.Id);
            foreach (var note in index.Notes.OrderByDescending(n => n.ModifiedUtc))
            {
                Notes.Add(note);
            }

            SelectedNote = Notes.FirstOrDefault();
            Editor.LoadNote(SelectedNote);
        }

        private void OnCreateNoteClick(object sender, RoutedEventArgs e)
        {
            // Creates a new note with an automatic name; can be renamed
            // afterwards by editing the title box in the editor.
            var note = _plugin.Storage.CreateNote(_game.Id);
            ReloadNotes();
            SelectedNote = Notes.FirstOrDefault(n => n.Id == note.Id);
            NotesListBox.SelectedItem = SelectedNote;
            Editor.LoadNote(SelectedNote);
        }

        private void OnNoteSelected(object sender, SelectionChangedEventArgs e)
        {
            // Before switching notes, flush any pending save on the one
            // currently being edited.
            Editor.FlushPendingSave();
            Editor.LoadNote(SelectedNote);
        }

        private void OnDeleteNoteClick(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var note = (NoteMeta)button.Tag;

            var settings = _plugin.GetSettings(false) as GameNotesSettingsViewModel;
            var confirmRequired = settings?.Settings?.ConfirmBeforeDelete ?? true;

            if (confirmRequired)
            {
                var result = _plugin.PlayniteApi.Dialogs.ShowMessage(
                    $"Delete the note \"{note.DisplayName}\"? This cannot be undone.",
                    "Delete note",
                    System.Windows.MessageBoxButton.YesNo);

                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }

            _plugin.Storage.DeleteNote(_game.Id, note.Id);
            ReloadNotes();
        }
    }
}
