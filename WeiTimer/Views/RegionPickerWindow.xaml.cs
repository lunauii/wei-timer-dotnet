using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseButtonState = System.Windows.Input.MouseButtonState;
using Point = System.Windows.Point;

namespace WeiTimer.Views;

/// <summary>
/// Interactive drag-select overlay, the Windows equivalent of the Linux
/// original's slurp/slop subprocess call. One topmost, transparent, borderless
/// window spanning the virtual screen (so a drag can cross monitors, matching
/// slurp's behavior), with Esc to cancel.
/// </summary>
public partial class RegionPickerWindow : Window
{
    private Point? _startPoint;                          // DIPs, for the visual selection rectangle only
    private System.Drawing.Point? _startPhysical;         // physical screen pixels, for the actual capture rect

    public System.Drawing.Rectangle? SelectedRegion { get; private set; }

    public RegionPickerWindow()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
    }

    /// <summary>Shows the overlay modally and returns the selected region in
    /// physical screen pixels, or null if the user cancelled (Esc, or a
    /// too-small drag).</summary>
    public static System.Drawing.Rectangle? PickRegion()
    {
        var picker = new RegionPickerWindow();
        picker.ShowDialog();
        return picker.SelectedRegion;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;
        SelectedRegion = null;
        DialogResult = false;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(RootCanvas);
        _startPhysical = System.Windows.Forms.Cursor.Position;
        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, _startPoint.Value.X);
        Canvas.SetTop(SelectionRect, _startPoint.Value.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_startPoint is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(RootCanvas);
        var (x, y, w, h) = NormalizeRect(_startPoint.Value, current);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_startPoint is null || _startPhysical is null)
            return;
        ReleaseMouseCapture();

        // The selection rectangle drawn during the drag uses DIPs relative to
        // this window (fine for visual feedback), but the actual capture rect
        // is computed from raw physical cursor coordinates instead of scaling
        // those DIPs -- this window spans the whole virtual screen and only
        // ever has one DPI context, so a DIP->physical conversion is wrong on
        // whichever monitor doesn't match that context. Cursor.Position is a
        // GetCursorPos() wrapper: real physical pixels, correct on any monitor
        // regardless of its scaling, with no conversion needed at all.
        var endPhysical = System.Windows.Forms.Cursor.Position;
        var (x, y, w, h) = NormalizePhysicalRect(_startPhysical.Value, endPhysical);
        _startPoint = null;
        _startPhysical = null;

        if (w < 2 || h < 2)
        {
            // Too small to be a deliberate selection -- treat like cancel.
            SelectedRegion = null;
            DialogResult = false;
            return;
        }

        SelectedRegion = new System.Drawing.Rectangle(x, y, w, h);
        DialogResult = true;
    }

    private static (double X, double Y, double W, double H) NormalizeRect(Point a, Point b) => (
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X),
        Math.Abs(b.Y - a.Y));

    private static (int X, int Y, int W, int H) NormalizePhysicalRect(System.Drawing.Point a, System.Drawing.Point b) => (
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Abs(b.X - a.X),
        Math.Abs(b.Y - a.Y));
}
