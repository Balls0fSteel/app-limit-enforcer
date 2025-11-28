using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AppLimitEnforcer.Models;

namespace AppLimitEnforcer.Services;

/// <summary>
/// Service for persisting and loading application data.
/// </summary>
public class DataService
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AppLimitEnforcer");

    private static readonly string DataFilePath = Path.Combine(AppDataFolder, "appdata.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Loads application data from disk.
    /// </summary>
    public async Task<AppData> LoadDataAsync()
    {
        try
        {
            if (!File.Exists(DataFilePath))
            {
                return new AppData();
            }

            var json = await File.ReadAllTextAsync(DataFilePath);
            return JsonSerializer.Deserialize<AppData>(json, JsonOptions) ?? new AppData();
        }
        catch
        {
            return new AppData();
        }
    }

    /// <summary>
    /// Saves application data to disk.
    /// </summary>
    public async Task SaveDataAsync(AppData data)
    {
        try
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }

            var json = JsonSerializer.Serialize(data, JsonOptions);
            await File.WriteAllTextAsync(DataFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving data: {ex.Message}");
        }
    }

    /// <summary>
    /// Cleans up old usage records (older than 7 days).
    /// </summary>
    public void CleanupOldRecords(AppData data)
    {
        var cutoffDate = DateTime.Today.AddDays(-7);
        data.UsageRecords.RemoveAll(r => r.Date < cutoffDate);
    }
}
