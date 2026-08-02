namespace WeiTimer.Views;

/// <summary>
/// Small modal confirming (or letting the user edit) the OCR'd carat count
/// before it's added to the daily total, mirroring window.py's
/// _show_carat_confirm_dialog (an Adw.AlertDialog + extra text-entry child).
/// </summary>
public partial class CaratConfirmDialog : Wpf.Ui.Controls.FluentWindow
{
    public int? ConfirmedAmount { get; private set; }

    public CaratConfirmDialog(int? parsedValue)
    {
        InitializeComponent();
        AmountBox.Text = parsedValue?.ToString() ?? string.Empty;
        AmountBox.Focus();
        AmountBox.SelectAll();
    }

    private void OnCancelClick(object sender, System.Windows.RoutedEventArgs e)
    {
        ConfirmedAmount = null;
        DialogResult = false;
    }

    private void OnConfirmClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!int.TryParse(AmountBox.Text.Trim(), out var amount))
        {
            AmountBox.BorderBrush = System.Windows.Media.Brushes.Red;
            return;
        }
        ConfirmedAmount = amount;
        DialogResult = true;
    }
}
