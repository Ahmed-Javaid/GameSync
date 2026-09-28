using Avalonia.Media;

namespace GameSync.UI.Controls;

/// <summary>The design system's icon set (design/system/components/bundle.js → Icon): SVG path data on a 24px grid.</summary>
public static class Icons
{
    private static readonly Dictionary<string, string[]> Paths = new()
    {
        ["home"] = ["M3 11l9-7 9 7", "M5 10v10h14V10", "M10 20v-6h4v6"],
        ["library"] = ["M4 4h7v7H4z", "M13 4h7v7h-7z", "M4 13h7v7H4z", "M13 13h7v7h-7z"],
        ["search"] = ["M11 4a7 7 0 1 0 0 14a7 7 0 1 0 0-14", "M20 20l-4-4"],
        ["saves"] = ["M5 3h11l3 3v15H5z", "M8 3v6h8V3", "M8 21v-7h8v7"],
        ["terminal"] = ["M3 5h18v14H3z", "M7 10l3 2-3 2", "M12 15h5"],
        ["activity"] = ["M3 12h4l3-8 4 16 3-8h4"],
        ["settings"] = ["M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6", "M19 12a7 7 0 0 0-.1-1.2l2-1.6-2-3.4-2.4 1a7 7 0 0 0-2-1.2L14 3h-4l-.5 2.6a7 7 0 0 0-2 1.2l-2.4-1-2 3.4 2 1.6A7 7 0 0 0 5 12c0 .4 0 .8.1 1.2l-2 1.6 2 3.4 2.4-1a7 7 0 0 0 2 1.2L10 21h4l.5-2.6a7 7 0 0 0 2-1.2l2.4 1 2-3.4-2-1.6c.1-.4.1-.8.1-1.2z"],
        ["play"] = ["M7 4.5v15l12-7.5z"],
        ["sync"] = ["M4 12a8 8 0 0 1 14-5.3L20 9", "M20 4v5h-5", "M20 12a8 8 0 0 1-14 5.3L4 15", "M4 20v-5h5"],
        ["share"] = ["M12 3v12", "M7 8l5-5 5 5", "M5 14v6h14v-6"],
        ["zip"] = ["M5 3h10l4 4v14H5z", "M10 3v2h2v2h-2v2h2v2h-2v2", "M9 15h4v4H9z"],
        ["folder"] = ["M3 6h6l2 2h10v11H3z"],
        ["cloud"] = ["M7 18h11a4 4 0 0 0 .5-8A6 6 0 0 0 7 9a4.5 4.5 0 0 0 0 9z"],
        ["upload"] = ["M12 16V5", "M7 10l5-5 5 5", "M5 19h14"],
        ["download"] = ["M12 5v11", "M7 11l5 5 5-5", "M5 19h14"],
        ["check"] = ["M5 12.5l4.5 4.5L19 7.5"],
        ["alert"] = ["M12 3l9.5 17h-19z", "M12 10v4", "M12 17h.01"],
        ["pause"] = ["M8 5v14", "M16 5v14"],
        ["lock"] = ["M6 11h12v10H6z", "M8.5 11V8a3.5 3.5 0 0 1 7 0v3"],
        ["unplug"] = ["M9 3v5", "M15 3v5", "M6 8h12v3a6 6 0 0 1-12 0z", "M12 17v4"],
        ["block"] = ["M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18", "M5.6 5.6l12.8 12.8"],
        ["archive"] = ["M3 4h18v5H3z", "M5 9v11h14V9", "M10 13h4"],
        ["pin"] = ["M9 3h6l-1 6 4 4H6l4-4z", "M12 13v8"],
        ["clock"] = ["M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18", "M12 7v5l3 2"],
        ["chevronRight"] = ["M9 5l7 7-7 7"],
        ["chevronDown"] = ["M5 9l7 7 7-7"],
        ["chevronsRight"] = ["M6 6l6 6-6 6", "M13 6l6 6-6 6"],
        ["x"] = ["M6 6l12 12", "M18 6L6 18"],
        ["info"] = ["M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18", "M12 11v6", "M12 7.5h.01"],
        ["copy"] = ["M8 8h12v12H8z", "M4 16V4h12"],
        ["monitor"] = ["M3 4h18v12H3z", "M8 20h8", "M12 16v4"],
        ["shield"] = ["M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6z", "M8.5 12l2.5 2.5 4.5-5"],
        ["palette"] = ["M12 3a9 9 0 1 0 0 18c1.1 0 1.8-.8 1.8-1.8 0-.5-.2-.9-.5-1.2-.3-.3-.5-.8-.5-1.2 0-1 .8-1.8 1.8-1.8H17a4 4 0 0 0 4-4c0-4.4-4-8-9-8z", "M7.5 11.5h.01", "M9.5 7.5h.01", "M14.5 7.5h.01"],
        ["drive"] = ["M3 13h18v6H3z", "M5 13l2.5-8h9L19 13", "M17 16h.01"],
        ["bell"] = ["M6 16v-5a6 6 0 0 1 12 0v5l1.5 2h-15z", "M10 20.5a2 2 0 0 0 4 0"],
        ["plus"] = ["M12 5v14", "M5 12h14"],
        ["sun"] = ["M12 8a4 4 0 1 0 0 8a4 4 0 1 0 0-8", "M12 2.5v2", "M12 19.5v2", "M2.5 12h2", "M19.5 12h2", "M5.3 5.3l1.4 1.4", "M17.3 17.3l1.4 1.4", "M5.3 18.7l1.4-1.4", "M17.3 6.7l1.4-1.4"],
        ["moon"] = ["M20 14.5A8.5 8.5 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z"],
        ["pencil"] = ["M4 20h4L19 9l-4-4L4 16z", "M13.5 6.5l4 4"],
        ["reset"] = ["M4 12a8 8 0 1 0 2.3-5.7L4 8.6", "M4 3.5v5.1h5.1"],
        ["star"] = ["M12 3.6l2.6 5.3 5.8.8-4.2 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8-4.2-4.1 5.8-.8z"],
        ["sort"] = ["M7 4v16", "M3.5 7.5L7 4l3.5 3.5", "M17 20V4", "M13.5 16.5L17 20l3.5-3.5"],
        ["chevronLeft"] = ["M15 5l-7 7 7 7"],
        ["arrowLeft"] = ["M19 12H5", "M11 18l-6-6 6-6"],
        ["trophy"] = ["M7 4h10v5a5 5 0 0 1-10 0z", "M7 5.5H4V7a3.5 3.5 0 0 0 3.6 3.5", "M17 5.5h3V7a3.5 3.5 0 0 1-3.6 3.5", "M12 14v4", "M8 20.5h8", "M9.5 18h5"],
        ["external"] = ["M14 4h6v6", "M20 4l-9 9", "M18 13.5V20H4V6h6.5"],
        ["file"] = ["M6 3h8l4 4v14H6z", "M14 3v4h4"],
        ["key"] = ["M8 10a4 4 0 1 0 0 8a4 4 0 1 0 0-8", "M10.9 11.1L19 3", "M16 6l2.5 2.5", "M13.5 8.5l2 2"],
        ["more"] = ["M6 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2", "M12 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2", "M18 11a1 1 0 1 0 0 2a1 1 0 1 0 0-2"],
        ["logo"] = ["M3 3h12v12H3z", "M9 9h12v12H9z"],
    };

    private static readonly Dictionary<string, Geometry[]> Parsed = [];

    public static IReadOnlyCollection<string> Names => Paths.Keys;

    /// <summary>The icon's paths, parsed once; an unknown name draws <c>info</c>, as the design system does.</summary>
    public static Geometry[]? Geometries(string? name)
    {
        if (name is null)
        {
            return null;
        }

        if (!Paths.ContainsKey(name))
        {
            name = "info";
        }

        lock (Parsed)
        {
            if (!Parsed.TryGetValue(name, out var geometries))
            {
                geometries = Paths[name].Select(p => (Geometry)StreamGeometry.Parse(p)).ToArray();
                Parsed[name] = geometries;
            }

            return geometries;
        }
    }
}
