namespace PoolScoreboard.Core.Models;

/// <summary>
/// Operator-set overlay colors. Values are hex color strings (e.g. "#1a2332").
/// </summary>
public class ColorTheme
{
    public string Background { get; set; } = "#1a2332";
    public string Accent { get; set; } = "#00d4ff";
    public string Text { get; set; } = "#f0f0f0";
}
