# App Limit Enforcer

Free & Open-source, modern Windows .NET application that helps you limit daily usage time for specific applications.

## Features

- **Add Applications**: Add .exe files or process names to monitor
- **Time Limits**: Set daily time limits (e.g., 2 hours) for each application
- **Warning Notifications**: Get warned before time runs out (configurable, e.g., 5 minutes before)
- **Automatic Enforcement**: Applications are automatically closed when time limit is reached
- **Kill Fallback**: If an app cannot be killed, a popup is shown asking you to close it manually
- **Daily Reset**: Usage counters reset automatically each day
- **System Tray**: Runs minimized in system tray
- **Startup Option**: Optionally start with Windows

## Requirements

- Windows 10 or later
- .NET 8.0 Runtime

## Building

```bash
dotnet build
```

## Running

```bash
dotnet run --project AppLimitEnforcer
```

Or run the compiled executable from `bin/Debug/net8.0-windows/AppLimitEnforcer.exe`.

## Usage

1. Launch the application
2. Enter a process name (e.g., `notepad`) or browse for an .exe file
3. Set the daily time limit (hours and minutes)
4. Set how many minutes before the limit you want to be warned
5. Click "Add" to start monitoring

The application will:
- Track how long each monitored app is running
- Show a warning when approaching the time limit
- Close the app when the time limit is reached
- Keep the app closed if you try to reopen it (until the next day)

## Data Storage

Application data is stored in:
`%LOCALAPPDATA%\AppLimitEnforcer\appdata.json`

## Code

Code was semi-written with the use of various AI LLM models.

## License

MIT License
