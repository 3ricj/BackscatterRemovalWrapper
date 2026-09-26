using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BsxtBatch.Core.Models;

namespace BsxtBatch.App.Converters;

/// <summary>Maps a <see cref="JobStatus"/> to a foreground brush for the job list.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly Brush Queued = new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60));
    private static readonly Brush Running = new SolidColorBrush(Color.FromRgb(0x00, 0x6B, 0xC0));
    private static readonly Brush Completed = new SolidColorBrush(Color.FromRgb(0x1A, 0x7F, 0x33));
    private static readonly Brush Skipped = new SolidColorBrush(Color.FromRgb(0xB8, 0x86, 0x00));
    private static readonly Brush Failed = new SolidColorBrush(Color.FromRgb(0xC0, 0x2B, 0x2B));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var brush = value switch
        {
            JobStatus.Running => Running,
            JobStatus.Completed => Completed,
            JobStatus.Skipped => Skipped,
            JobStatus.Failed => Failed,
            _ => Queued,
        };
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
