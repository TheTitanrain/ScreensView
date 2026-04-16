using System.Windows;
using ScreensView.Shared;
using ScreensView.Shared.Models;
using ScreensView.Viewer.Services;

namespace ScreensView.Viewer.Views;

public partial class AddMultipleComputersWindow : Window
{
    private readonly ISet<string> _existingHosts;

    public List<ComputerConfig> Results { get; private set; } = [];

    public AddMultipleComputersWindow(ISet<string> existingHosts)
    {
        InitializeComponent();
        _existingHosts = existingHosts;
        HostsPortBox.Text = Constants.DefaultPort.ToString();
        RangePortBox.Text = Constants.DefaultPort.ToString();
        UpdateHostsCounter();
        UpdateRangeStatus();
    }

    private bool IsRangeTab => Tabs.SelectedIndex == 1;

    // ── Hosts tab ───────────────────────────────────────────────────────────

    private void HostsBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateHostsCounter();

    private void UpdateHostsCounter()
    {
        var hosts = BulkComputerParser.ParseHosts(
            HostsBox.Text,
            int.TryParse(HostsPortBox.Text, out var p) ? p : 0,
            _existingHosts);
        HostsCountLabel.Text = hosts.Count > 0
            ? string.Format(LocalizationService.Get("Str.AddMultiple.CountFormat"), hosts.Count)
            : string.Empty;
        BtnAdd.IsEnabled = true;
        BtnAdd.Content = hosts.Count > 0
            ? string.Format(LocalizationService.Get("Str.AddMultiple.BtnAddCount"), hosts.Count)
            : LocalizationService.Get("Str.AddMultiple.BtnAdd");
    }

    // ── IP range tab ────────────────────────────────────────────────────────

    private void IpRange_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateRangeStatus();

    private void UpdateRangeStatus()
    {
        if (string.IsNullOrWhiteSpace(StartIpBox.Text) &&
            string.IsNullOrWhiteSpace(EndIpBox.Text))
        {
            RangeStatusLabel.Text = string.Empty;
            RangeStatusLabel.Foreground = System.Windows.Media.Brushes.Gray;
            BtnAdd.IsEnabled = false;
            BtnAdd.Content = LocalizationService.Get("Str.AddMultiple.BtnAdd");
            return;
        }

        var result = BulkComputerParser.ParseIpRange(
            StartIpBox.Text, EndIpBox.Text,
            int.TryParse(RangePortBox.Text, out var p) ? p : 0,
            _existingHosts, out var error);

        if (error != null)
        {
            RangeStatusLabel.Text = error;
            RangeStatusLabel.Foreground = System.Windows.Media.Brushes.Red;
            BtnAdd.IsEnabled = false;
            BtnAdd.Content = LocalizationService.Get("Str.AddMultiple.BtnAdd");
        }
        else
        {
            RangeStatusLabel.Text = string.Format(LocalizationService.Get("Str.AddMultiple.CountFormat"), result.Count);
            RangeStatusLabel.Foreground = System.Windows.Media.Brushes.Gray;
            BtnAdd.IsEnabled = true;
            BtnAdd.Content = string.Format(LocalizationService.Get("Str.AddMultiple.BtnAddCount"), result.Count);
        }
    }

    private void Tabs_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        if (IsRangeTab)
            UpdateRangeStatus();
        else
            UpdateHostsCounter();
    }

    // ── OK ──────────────────────────────────────────────────────────────────

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!IsRangeTab)
        {
            if (!int.TryParse(HostsPortBox.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show(LocalizationService.Get("Str.Val.InvalidPort"), LocalizationService.Get("Str.Val.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Results = [.. BulkComputerParser.ParseHosts(HostsBox.Text, port, _existingHosts)];
            if (Results.Count == 0)
            {
                MessageBox.Show(LocalizationService.Get("Str.Val.EnterHost2"), LocalizationService.Get("Str.Val.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }
        else
        {
            if (!int.TryParse(RangePortBox.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show(LocalizationService.Get("Str.Val.InvalidPort"), LocalizationService.Get("Str.Val.Title"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            Results = [.. BulkComputerParser.ParseIpRange(
                StartIpBox.Text, EndIpBox.Text, port, _existingHosts, out _)];
        }

        DialogResult = true;
    }
}
