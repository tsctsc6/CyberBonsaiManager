using System.Windows.Media;

namespace CyberBonsaiManager.Utilities;

public class StringWithColor
{
    public string Value { get; set; } = string.Empty;
    public Brush Color { get; set; } = Brushes.Black;
}