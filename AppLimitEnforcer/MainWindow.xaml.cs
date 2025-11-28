using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using AppLimitEnforcer.Models;
using AppLimitEnforcer.Services;

namespace AppLimitEnforcer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly DataService _dataService;
    private readonly ProcessMonitorService _monitorService;
    private readonly ObservableCollection<RuleViewModel> _rules;
    private System.Windows.Forms.NotifyIcon? _notifyIcon;
    private bool _isClosing;

    public MainWindow()
    {
        InitializeComponent();

        _dataService = new DataService();
        _monitorService = new ProcessMonitorService(_dataService);
        _rules = new ObservableCollection<RuleViewModel>();
        RulesListBox.ItemsSource = _rules;

        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        await _monitorService.InitializeAsync();

        // Set up event handlers
        _monitorService.WarningTriggered += OnWarningTriggered;
        _monitorService.AppKilled += OnAppKilled;
        _monitorService.AppKillFailed += OnAppKillFailed;
        _monitorService.UsageUpdated += OnUsageUpdated;

        // Load rules into view
        RefreshRulesList();

        // Set startup checkbox
        StartWithWindowsCheckBox.IsChecked = StartupService.IsStartupEnabled();

        // Start monitoring
        _monitorService.Start();

        // Initialize system tray icon
        InitializeNotifyIcon();

        // If settings say start minimized, minimize to tray
        if (_monitorService.AppData.Settings.StartMinimized)
        {
            MinimizeToTray();
        }
    }

    private void InitializeNotifyIcon()
    {
        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "App Limit Enforcer"
        };

        _notifyIcon.DoubleClick += (s, e) => ShowWindow();

        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (s, e) => ShowWindow());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (s, e) => ExitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void RefreshRulesList()
    {
        _rules.Clear();
        foreach (var rule in _monitorService.AppData.Rules)
        {
            var usage = _monitorService.GetOrCreateTodayUsage(rule.Id);
            _rules.Add(new RuleViewModel(rule, usage));
        }

        EmptyStateText.Visibility = _rules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnWarningTriggered(object? sender, WarningEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _notifyIcon?.ShowBalloonTip(
                5000,
                "Time Warning",
                $"{e.Rule.DisplayName} has approximately {e.RemainingMinutes} minutes remaining.",
                System.Windows.Forms.ToolTipIcon.Warning);

            System.Windows.MessageBox.Show(
                $"{e.Rule.DisplayName} has approximately {e.RemainingMinutes} minutes of allowed time remaining.\n\nThe application will be closed when the time limit is reached.",
                "Time Limit Warning",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        });
    }

    private void OnAppKilled(object? sender, AppKillEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _notifyIcon?.ShowBalloonTip(
                3000,
                "Application Closed",
                $"{e.ProcessName} was closed because the daily time limit was reached.",
                System.Windows.Forms.ToolTipIcon.Info);
        });
    }

    private void OnAppKillFailed(object? sender, AppKillFailedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            System.Windows.MessageBox.Show(
                $"Could not close {e.ProcessName}.\n\nReason: {e.Error}\n\nPlease close the application manually.",
                "Failed to Close Application",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        });
    }

    private void OnUsageUpdated(object? sender, UsageUpdatedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var ruleVm = _rules.FirstOrDefault(r => r.Id == e.RuleId);
            if (ruleVm != null)
            {
                ruleVm.UsedSecondsToday = e.UsedSeconds;
            }
        });
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var processName = ProcessNameTextBox.Text.Trim();
        if (string.IsNullOrEmpty(processName))
        {
            System.Windows.MessageBox.Show("Please enter a process name or browse for an executable.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(LimitHoursTextBox.Text, out int hours) || hours < 0)
        {
            hours = 0;
        }

        if (!int.TryParse(LimitMinutesTextBox.Text, out int minutes) || minutes < 0)
        {
            minutes = 0;
        }

        var totalMinutes = hours * 60 + minutes;
        if (totalMinutes <= 0)
        {
            System.Windows.MessageBox.Show("Please enter a valid time limit (at least 1 minute).", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(WarningMinutesTextBox.Text, out int warningMinutes) || warningMinutes < 0)
        {
            warningMinutes = 5;
        }

        var displayName = System.IO.Path.GetFileNameWithoutExtension(processName);

        var rule = new AppLimitRule
        {
            ProcessNameOrPath = processName,
            DisplayName = displayName,
            DailyLimitMinutes = totalMinutes,
            WarningMinutesBefore = warningMinutes,
            IsEnabled = true
        };

        await _monitorService.AddRuleAsync(rule);
        RefreshRulesList();

        // Clear input
        ProcessNameTextBox.Clear();
        LimitHoursTextBox.Text = "2";
        LimitMinutesTextBox.Text = "0";
        WarningMinutesTextBox.Text = "5";
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
            Title = "Select Application"
        };

        if (dialog.ShowDialog() == true)
        {
            ProcessNameTextBox.Text = dialog.FileName;
        }
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is Guid ruleId)
        {
            var result = System.Windows.MessageBox.Show(
                "Are you sure you want to remove this application from the list?",
                "Confirm Removal",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _monitorService.RemoveRuleAsync(ruleId);
                RefreshRulesList();
            }
        }
    }

    private async void EnabledCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.DataContext is RuleViewModel vm)
        {
            vm.IsEnabled = checkBox.IsChecked ?? false;
            var rule = _monitorService.AppData.Rules.FirstOrDefault(r => r.Id == vm.Id);
            if (rule != null)
            {
                rule.IsEnabled = vm.IsEnabled;
                await _monitorService.SaveDataAsync();
            }
        }
    }

    private void StartWithWindowsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var isChecked = StartWithWindowsCheckBox.IsChecked ?? false;
        StartupService.SetStartupEnabled(isChecked);
    }

    private void MinimizeToTrayButton_Click(object sender, RoutedEventArgs e)
    {
        MinimizeToTray();
    }

    private void MinimizeToTray()
    {
        Hide();
        _notifyIcon?.ShowBalloonTip(
            2000,
            "App Limit Enforcer",
            "Running in background. Double-click to open.",
            System.Windows.Forms.ToolTipIcon.Info);
    }

    private void ShowWindow()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_isClosing)
        {
            e.Cancel = true;
            MinimizeToTray();
        }
    }

    private async void ExitApplication()
    {
        _isClosing = true;
        _monitorService.Stop();
        await _monitorService.SaveDataAsync();
        _notifyIcon?.Dispose();
        _monitorService.Dispose();
        System.Windows.Application.Current.Shutdown();
    }
}

/// <summary>
/// ViewModel for displaying rules in the list.
/// </summary>
public class RuleViewModel : INotifyPropertyChanged
{
    private int _usedSecondsToday;
    private bool _isEnabled;

    public Guid Id { get; }
    public string DisplayName { get; }
    public string ProcessNameOrPath { get; }
    public int DailyLimitMinutes { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    public int UsedSecondsToday
    {
        get => _usedSecondsToday;
        set
        {
            _usedSecondsToday = value;
            OnPropertyChanged(nameof(UsedSecondsToday));
            OnPropertyChanged(nameof(UsageDisplay));
            OnPropertyChanged(nameof(UsagePercent));
        }
    }

    public string LimitDisplay => $"{DailyLimitMinutes / 60}h {DailyLimitMinutes % 60}m";

    public string UsageDisplay
    {
        get
        {
            var usedMinutes = UsedSecondsToday / 60;
            var remainingMinutes = Math.Max(0, DailyLimitMinutes - usedMinutes);
            return $"{usedMinutes}m / {DailyLimitMinutes}m";
        }
    }

    public double UsagePercent
    {
        get
        {
            if (DailyLimitMinutes <= 0) return 0;
            var usedMinutes = UsedSecondsToday / 60.0;
            return Math.Min(100, (usedMinutes / DailyLimitMinutes) * 100);
        }
    }

    public RuleViewModel(AppLimitRule rule, AppUsageRecord usage)
    {
        Id = rule.Id;
        DisplayName = rule.DisplayName;
        ProcessNameOrPath = rule.ProcessNameOrPath;
        DailyLimitMinutes = rule.DailyLimitMinutes;
        IsEnabled = rule.IsEnabled;
        UsedSecondsToday = usage.UsedSecondsToday;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}