using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ognia.Wpf.Models.Playlist;
using Ognia.Wpf.ViewModels.MainPane;

namespace Ognia.Wpf.Views.MainPane;

public partial class LibraryView : UserControl
{
    private Point _dragStart;
    private PlaylistResponse? _dragItem;

    public LibraryView()
    {
        InitializeComponent();
    }

    private void PlaylistsControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(PlaylistsControl);
        _dragItem = GetPlaylistUnderMouse(e.OriginalSource as DependencyObject);
    }

    private void PlaylistsControl_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragItem == null) return;

        var pos = e.GetPosition(PlaylistsControl);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(PlaylistsControl, _dragItem, DragDropEffects.Move);
        _dragItem = null;
    }

    private void PlaylistsControl_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PlaylistResponse))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void PlaylistsControl_Drop(object sender, DragEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm) return;
        if (!e.Data.GetDataPresent(typeof(PlaylistResponse))) return;

        var dragged = (PlaylistResponse)e.Data.GetData(typeof(PlaylistResponse))!;
        var target = GetPlaylistUnderMouse(e.OriginalSource as DependencyObject);

        int toIndex;
        if (target != null)
        {
            toIndex = vm.Session.Playlists.IndexOf(target);
        }
        else
        {
            // Dropped on empty space — move to end.
            toIndex = vm.Session.Playlists.Count - 1;
        }

        vm.MovePlaylist(dragged, toIndex);
    }

    private static PlaylistResponse? GetPlaylistUnderMouse(DependencyObject? source)
    {
        while (source != null && source is not FrameworkElement fe)
            source = VisualTreeHelper.GetParent(source);

        // Walk up to the tile Grid (the DataTemplate root), whose DataContext
        // is a PlaylistResponse.
        while (source != null)
        {
            if (source is FrameworkElement element &&
                element.DataContext is PlaylistResponse p)
                return p;

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void PlaylistTile_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not LibraryViewModel vm) return;
        if (sender is not FrameworkElement fe || fe.Tag is not PlaylistResponse playlist) return;

        vm.OpenPlaylistCommand.Execute(playlist);
    }

    private async void PlaylistDelete_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // prevent tile click from also firing

        if (DataContext is not LibraryViewModel vm) return;
        if (sender is not FrameworkElement fe || fe.Tag is not PlaylistResponse playlist) return;

        await vm.Session.DeletePlaylistAsync(playlist);
    }
}