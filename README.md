# BackITUp

BackITUp is a Windows desktop folder backup utility built with C# and .NET 8 Windows Forms. Copy multiple source folders to a local folder, mapped drive, or network share, on demand or at a repeating interval while the app is running.

## Features

- Multiple source folders and one destination, including UNC paths such as `\\SERVER\Share\Backups`.
- Manual backups and recurring runs every 1–1,440 minutes (default: 60).
- Cancellation and a timestamped Activity log.
- System tray controls and saved settings.

## Requirements

Windows and the .NET 8 SDK are required to build or run from source. A framework-dependent build requires the .NET 8 Desktop Runtime; self-contained publishes include the runtime.

The Windows account running the app needs read access to sources and write access to the destination. Network shares use that account's permissions; BackITUp does not store share credentials.

## Get started

From the project directory:

```powershell
dotnet run --project BackITUp.csproj
```

1. Click **Add Folder** for each source.
2. Browse to a destination or enter its local, mapped-drive, or UNC path. Choose a destination outside your source folders.
3. Set the repeat interval.
4. Click **Run Now** for one backup, or **Start Backups** to run immediately and enable recurring backups.

**Stop Backups** stops future scheduled runs. **Cancel Current Run** requests cancellation of an active run. Cancellation is checked between file operations, so an in-progress file copy may finish first. Only one backup runs at a time.

Closing or minimizing the window keeps BackITUp running in the tray. Double-click the tray icon to restore it, or right-click for **Show**, **Run Backup Now**, **Start Backups** / **Stop Backups**, and **Exit**. Choose **Exit** to fully quit.

The scheduler only runs while the app is open. It is not a Windows service, does not install a startup entry, and starts stopped when the app is reopened.

## Backup behavior and restore

Each source gets a destination subfolder derived from its full path, replacing separators and invalid filename characters with underscores:

```text
Source:       C:\Users\Example\Documents
Destination:  D:\Backups
Copied into:  D:\Backups\C__Users_Example_Documents
```

Directory structure is preserved. A file is copied if its destination is missing, its size differs, or its source modification time is more than one second newer than the destination. Copies overwrite existing destination files and preserve the source's UTC modification time.

This is a file-copy backup with no version history, compression, encryption, or content hashing. Same-size changes with unchanged or older timestamps can be skipped. Files deleted from a source remain in the destination. Restore files by copying them back with File Explorer.

Missing sources are skipped. Inaccessible entries may be omitted during enumeration; file copy permission and I/O errors appear as skipped files in Activity. Review Activity after a run: completion can include skipped files. The log is displayed in the current session and is not saved to disk.

## Settings

Settings are saved to `%APPDATA%\BackITUp\settings.json`, containing `SourceFolders`, `DestinationFolder`, and `IntervalMinutes`. Missing source folders are not loaded into the source list at startup. Unreadable or invalid settings fall back to defaults.

## Build

```powershell
dotnet build BackITUp.csproj -c Release
```

Framework-dependent output is under `bin\Release\net8.0-windows`.

## Publish for Windows x64

Self-contained folder:

```powershell
dotnet publish BackITUp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -o .\publish\folder
```

Distribute the entire `publish\folder` directory, including `BackITUp.exe`, app DLLs, and runtime files.

Self-contained single executable:

```powershell
dotnet publish BackITUp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:SatelliteResourceLanguages=en -o .\publish\single-file
```

The executable is `publish\single-file\BackITUp.exe`. Native runtime components may be extracted at launch.

## Project layout

| File or folder | Purpose |
| --- | --- |
| `Program.cs` | Windows Forms entry point |
| `MainForm.cs` | Interface, scheduler, tray controls, and Activity log |
| `BackupService.cs` | Directory traversal and copy decisions |
| `AppSettings.cs` | JSON settings storage |
| `Assets/AppIcon.ico` | App, taskbar, and tray icon |
| `Tools/IconBuilder` | Optional icon generation tool |
| `docs/HOW_IT_WORKS.md` | Detailed code walkthrough |

See [the code walkthrough](docs/HOW_IT_WORKS.md) for implementation details.

To regenerate the icon on Windows:

```powershell
dotnet run --project Tools/IconBuilder/IconBuilder.csproj -- Assets/AppIcon.ico
```
