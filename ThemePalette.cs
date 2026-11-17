using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Veyro;

internal static class ThemePalette
{
    internal static readonly string[] Keys = { "Panel", "Surface", "Stroke", "Ink", "Secondary", "Accent" };
    internal static bool Valid(string? color) => color is not null && Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$");

    internal static string[] Resolve(string theme, Dictionary<string, string>? custom = null)
    {
        var colors = theme switch
        {
            "Pearl" => new[] { "#F4F1EC", "#E2DED7", "#CBC5BB", "#242830", "#60646C", "#9CBDEC" },
            "Iris" => new[] { "#201A30", "#30263F", "#51415E", "#FAF3FF", "#BFB0CF", "#985ADD" },
            "Forest" => new[] { "#14231E", "#24362E", "#40594B", "#EDF8EF", "#A5BCAF", "#377F60" },
            _ => new[] { "#1C1C1B", "#292929", "#444443", "#F1F4FB", "#AAAAAA", "#3297FF" }
        };
        if (theme == "Custom" && custom is not null)
            for (var i = 0; i < Keys.Length; i++)
                if (custom.TryGetValue(Keys[i], out var color) && Valid(color)) colors[i] = color;
        return colors;
    }
}
