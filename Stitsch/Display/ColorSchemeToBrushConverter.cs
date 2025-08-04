using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Simscop.Pl.Wpf.Converters
{
    public class ColorSchemeToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string name && _colorMapBrushes.TryGetValue(name, out var brush))
                return brush;

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static readonly Dictionary<string, Brush> _colorMapBrushes = new()
{
    { "Gray", CreateLinearGradient(Colors.Black, Colors.White) },
    { "Red", CreateLinearGradient(Colors.DarkRed, Colors.Red) },
    { "Green", CreateLinearGradient(Colors.DarkGreen, Colors.Lime) },
    { "Blue", CreateLinearGradient(Colors.DarkBlue, Colors.Cyan) },
    { "Purple", CreateLinearGradient(Colors.Indigo, Colors.Violet) },
    { "Pink", CreateLinearGradient(Colors.HotPink, Colors.LightPink) },
    { "Bone", CreateGradientBrush(new[] {
        ("#000000", 0.0), ("#4C4C4C", 0.25), ("#AAAAAA", 0.5), ("#DDDDDD", 0.75), ("#FFFFFF", 1.0)
    }) },
    { "Jet", CreateGradientBrush(new[] {
        ("#00007F", 0.0), ("#0000FF", 0.25), ("#00FFFF", 0.5), ("#FFFF00", 0.75), ("#FF0000", 1.0)
    }) },
    { "Rainbow", CreateGradientBrush(new[] {
        ("#FF0000", 0.0), ("#FF7F00", 0.2), ("#FFFF00", 0.4), ("#00FF00", 0.6), ("#0000FF", 0.8), ("#8B00FF", 1.0)
    }) },
    { "Summer", CreateLinearGradient(Colors.Yellow, Colors.Green) },
    { "Spring", CreateLinearGradient(Colors.Magenta, Colors.Green) },
    { "Autumn", CreateGradientBrush(new[] {
        ("Red", 0.0), ("Orange", 0.5), ("Yellow", 1.0)
    }) },
    { "Winter", CreateGradientBrush(new[] {
        ("Blue", 0.0), ("LightBlue", 0.5), ("White", 1.0)
    }) },
    { "Cool", CreateLinearGradient(Colors.Cyan, Colors.Magenta) },
    { "Ocean", CreateGradientBrush(new[] {
        ("#000033", 0.0), ("#006699", 0.5), ("#00CCCC", 1.0)
    }) },
    { "Spot", CreateGradientBrush(new[] {
        ("Black", 0.0), ("White", 0.5), ("Black", 1.0)
    }) },
    { "DeepGreen", CreateGradientBrush(new[] {
        ("#001400", 0.0), ("#004d00", 0.3), ("#00aa00", 0.6), ("#00ff00", 1.0)
    }) },
    { "Hsv", CreateHueGradient() },
    { "Parula", CreateParulaGradient() },
    { "Hot", CreateGradientBrush(new[] {
        ("#0B0000", 0.0), ("#FF0000", 0.3), ("#FFFF00", 0.6), ("#FFFFFF", 1.0)
    }) },
    { "Magma", CreateGradientBrush(new[] {
        ("#000004", 0.0), ("#3B0F70", 0.25), ("#8C2981", 0.5), ("#DE4968", 0.75), ("#FEE825", 1.0)
    }) },
    { "Inferno", CreateGradientBrush(new[] {
        ("#000004", 0.0), ("#420A68", 0.25), ("#932667", 0.5), ("#DD513A", 0.75), ("#FBA60C", 1.0)
    }) },
    { "Plasma", CreateGradientBrush(new[] {
        ("#0D0887", 0.0), ("#6A00A8", 0.25), ("#CB4679", 0.5), ("#F89441", 0.75), ("#F0F921", 1.0)
    }) },
    { "Viridis", CreateGradientBrush(new[] {
        ("#440154", 0.0), ("#3B528B", 0.25), ("#21908C", 0.5), ("#5DC963", 0.75), ("#FDE725", 1.0)
    }) },
    { "Cividis", CreateGradientBrush(new[] {
        ("#00204C", 0.0), ("#2F4F7B", 0.25), ("#726FA5", 0.5), ("#BBAA6B", 0.75), ("#FFD645", 1.0)
    }) },
    { "Twilight", CreateGradientBrush(new[] {
        ("#e2d9e2", 0.0), ("#a88dbc", 0.25), ("#5f4b66", 0.5), ("#2a2e38", 0.75), ("#141415", 1.0)
    }) },
    { "TwilightShifted", CreateGradientBrush(new[] {
        ("#F0E442", 0.0), ("#C05479", 0.25), ("#7B3791", 0.5), ("#3B2D74", 0.75), ("#19162B", 1.0)
    }) },
    { "Turbo", CreateGradientBrush(new[] {
        ("#30123b", 0.0), ("#4148a1", 0.25), ("#49b4e6", 0.5), ("#e3e949", 0.75), ("#f90000", 1.0)
    }) },
};

        private static Brush CreateLinearGradient(Color start, Color end)
        {
            return new LinearGradientBrush(
                new GradientStopCollection {
                new GradientStop(start, 0),
                new GradientStop(end, 1)
                },
                new Point(0, 0.5), new Point(1, 0.5)
            );
        }

        private static Brush CreateHueGradient()
        {
            return new LinearGradientBrush(
                new GradientStopCollection {
                new GradientStop(Color.FromRgb(255, 0, 0), 0.0),   // 红
                new GradientStop(Color.FromRgb(255, 255, 0), 0.17), // 黄
                new GradientStop(Color.FromRgb(0, 255, 0), 0.33),   // 绿
                new GradientStop(Color.FromRgb(0, 255, 255), 0.5),  // 青
                new GradientStop(Color.FromRgb(0, 0, 255), 0.66),   // 蓝
                new GradientStop(Color.FromRgb(255, 0, 255), 0.83), // 紫
                new GradientStop(Color.FromRgb(255, 0, 0), 1.0)     // 回到红
                },
                new Point(0, 0.5), new Point(1, 0.5)
            );
        }

        private static Brush CreateParulaGradient()
        {
            return new LinearGradientBrush(
                new GradientStopCollection {
                new GradientStop(Color.FromRgb(53, 42, 135), 0.0),
                new GradientStop(Color.FromRgb(15, 92, 221), 0.25),
                new GradientStop(Color.FromRgb(18, 125, 216), 0.5),
                new GradientStop(Color.FromRgb(7, 156, 207), 0.75),
                new GradientStop(Color.FromRgb(134, 181, 229), 0.9),
                new GradientStop(Color.FromRgb(230, 230, 128), 1.0)
                },
                new Point(0, 0.5), new Point(1, 0.5)
            );
        }

        private static LinearGradientBrush CreateGradientBrush((string colorCode, double offset)[] stops)
        {
            var gradientStops = new GradientStopCollection();
            foreach (var (colorCode, offset) in stops)
            {
                var color = (Color)ColorConverter.ConvertFromString(colorCode);
                gradientStops.Add(new GradientStop(color, offset));
            }

            return new LinearGradientBrush(gradientStops, new Point(0, 0), new Point(1, 0));
        }
    }
}
