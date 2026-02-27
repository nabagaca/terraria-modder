using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using TerrariaModManager.Models;
using TerrariaModManager.ViewModels;

namespace TerrariaModManager.Views;

public partial class BrowseView : UserControl
{
    public BrowseView()
    {
        InitializeComponent();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is BrowseViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnCardTapped(object? sender, TappedEventArgs e)
    {
        // Don't open sidebar when clicking buttons (Install, View on Nexus)
        if (e.Source is Visual visual)
        {
            var current = visual;
            while (current != null && current != sender)
            {
                if (current is Button) return;
                current = current.GetVisualParent() as Visual;
            }
        }

        if (sender is Control control && control.DataContext is NexusMod mod
            && DataContext is BrowseViewModel vm)
        {
            vm.OpenDetailCommand.Execute(mod);
        }
    }
}
