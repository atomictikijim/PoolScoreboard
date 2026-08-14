using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PoolScoreboard.Controller.ViewModels;

/// <summary>
/// One numbered ball in a ball display. Shared by reference between the numeric row and the
/// 8-ball group displays, so pocketed state stays consistent no matter which layout shows it.
/// </summary>
public partial class BallItemViewModel : ObservableObject
{
    public int Number { get; }

    public Brush Color { get; }

    [ObservableProperty]
    private bool isPocketed;

    public BallItemViewModel(int number, Brush color)
    {
        Number = number;
        Color = color;
    }
}
