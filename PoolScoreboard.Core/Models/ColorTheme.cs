namespace PoolScoreboard.Core.Models;

/// <summary>
/// Operator-set overlay colors. Values are hex color strings (e.g. "#1a2332").
/// </summary>
public class ColorTheme
{
    public string HomeBackground { get; set; } = "#1a2332";
    public string AwayBackground { get; set; } = "#1a2332";
    public string HomeAccent { get; set; } = "#00d4ff";
    public string AwayAccent { get; set; } = "#00d4ff";
    public string HomeText { get; set; } = "#f0f0f0";
    public string AwayText { get; set; } = "#f0f0f0";
}
