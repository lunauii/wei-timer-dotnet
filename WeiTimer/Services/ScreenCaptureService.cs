using System;
using System.Drawing;
using System.Globalization;

namespace WeiTimer.Services;

/// <summary>
/// Screen capture. Windows has exactly one capture path (Win32 GDI), unlike the
/// Linux original's Wayland/X11/portal backend selection -- so this is just
/// geometry parsing plus a single CopyFromScreen call.
///
/// Geometry strings keep the "X,Y WxH" format from the Python app (a
/// slurp/grim-style string) purely so it round-trips through config storage in
/// a human-readable way; there is no cross-tool compatibility reason to keep it
/// on Windows.
/// </summary>
public static class ScreenCaptureService
{
    public static Rectangle ParseGeometry(string geometry)
    {
        var parts = geometry.Split(' ', 2);
        if (parts.Length != 2)
            throw new FormatException($"Invalid geometry string: '{geometry}'");

        var xy = parts[0].Split(',');
        var wh = parts[1].Split('x');
        var x = int.Parse(xy[0], CultureInfo.InvariantCulture);
        var y = int.Parse(xy[1], CultureInfo.InvariantCulture);
        var w = int.Parse(wh[0], CultureInfo.InvariantCulture);
        var h = int.Parse(wh[1], CultureInfo.InvariantCulture);
        return new Rectangle(x, y, w, h);
    }

    public static string ToGeometryString(Rectangle rect) =>
        $"{rect.X},{rect.Y} {rect.Width}x{rect.Height}";

    /// <summary>Captures the given physical-pixel screen rectangle (as parsed from a
    /// geometry string) into a new Bitmap.</summary>
    public static Bitmap CaptureRegion(string geometry) => CaptureRegion(ParseGeometry(geometry));

    public static Bitmap CaptureRegion(Rectangle rect)
    {
        var bitmap = new Bitmap(rect.Width, rect.Height);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(rect.Location, Point.Empty, rect.Size);
        return bitmap;
    }
}
