# How BackITUp Is Coded

BackITUp is a small Windows desktop backup utility written in C# with .NET 8 and Windows Forms. The app is intentionally simple: it has one main form, one backup service, one settings class, and a small icon-generation helper under `Tools`.

The main application code lives in these files:

- `Program.cs`: starts the WinForms application.
- `MainForm.cs`: builds the UI, owns the scheduler, owns the task tray icon, and coordinates backup runs.
- `BackupService.cs`: performs the actual folder copy work.
- `AppSettings.cs`: loads and saves user settings as JSON.
- `BackITUp.csproj`: configures the project as a Windows Forms executable and assigns the application icon.
- `Assets/AppIcon.ico`: the icon used by the window, taskbar, executable, and tray icon.
- `Tools/IconBuilder/Program.cs`: helper program used to generate the `.ico` file.

## Project Setup

The project is configured in `BackITUp.csproj`.

```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net8.0-windows</TargetFramework>
<Nullable>enable</Nullable>
<UseWindowsForms>true</UseWindowsForms>
<ImplicitUsings>enable</ImplicitUsings>
<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>
```

The important parts are:

- `WinExe` builds a Windows GUI program instead of a console app.
- `net8.0-windows` targets .NET 8 with Windows-specific APIs available.
- `UseWindowsForms` enables WinForms controls such as `Form`, `Button`, `NotifyIcon`, `ContextMenuStrip`, `FolderBrowserDialog`, and `Timer`.
- `ApplicationIcon` embeds `Assets\AppIcon.ico` into the executable. That embedded icon is later extracted at runtime and reused for the form and tray icon.

The project also excludes helper tool source files from the main app build:

```xml
<Compile Remove="Tools\**\*.cs" />
```

That keeps `Tools/IconBuilder` separate from the actual BackITUp app.

## Application Startup

The app starts in `Program.cs`.

```csharp
[STAThread]
static void Main()
{
    ApplicationConfiguration.Initialize();
    Application.Run(new MainForm());
}
```

`[STAThread]` is required for many Windows UI features and dialogs. `ApplicationConfiguration.Initialize()` applies the default WinForms setup, and `Application.Run(new MainForm())` starts the message loop with `MainForm` as the main window.

That message loop is important for the tray icon too. `NotifyIcon` depends on the WinForms message loop staying alive, even when the main window is hidden.

## Main Form Responsibilities

`MainForm` is the center of the app. It owns:

- the source folder list
- the destination path text box
- the interval input
- the start, run, cancel, and minimize buttons
- the activity log
- the backup scheduler timer
- the system tray icon and tray menu
- the current backup cancellation token
- the running/stopped state

At the top of `MainForm.cs`, the important fields are declared:

```csharp
private readonly BackupService backupService = new();
private readonly AppSettings settings;
private readonly System.Windows.Forms.Timer scheduleTimer = new();
private readonly NotifyIcon trayIcon = new();
private readonly ContextMenuStrip trayMenu = new();
private readonly ToolStripMenuItem trayStartStopItem = new("Start Backups");
private readonly Icon appIcon;

private CancellationTokenSource? backupCancellation;
private bool allowExit;
private bool isBackupRunning;
private bool isSchedulerStarted;
```

The form keeps these objects alive for the lifetime of the app. That matters especially for `NotifyIcon`: if the `NotifyIcon` object is not retained and disposed correctly, the tray icon can disappear early or remain as a stale icon after exit.

## Constructor Flow

The `MainForm` constructor does four setup steps:

```csharp
settings = AppSettings.Load();
appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();

InitializeComponent();
LoadSettingsIntoUi();
UpdateUiState();
Log("Ready. Add source folders, choose a destination/share, then start backups.");
```

The flow is:

1. Load saved settings from `%APPDATA%\BackITUp\settings.json`.
2. Extract the embedded executable icon.
3. Build the WinForms UI.
4. Populate the UI from saved settings.
5. Sync button labels, tray tooltip text, and enabled/disabled state.
6. Write an initial log message.

The embedded icon is loaded once into `appIcon` and reused by both the main form and the tray icon.

## UI Layout

The UI is built directly in C# inside `InitializeComponent()`. There is no designer file. This keeps the project compact and makes all layout code visible in one place.

The root layout is a `TableLayoutPanel` with four rows:

- header and status
- source/destination settings
- command buttons
- activity log

The source area uses a `ListBox` for selected folders and buttons for adding/removing folders. The destination area uses a `TextBox`, a browse button, and a `NumericUpDown` for the repeat interval.

The key user actions are wired through event handlers:

```csharp
addSourceButton.Click += AddSourceButton_Click;
removeSourceButton.Click += RemoveSourceButton_Click;
browseDestinationButton.Click += BrowseDestinationButton_Click;
startStopButton.Click += StartStopButton_Click;
runNowButton.Click += RunNowButton_Click;
cancelButton.Click += CancelButton_Click;
minimizeButton.Click += (_, _) => MinimizeToTray();
```

Because the controls are fields, other methods can enable/disable them and read their values without passing controls around.

## Settings Persistence

Settings are managed by `AppSettings.cs`.

The settings model contains:

```csharp
public List<string> SourceFolders { get; set; } = [];
public string DestinationFolder { get; set; } = string.Empty;
public int IntervalMinutes { get; set; } = 60;
```

The settings file path is:

```text
%APPDATA%\BackITUp\settings.json
```

`AppSettings.SettingsPath` creates the `%APPDATA%\BackITUp` folder if needed and returns the full JSON file path.

`Load()` tries to read and deserialize the JSON file. If the file does not exist, is corrupt, or cannot be read, the app falls back to default settings instead of crashing.

`Save()` normalizes values before writing:

- removes blank source paths
- trims whitespace
- removes duplicate source folders case-insensitively
- trims the destination folder
- clamps the interval between 1 and 1440 minutes

`MainForm.SaveSettingsFromUi()` copies the current UI state into the settings object, then calls `settings.Save()`.

Settings are saved after important changes, before backup runs, when the form is closing, and in `OnClosing()`.

## Backup Scheduling

Scheduling is handled with a WinForms timer:

```csharp
private readonly System.Windows.Forms.Timer scheduleTimer = new();
```

The timer is wired in `InitializeComponent()`:

```csharp
scheduleTimer.Tick += async (_, _) => await RunBackupAsync("Scheduled backup started.");
```

When the user starts backups, `StartScheduler()` validates the settings, saves them, flips the scheduler state, sets the timer interval, and starts the timer:

```csharp
isSchedulerStarted = true;
scheduleTimer.Interval = (int)intervalInput.Value * 60 * 1000;
scheduleTimer.Start();
```

Then it immediately runs an initial backup:

```csharp
await RunBackupAsync("Initial backup started.");
```

That means the user does not have to wait for the first interval to pass. The interval controls later automatic runs only.

Stopping the scheduler sets `isSchedulerStarted` to false and calls `scheduleTimer.Stop()`.

## Backup Run Flow

All manual, scheduled, and tray-triggered backup runs go through the same method:

```csharp
private async Task RunBackupAsync(string message)
```

That method does the coordination work around `BackupService`:

1. If a backup is already running, log a message and return.
2. Validate that at least one source and a destination are configured.
3. Save the latest UI settings.
4. Mark the app as running.
5. Create a `CancellationTokenSource`.
6. Create a `Progress<string>` object that writes service messages to the log.
7. Call `backupService.RunAsync(...)`.
8. Catch cancellation, backup failures, and general exceptions.
9. Dispose the cancellation token and update the UI state.

The `isBackupRunning` flag prevents overlapping runs. This is important because a scheduled timer tick could happen while a manual backup is already running.

The cancel button calls:

```csharp
backupCancellation?.Cancel();
```

The backup service checks that token while walking directories and copying files.

## Backup Copy Logic

`BackupService.RunAsync()` performs the actual filesystem work on a background task:

```csharp
await Task.Run(() =>
{
    Directory.CreateDirectory(destinationFolder);

    foreach (var sourceFolder in sourceFolders)
    {
        var targetFolder = Path.Combine(destinationFolder, BuildTargetFolderName(sourceFolder));
        CopyDirectory(sourceFolder, targetFolder, progress, cancellationToken);
    }
}, cancellationToken);
```

The service creates the destination folder if needed, then processes each source folder.

Each source gets its own destination subfolder. `BuildTargetFolderName()` turns a full path into a safe folder name by replacing invalid filename characters and path separators with underscores. For example, a source path like:

```text
C:\Users\Kevin\Documents
```

becomes a destination folder name similar to:

```text
C__Users_Kevin_Documents
```

That avoids collisions between source folders with the same leaf name in different locations.

`CopyDirectory()` does two passes:

1. Create matching directories under the target.
2. Copy files that are missing or out of date.

The enumeration options are:

```csharp
var options = new EnumerationOptions
{
    IgnoreInaccessible = true,
    RecurseSubdirectories = true,
    ReturnSpecialDirectories = false
};
```

That recursively walks subdirectories, skips inaccessible directories where Windows allows it, and avoids special `.` or `..` entries.

`NeedsCopy()` decides whether to copy a file:

```csharp
return sourceInfo.Length != targetInfo.Length ||
    sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc.AddSeconds(1);
```

A file is copied if the target does not exist, the file size changed, or the source is newer than the target. After copying, the target file's last-write timestamp is set to match the source.

The service reports progress after every 25 copied files and at the end of each source folder.

## Protection Against Recursive Backups

The backup service includes a guard against copying the backup destination into itself.

`IsSameOrChildPath()` compares full normalized paths and returns true when one path is the same as, or below, another path.

That check is used while creating directories and copying files:

```csharp
if (IsSameOrChildPath(sourceFile, targetRoot))
{
    skipped++;
    continue;
}
```

This matters if the destination folder is accidentally located inside a selected source folder. Without this check, the app could keep discovering its own backup output and copying it again.

## Task Tray Functionality

The tray behavior is implemented with WinForms' `NotifyIcon` and `ContextMenuStrip`.

The form owns these fields:

```csharp
private readonly NotifyIcon trayIcon = new();
private readonly ContextMenuStrip trayMenu = new();
private readonly ToolStripMenuItem trayStartStopItem = new("Start Backups");
private bool allowExit;
```

`NotifyIcon` is the Windows system tray icon. `ContextMenuStrip` is the right-click menu attached to the icon. `trayStartStopItem` is kept as a field because its text needs to change between `Start Backups` and `Stop Backups` as the scheduler state changes.

### Tray Icon Setup

`InitializeComponent()` calls:

```csharp
ConfigureTrayIcon();
```

`ConfigureTrayIcon()` creates the tray menu items:

```csharp
var showItem = new ToolStripMenuItem("Show", null, (_, _) => RestoreFromTray());
var runItem = new ToolStripMenuItem("Run Backup Now", null, async (_, _) => await RunBackupAsync("Manual backup started from tray."));
trayStartStopItem.Click += (_, _) => ToggleScheduler();
var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitFromTray());
```

The tray menu contains:

- `Show`: restores the hidden window.
- `Run Backup Now`: starts a manual backup without restoring the window.
- `Start Backups` / `Stop Backups`: toggles the scheduler.
- `Exit`: fully closes the app.

Those items are added to `trayMenu`, and then the menu is attached to the tray icon:

```csharp
trayIcon.Icon = appIcon;
trayIcon.Text = "BackITUp";
trayIcon.ContextMenuStrip = trayMenu;
trayIcon.Visible = true;
trayIcon.DoubleClick += (_, _) => RestoreFromTray();
```

The tray icon is visible as soon as the form is initialized. Double-clicking the tray icon restores the window.

### Using the Same Icon Everywhere

The custom icon is embedded through the project file:

```xml
<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>
```

At runtime, the form extracts the associated executable icon:

```csharp
appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();
```

That icon is assigned to the form:

```csharp
Icon = appIcon;
```

And to the tray icon:

```csharp
trayIcon.Icon = appIcon;
```

This is why the same custom icon appears in the window title bar, taskbar, executable, and system tray.

### Minimize To Tray

There are two ways the app minimizes to the tray:

1. The user clicks the `Minimize to Tray` button.
2. The user minimizes the window normally.

The button calls:

```csharp
minimizeButton.Click += (_, _) => MinimizeToTray();
```

The normal window minimize path is handled by the form resize event:

```csharp
Resize += MainForm_Resize;
```

`MainForm_Resize()` checks for the minimized state:

```csharp
if (WindowState == FormWindowState.Minimized)
{
    MinimizeToTray();
}
```

`MinimizeToTray()` hides the form and removes it from the taskbar:

```csharp
private void MinimizeToTray()
{
    Hide();
    ShowInTaskbar = false;
    WindowState = FormWindowState.Minimized;
}
```

The app itself is still running because the WinForms message loop is still active. Only the window is hidden.

### Restore From Tray

The tray menu's `Show` item and the tray icon double-click both call:

```csharp
RestoreFromTray();
```

That method reverses the minimize operation:

```csharp
private void RestoreFromTray()
{
    Show();
    ShowInTaskbar = true;
    WindowState = FormWindowState.Normal;
    Activate();
}
```

The window becomes visible, returns to the taskbar, is set back to normal size, and is activated.

### Close Button Behavior

Clicking the window close button does not immediately exit the program. The form intercepts `FormClosing`:

```csharp
FormClosing += MainForm_FormClosing;
```

`MainForm_FormClosing()` first saves settings, then checks `allowExit`:

```csharp
if (allowExit)
{
    return;
}
```

If `allowExit` is false, the close is canceled:

```csharp
e.Cancel = true;
MinimizeToTray();
ShowNotification("BackITUp is still running", "Use the tray icon menu and choose Exit to close it.");
```

This is the core of the tray app behavior. The window's close button behaves like "hide to tray" so scheduled backups can continue in the background.

### Fully Exiting From The Tray

The tray menu's `Exit` item calls `ExitFromTray()`.

```csharp
private void ExitFromTray()
{
    allowExit = true;
    scheduleTimer.Stop();
    backupCancellation?.Cancel();
    trayIcon.Visible = false;
    Close();
}
```

This method:

1. Sets `allowExit = true`, which lets `MainForm_FormClosing()` allow the close.
2. Stops the scheduler timer.
3. Cancels any active backup.
4. Hides the tray icon before closing.
5. Calls `Close()`.

The `allowExit` flag is the difference between "close button means hide" and "tray Exit means really quit."

### Tray Tooltip Text

The tray tooltip is updated in `UpdateUiState()`:

```csharp
trayIcon.Text = statusLabel.Text.Length > 63 ? statusLabel.Text[..60] + "..." : statusLabel.Text;
```

Windows tray tooltip text has a short length limit, so the code truncates long status text to stay within that limit.

The status text includes:

- whether a backup is running
- whether the scheduler is running in the background
- source count
- destination path

The same method also updates `trayStartStopItem.Text`, so the tray menu stays in sync with the main Start/Stop button.

### Tray Notifications

Notifications use `NotifyIcon` balloon tips:

```csharp
private void ShowNotification(string title, string message)
{
    trayIcon.BalloonTipTitle = title;
    trayIcon.BalloonTipText = message;
    trayIcon.ShowBalloonTip(2500);
}
```

The app shows a notification when:

- the user closes the window and the app hides to the tray
- a backup fails

## Cleanup And Disposal

`MainForm.Dispose()` disposes the long-lived WinForms resources:

```csharp
scheduleTimer.Dispose();
trayIcon.Dispose();
trayMenu.Dispose();
appIcon.Dispose();
backupCancellation?.Dispose();
```

This is especially important for tray apps. If the tray icon is not hidden and disposed during exit, Windows can temporarily show a stale icon until the user hovers over the tray area.

`ExitFromTray()` also sets:

```csharp
trayIcon.Visible = false;
```

That removes the icon immediately before the form closes.

## Icon Generation

The icon was generated with the helper project under `Tools/IconBuilder`.

`Tools/IconBuilder/Program.cs` renders multiple PNG sizes into one `.ico` file:

```csharp
var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
```

It uses `System.Drawing` to draw a rounded blue background, a backup box, and an arrow. Then it writes a Windows ICO file by hand:

1. Write the ICO header.
2. Render each size as PNG bytes.
3. Write one directory entry per image size.
4. Append each PNG image.

The resulting `AppIcon.ico` is stored in `Assets` and referenced from the main project file.

## End-To-End User Flow

Here is the full flow when someone uses the app:

1. The app starts in `Program.Main()`.
2. `MainForm` loads saved settings and builds the UI.
3. `ConfigureTrayIcon()` creates the tray icon and right-click menu.
4. The user adds source folders and chooses a destination.
5. The app saves those settings to `%APPDATA%\BackITUp\settings.json`.
6. The user clicks `Start Backups`.
7. `StartScheduler()` starts the timer and immediately runs the first backup.
8. `RunBackupAsync()` delegates file copying to `BackupService`.
9. Progress messages are written to the activity log.
10. If the user minimizes or closes the window, the form hides but the app keeps running in the tray.
11. Scheduled backups continue while the app remains open in the tray.
12. The user can restore, run a backup, stop scheduling, or exit from the tray menu.

## Why The Tray Setup Works

The tray functionality works because the application separates the lifetime of the process from the visibility of the window.

The WinForms application keeps running as long as the message loop started by `Application.Run(new MainForm())` is alive. Hiding the form with `Hide()` does not stop that message loop. The `NotifyIcon` stays alive because it is stored as a field on `MainForm`. The scheduler keeps working because the form still exists and the timer is still active.

The close button is converted into "hide to tray" by canceling the `FormClosing` event. The only path that allows the form to actually close is the tray menu's `Exit` command, which sets `allowExit = true` before calling `Close()`.

That is the key pattern:

```csharp
// Normal close:
e.Cancel = true;
MinimizeToTray();

// Real exit:
allowExit = true;
Close();
```

This gives BackITUp the expected background utility behavior: the window can disappear, the tray icon remains available, scheduled work continues, and the user still has an explicit way to fully quit.
