using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ognia.Wpf.Services;
using Ognia.Wpf.ViewModels;
using Ognia.Wpf.ViewModels.MainPane;

namespace Ognia.Wpf.Views.MainPane;

public partial class PlaylistView : UserControl
{
    private Point _dragStart;
    private TrackRowViewModel? _dragItem;

    public PlaylistView()
    {
        InitializeComponent();
    }

    private void TracksList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(TracksList);

        // Don't hijack clicks on interactive controls inside a row.
        if (IsInteractiveSource(e.OriginalSource as DependencyObject))
        {
            _dragItem = null;
            return;
        }

        _dragItem = GetItemUnderMouse(e.OriginalSource as DependencyObject);
        if (_dragItem == null) return;
        if (DataContext is not PlaylistViewModel vm) return;

        var session = vm.Session;
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (shift)
        {
            session.SelectRange(_dragItem, additive: ctrl);
        }
        else if (ctrl)
        {
            session.ToggleSelection(_dragItem);
        }
        else
        {
            // The ListBox remains in Multiple mode for range interaction, so
            // clear its native visual selection for a plain row click.
            TracksList.SelectedItems.Clear();
            session.SelectSingle(_dragItem);
        }
    }

    private void TracksList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;

        var pos = e.GetPosition(TracksList);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(TracksList, _dragItem, DragDropEffects.Move);
        _dragItem = null;
    }

    private void TracksList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TrackRowViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void TracksList_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not PlaylistViewModel vm) return;
        if (vm.Session.IsBusy) return;
        if (!e.Data.GetDataPresent(typeof(TrackRowViewModel))) return;

        var dragged = (TrackRowViewModel)e.Data.GetData(typeof(TrackRowViewModel))!;
        var target = GetItemUnderMouse(e.OriginalSource as DependencyObject);

        var session = vm.Session;
        var full = session.SelectedPlaylistTracks;

        var fromIndex = full.IndexOf(dragged);
        if (fromIndex < 0) return;

        int toIndex = target != null ? full.IndexOf(target) : full.Count - 1;
        if (toIndex < 0 || toIndex == fromIndex) return;

        // Move within the full collection, then reindex + reapply filter.
        full.Move(fromIndex, toIndex);
        session.ReindexTracks();
        session.RefreshFilteredTracks();

        var orderedIds = full.Select(t => t.Id).ToList();
        var playlistId = session.SelectedPlaylist?.Id;
        if (playlistId == null) return;

        _ = PersistOrderAsync(vm, playlistId.Value, orderedIds);
    }

    private static async Task PersistOrderAsync(PlaylistViewModel vm, int playlistId, List<int> orderedIds)
    {
        var result = await vm.Session.Repo.ReorderTracksAsync(playlistId, orderedIds);
        if (!result.Success)
            NotificationService.Instance.Error(result.Error ?? "Failed to reorder tracks.");
    }

    private static TrackRowViewModel? GetItemUnderMouse(DependencyObject? source)
    {
        while (source != null && source is not ListBoxItem)
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);

        return (source as ListBoxItem)?.DataContext as TrackRowViewModel;
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button or CheckBox or ComboBox or ComboBoxItem or Slider or TextBox)
                return true;

            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }
        return false;
    }
}