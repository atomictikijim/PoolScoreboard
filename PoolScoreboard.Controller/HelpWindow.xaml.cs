using System.Windows;
using System.Windows.Controls;

namespace PoolScoreboard.Controller;

public partial class HelpWindow : Window
{
    public HelpWindow()
    {
        InitializeComponent();
    }

    private void ShowOverview_Click(object sender, RoutedEventArgs e) => ShowSection(OverviewPanel, OverviewNavButton);

    private void ShowController_Click(object sender, RoutedEventArgs e) => ShowSection(ControllerPanel, ControllerNavButton);

    private void ShowObs_Click(object sender, RoutedEventArgs e) => ShowSection(ObsPanel, ObsNavButton);

    private void ShowStreamDeck_Click(object sender, RoutedEventArgs e) => ShowSection(StreamDeckPanel, StreamDeckNavButton);

    private void ShowSection(StackPanel panel, Button navButton)
    {
        OverviewPanel.Visibility = Visibility.Collapsed;
        ControllerPanel.Visibility = Visibility.Collapsed;
        ObsPanel.Visibility = Visibility.Collapsed;
        StreamDeckPanel.Visibility = Visibility.Collapsed;
        panel.Visibility = Visibility.Visible;

        OverviewNavButton.Style = (Style)FindResource("NavButtonStyle");
        ControllerNavButton.Style = (Style)FindResource("NavButtonStyle");
        ObsNavButton.Style = (Style)FindResource("NavButtonStyle");
        StreamDeckNavButton.Style = (Style)FindResource("NavButtonStyle");
        navButton.Style = (Style)FindResource("ActiveButtonStyle");

        ((ScrollViewer)((Grid)panel.Parent).Parent).ScrollToTop();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
