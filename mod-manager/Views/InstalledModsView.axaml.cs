using Avalonia.Controls;
using TerrariaModManager.ViewModels;

namespace TerrariaModManager.Views;

public partial class InstalledModsView : UserControl
{
    public InstalledModsView()
    {
        InitializeComponent();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && DataContext is InstalledModsViewModel vm)
        {
            vm.UpdateSelection(listBox.SelectedItems!.Cast<object>().ToList());
        }
    }
}
