using System.Windows;
using System.Windows.Controls;
using PoolScoreboard.Controller.Assets.Flags;

namespace PoolScoreboard.Controller;

public partial class FlagPickerWindow : Window
{
    public FlagIconEntry? SelectedEntry { get; private set; }

    public FlagPickerWindow()
    {
        InitializeComponent();
    }

    private void FlagTile_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FlagIconEntry entry)
        {
            SelectedEntry = entry;
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
