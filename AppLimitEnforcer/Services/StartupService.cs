using System;
using Microsoft.Win32;

namespace AppLimitEnforcer.Services;

/// <summary>
/// Service for managing Windows startup registration.
/// </summary>
public static class StartupService
{
    private const string AppName = "AppLimitEnforcer";
    private const string RegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Checks if the application is registered to start with Windows.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enables the application to start with Windows.
    /// </summary>
    public static bool EnableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return false;

            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return false;

            key.SetValue(AppName, $"\"{exePath}\"");
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Disables the application from starting with Windows.
    /// </summary>
    public static bool DisableStartup()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, true);
            if (key == null) return false;

            if (key.GetValue(AppName) != null)
            {
                key.DeleteValue(AppName);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sets the startup state based on the provided value.
    /// </summary>
    public static bool SetStartupEnabled(bool enabled)
    {
        return enabled ? EnableStartup() : DisableStartup();
    }
}
