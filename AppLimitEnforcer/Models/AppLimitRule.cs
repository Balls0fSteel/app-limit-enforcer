using System;
using System.Text.Json.Serialization;

namespace AppLimitEnforcer.Models;

/// <summary>
/// Represents a rule that defines time limits for an application.
/// </summary>
public class AppLimitRule
{
    /// <summary>
    /// Unique identifier for this rule.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The process name (without .exe) or full path to the executable.
    /// </summary>
    public string ProcessNameOrPath { get; set; } = string.Empty;

    /// <summary>
    /// Display name for the application.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Daily time limit in minutes.
    /// </summary>
    public int DailyLimitMinutes { get; set; } = 120; // Default 2 hours

    /// <summary>
    /// Minutes before the limit to show warning.
    /// </summary>
    public int WarningMinutesBefore { get; set; } = 5;

    /// <summary>
    /// Whether this rule is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Tracks the usage time for a specific app rule on a specific date.
/// </summary>
public class AppUsageRecord
{
    /// <summary>
    /// The ID of the AppLimitRule this record is for.
    /// </summary>
    public Guid RuleId { get; set; }

    /// <summary>
    /// The date this usage record is for (without time component).
    /// </summary>
    public DateTime Date { get; set; }

    /// <summary>
    /// Total accumulated usage time in seconds for this date.
    /// </summary>
    public int UsedSecondsToday { get; set; }

    /// <summary>
    /// Whether a warning has been shown for this session.
    /// </summary>
    public bool WarningShown { get; set; }
}

/// <summary>
/// Application settings.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// Whether to start with Windows.
    /// </summary>
    public bool StartWithWindows { get; set; } = true;

    /// <summary>
    /// Whether to start minimized to system tray.
    /// </summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>
    /// Polling interval in seconds to check running processes.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 5;
}

/// <summary>
/// Complete application data including rules, usage records, and settings.
/// </summary>
public class AppData
{
    public List<AppLimitRule> Rules { get; set; } = new();
    public List<AppUsageRecord> UsageRecords { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
}
