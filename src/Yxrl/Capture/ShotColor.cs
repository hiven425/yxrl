using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace Yxrl.Capture;

internal readonly record struct ShotColor(byte Red, byte Green, byte Blue)
{
    public Color MediaColor => Color.FromRgb(Red, Green, Blue);

    public string Format(int format)
    {
        if (format == 0)
            return $"#{Red:X2}{Green:X2}{Blue:X2}";
        if (format == 1)
            return $"rgb({Red}, {Green}, {Blue})";

        double r = Red / 255.0, g = Green / 255.0, b = Blue / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double delta = max - min, light = (max + min) / 2;
        double hue = delta == 0 ? 0 : max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4;
        double degrees = (hue * 60 + 360) % 360;
        double saturation = delta == 0 ? 0 : delta / (1 - Math.Abs(2 * light - 1));
        return format == 2
            ? FormattableString.Invariant($"hsv({degrees:0.##}, {(max == 0 ? 0 : delta / max * 100):0.##}%, {max * 100:0.##}%)")
            : FormattableString.Invariant($"hsl({degrees:0.##}, {saturation * 100:0.##}%, {light * 100:0.##}%)");
    }

    public static bool TryParse(string text, out ShotColor color)
    {
        color = default;
        string value = text.Trim();
        if (value.Length > 40)
            return false;
        if (value.StartsWith('#') && value.Length is 4 or 7 &&
            uint.TryParse(value.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint hex))
        {
            color = value.Length == 4
                ? new((byte)((hex >> 8) * 17), (byte)((hex >> 4 & 15) * 17), (byte)((hex & 15) * 17))
                : new((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
            return true;
        }

        var rgb = Regex.Match(value, @"\Argb\(\s*([0-9]{1,3})\s*,\s*([0-9]{1,3})\s*,\s*([0-9]{1,3})\s*\)\z", RegexOptions.IgnoreCase);
        if (rgb.Success && byte.TryParse(rgb.Groups[1].Value, out byte r) &&
            byte.TryParse(rgb.Groups[2].Value, out byte g) && byte.TryParse(rgb.Groups[3].Value, out byte b))
        {
            color = new(r, g, b);
            return true;
        }

        const string number = @"([0-9]{1,3}(?:\.[0-9]{1,2})?)";
        var hue = Regex.Match(value, $@"\Ah(sv|sl)\(\s*{number}\s*,\s*{number}%\s*,\s*{number}%\s*\)\z", RegexOptions.IgnoreCase);
        if (!hue.Success)
            return false;
        double angle = double.Parse(hue.Groups[2].Value, CultureInfo.InvariantCulture);
        double saturation = double.Parse(hue.Groups[3].Value, CultureInfo.InvariantCulture);
        double level = double.Parse(hue.Groups[4].Value, CultureInfo.InvariantCulture);
        if (angle > 360 || saturation > 100 || level > 100)
            return false;

        double v = level / 100.0, s = saturation / 100.0;
        bool hsv = hue.Groups[1].Value.Equals("sv", StringComparison.OrdinalIgnoreCase);
        double chroma = hsv ? v * s : (1 - Math.Abs(2 * v - 1)) * s;
        double sector = angle % 360 / 60.0;
        double x = chroma * (1 - Math.Abs(sector % 2 - 1));
        double offset = hsv ? v - chroma : v - chroma / 2;
        var (red, green, blue) = (int)sector switch
        {
            0 => (chroma, x, 0d),
            1 => (x, chroma, 0d),
            2 => (0d, chroma, x),
            3 => (0d, x, chroma),
            4 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };
        byte Channel(double channel) => (byte)Math.Round((channel + offset) * 255);
        color = new(Channel(red), Channel(green), Channel(blue));
        return true;
    }
}
