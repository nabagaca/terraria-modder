using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace TerrariaModManager.Helpers;

public static class DialogHelper
{
    public static async Task<ButtonResult> ShowDialog(string title, string message,
        ButtonEnum buttons = ButtonEnum.Ok, Icon icon = Icon.None)
    {
        var box = MessageBoxManager.GetMessageBoxStandard(
            new MsBox.Avalonia.Dto.MessageBoxStandardParams
            {
                ContentTitle = title,
                ContentMessage = message,
                ButtonDefinitions = buttons,
                Icon = icon,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner
            });

        return App.MainWindow != null
            ? await box.ShowWindowDialogAsync(App.MainWindow)
            : await box.ShowAsync();
    }
}
