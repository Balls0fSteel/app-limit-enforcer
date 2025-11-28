using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppLimitEnforcer.Models;

namespace AppLimitEnforcer.Services;

/// <summary>
/// Service for monitoring running processes and enforcing time limits.
/// </summary>
public class ProcessMonitorService : IDisposable
{
    private readonly DataService _dataService;
    private AppData _appData;
    private System.Threading.Timer? _monitorTimer;
    private readonly object _lockObject = new();
    private bool _isDisposed;
    private DateTime _lastSaveTime = DateTime.MinValue;

    public event EventHandler<WarningEventArgs>? WarningTriggered;
    public event EventHandler<AppKillEventArgs>? AppKilled;
    public event EventHandler<AppKillFailedEventArgs>? AppKillFailed;
    public event EventHandler<UsageUpdatedEventArgs>? UsageUpdated;

    public ProcessMonitorService(DataService dataService)
    {
        _dataService = dataService;
        _appData = new AppData();
    }

    /// <summary>
    /// Initializes the service by loading data.
    /// </summary>
    public async Task InitializeAsync()
    {
        _appData = await _dataService.LoadDataAsync();
        _dataService.CleanupOldRecords(_appData);
    }

    /// <summary>
    /// Gets the current app data.
    /// </summary>
    public AppData AppData => _appData;

    /// <summary>
    /// Starts the monitoring timer.
    /// </summary>
    public void Start()
    {
        var interval = TimeSpan.FromSeconds(_appData.Settings.PollingIntervalSeconds);
        _monitorTimer = new System.Threading.Timer(MonitorCallback, null, TimeSpan.Zero, interval);
    }

    /// <summary>
    /// Stops the monitoring timer.
    /// </summary>
    public void Stop()
    {
        _monitorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>
    /// Adds a new rule.
    /// </summary>
    public async Task AddRuleAsync(AppLimitRule rule)
    {
        lock (_lockObject)
        {
            _appData.Rules.Add(rule);
        }
        await SaveDataAsync();
    }

    /// <summary>
    /// Updates an existing rule.
    /// </summary>
    public async Task UpdateRuleAsync(AppLimitRule rule)
    {
        lock (_lockObject)
        {
            var index = _appData.Rules.FindIndex(r => r.Id == rule.Id);
            if (index >= 0)
            {
                _appData.Rules[index] = rule;
            }
        }
        await SaveDataAsync();
    }

    /// <summary>
    /// Removes a rule.
    /// </summary>
    public async Task RemoveRuleAsync(Guid ruleId)
    {
        lock (_lockObject)
        {
            _appData.Rules.RemoveAll(r => r.Id == ruleId);
            _appData.UsageRecords.RemoveAll(u => u.RuleId == ruleId);
        }
        await SaveDataAsync();
    }

    /// <summary>
    /// Gets usage record for a rule on today's date.
    /// </summary>
    public AppUsageRecord GetOrCreateTodayUsage(Guid ruleId)
    {
        var today = DateTime.Today;
        lock (_lockObject)
        {
            var usage = _appData.UsageRecords.FirstOrDefault(u => u.RuleId == ruleId && u.Date == today);
            if (usage == null)
            {
                usage = new AppUsageRecord
                {
                    RuleId = ruleId,
                    Date = today,
                    UsedSecondsToday = 0,
                    WarningShown = false
                };
                _appData.UsageRecords.Add(usage);
            }
            return usage;
        }
    }

    /// <summary>
    /// Saves data to disk.
    /// </summary>
    public async Task SaveDataAsync()
    {
        _lastSaveTime = DateTime.Now;
        await _dataService.SaveDataAsync(_appData);
    }

    private void MonitorCallback(object? state)
    {
        try
        {
            MonitorProcesses();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Monitor error: {ex.Message}");
        }
    }

    private void MonitorProcesses()
    {
        List<AppLimitRule> rulesToCheck;
        lock (_lockObject)
        {
            rulesToCheck = _appData.Rules.Where(r => r.IsEnabled).ToList();
        }

        var runningProcesses = Process.GetProcesses();
        var matchedRules = new HashSet<Guid>();

        foreach (var rule in rulesToCheck)
        {
            var matchingProcesses = FindMatchingProcesses(runningProcesses, rule);

            if (matchingProcesses.Count > 0)
            {
                matchedRules.Add(rule.Id);
                var usage = GetOrCreateTodayUsage(rule.Id);
                var limitSeconds = rule.DailyLimitMinutes * 60;
                var warningThresholdSeconds = (rule.DailyLimitMinutes - rule.WarningMinutesBefore) * 60;

                // Check if already over limit
                if (usage.UsedSecondsToday >= limitSeconds)
                {
                    foreach (var process in matchingProcesses)
                    {
                        TryKillProcess(process, rule);
                    }
                }
                else
                {
                    // Add time
                    lock (_lockObject)
                    {
                        usage.UsedSecondsToday += _appData.Settings.PollingIntervalSeconds;
                    }

                    // Check if we need to show warning
                    if (usage.UsedSecondsToday >= warningThresholdSeconds && !usage.WarningShown)
                    {
                        usage.WarningShown = true;
                        var remainingMinutes = (limitSeconds - usage.UsedSecondsToday) / 60;
                        OnWarningTriggered(rule, remainingMinutes);
                    }

                    // Check if limit is now exceeded
                    if (usage.UsedSecondsToday >= limitSeconds)
                    {
                        foreach (var process in matchingProcesses)
                        {
                            TryKillProcess(process, rule);
                        }
                    }

                    OnUsageUpdated(rule.Id, usage.UsedSecondsToday, limitSeconds);
                }
            }
        }

        // Save periodically (every 30 seconds)
        if ((DateTime.Now - _lastSaveTime).TotalSeconds >= 30)
        {
            _ = SaveDataAsync();
        }

        // Dispose process objects
        foreach (var process in runningProcesses)
        {
            try { process.Dispose(); } catch { }
        }
    }

    private List<Process> FindMatchingProcesses(Process[] processes, AppLimitRule rule)
    {
        var matches = new List<Process>();
        var searchName = rule.ProcessNameOrPath.ToLowerInvariant();

        // Remove .exe extension if present for comparison
        if (searchName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            searchName = searchName[..^4];
        }

        foreach (var process in processes)
        {
            try
            {
                var processName = process.ProcessName.ToLowerInvariant();

                // Match by process name
                if (processName == searchName)
                {
                    matches.Add(process);
                    continue;
                }

                // Try to match by full path if it looks like a path
                if (rule.ProcessNameOrPath.Contains('\\') || rule.ProcessNameOrPath.Contains('/'))
                {
                    try
                    {
                        var processPath = process.MainModule?.FileName?.ToLowerInvariant();
                        if (processPath != null && processPath == rule.ProcessNameOrPath.ToLowerInvariant())
                        {
                            matches.Add(process);
                        }
                    }
                    catch
                    {
                        // Access denied for some processes
                    }
                }
            }
            catch
            {
                // Process may have exited
            }
        }

        return matches;
    }

    private void TryKillProcess(Process process, AppLimitRule rule)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                OnAppKilled(rule, process.ProcessName);
            }
        }
        catch (Exception ex)
        {
            OnAppKillFailed(rule, process.ProcessName, ex.Message);
        }
    }

    protected virtual void OnWarningTriggered(AppLimitRule rule, int remainingMinutes)
    {
        WarningTriggered?.Invoke(this, new WarningEventArgs(rule, remainingMinutes));
    }

    protected virtual void OnAppKilled(AppLimitRule rule, string processName)
    {
        AppKilled?.Invoke(this, new AppKillEventArgs(rule, processName));
    }

    protected virtual void OnAppKillFailed(AppLimitRule rule, string processName, string error)
    {
        AppKillFailed?.Invoke(this, new AppKillFailedEventArgs(rule, processName, error));
    }

    protected virtual void OnUsageUpdated(Guid ruleId, int usedSeconds, int limitSeconds)
    {
        UsageUpdated?.Invoke(this, new UsageUpdatedEventArgs(ruleId, usedSeconds, limitSeconds));
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _monitorTimer?.Dispose();
        }
    }
}

public class WarningEventArgs : EventArgs
{
    public AppLimitRule Rule { get; }
    public int RemainingMinutes { get; }

    public WarningEventArgs(AppLimitRule rule, int remainingMinutes)
    {
        Rule = rule;
        RemainingMinutes = remainingMinutes;
    }
}

public class AppKillEventArgs : EventArgs
{
    public AppLimitRule Rule { get; }
    public string ProcessName { get; }

    public AppKillEventArgs(AppLimitRule rule, string processName)
    {
        Rule = rule;
        ProcessName = processName;
    }
}

public class AppKillFailedEventArgs : EventArgs
{
    public AppLimitRule Rule { get; }
    public string ProcessName { get; }
    public string Error { get; }

    public AppKillFailedEventArgs(AppLimitRule rule, string processName, string error)
    {
        Rule = rule;
        ProcessName = processName;
        Error = error;
    }
}

public class UsageUpdatedEventArgs : EventArgs
{
    public Guid RuleId { get; }
    public int UsedSeconds { get; }
    public int LimitSeconds { get; }

    public UsageUpdatedEventArgs(Guid ruleId, int usedSeconds, int limitSeconds)
    {
        RuleId = ruleId;
        UsedSeconds = usedSeconds;
        LimitSeconds = limitSeconds;
    }
}
