using System.ComponentModel;
using System.Drawing;

namespace BackITUp;

public sealed class MainForm : Form
{
    private readonly BackupService backupService = new();
    private readonly AppSettings settings;
    private readonly System.Windows.Forms.Timer scheduleTimer = new();
    private readonly NotifyIcon trayIcon = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly ListBox sourceList = new();
    private readonly TextBox destinationTextBox = new();
    private readonly NumericUpDown intervalInput = new();
    private readonly Button startStopButton = new();
    private readonly Button runNowButton = new();
    private readonly Button cancelButton = new();
    private readonly Button removeSourceButton = new();
    private readonly TextBox logBox = new();
    private readonly Label statusLabel = new();
    private readonly ToolStripMenuItem trayStartStopItem = new("Start Backups");
    private readonly Icon appIcon;

    private CancellationTokenSource? backupCancellation;
    private bool allowExit;
    private bool isBackupRunning;
    private bool isSchedulerStarted;

    public MainForm()
    {
        settings = AppSettings.Load();
        appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? (Icon)SystemIcons.Application.Clone();

        InitializeComponent();
        LoadSettingsIntoUi();
        UpdateUiState();
        Log("Ready. Add source folders, choose a destination/share, then start backups.");
    }

    private void InitializeComponent()
    {
        Text = "BackITUp";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(860, 660);
        Size = new Size(980, 740);
        Icon = appIcon;

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18),
            BackColor = Color.FromArgb(248, 249, 251)
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 246));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var titleLabel = new Label
        {
            Text = "BackITUp",
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 20, FontStyle.Bold),
            ForeColor = Color.FromArgb(26, 31, 40),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        statusLabel.Dock = DockStyle.Fill;
        statusLabel.ForeColor = Color.FromArgb(86, 96, 112);
        statusLabel.Text = "Stopped";
        statusLabel.TextAlign = ContentAlignment.TopLeft;
        statusLabel.AutoEllipsis = true;

        header.Controls.Add(titleLabel, 0, 0);
        header.Controls.Add(statusLabel, 0, 1);

        var settingsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        settingsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        var sourcesGroup = new GroupBox
        {
            Text = "Source folders",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };

        var sourceLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        sourceLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        sourceLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        sourceList.Dock = DockStyle.Fill;
        sourceList.HorizontalScrollbar = true;
        sourceList.SelectedIndexChanged += (_, _) => UpdateUiState();

        var sourceButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        var addSourceButton = new Button
        {
            Text = "Add Folder",
            AutoSize = true,
            Height = 32
        };
        addSourceButton.Click += AddSourceButton_Click;

        removeSourceButton.Text = "Remove";
        removeSourceButton.AutoSize = true;
        removeSourceButton.Height = 32;
        removeSourceButton.Click += RemoveSourceButton_Click;

        sourceButtons.Controls.Add(addSourceButton);
        sourceButtons.Controls.Add(removeSourceButton);
        sourceLayout.Controls.Add(sourceList, 0, 0);
        sourceLayout.Controls.Add(sourceButtons, 0, 1);
        sourcesGroup.Controls.Add(sourceLayout);

        var destinationGroup = new GroupBox
        {
            Text = "Destination",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };

        var destinationLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        destinationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        destinationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        destinationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        destinationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        destinationLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var destinationLabel = new Label
        {
            Text = "Folder path or network share",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(62, 72, 86)
        };

        destinationTextBox.Dock = DockStyle.Fill;
        destinationTextBox.PlaceholderText = @"Example: \\SERVER\Share\Backups";

        var destinationButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        var browseDestinationButton = new Button
        {
            Text = "Browse",
            AutoSize = true,
            Height = 32
        };
        browseDestinationButton.Click += BrowseDestinationButton_Click;

        destinationButtons.Controls.Add(browseDestinationButton);

        var intervalLabel = new Label
        {
            Text = "Repeat while started, every minutes",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(62, 72, 86)
        };

        intervalInput.Minimum = 1;
        intervalInput.Maximum = 1440;
        intervalInput.Value = 60;
        intervalInput.Dock = DockStyle.Left;
        intervalInput.Width = 96;
        intervalInput.Height = 30;

        destinationLayout.Controls.Add(destinationLabel, 0, 0);
        destinationLayout.Controls.Add(destinationTextBox, 0, 1);
        destinationLayout.Controls.Add(destinationButtons, 0, 2);
        destinationLayout.Controls.Add(intervalLabel, 0, 3);
        destinationLayout.Controls.Add(intervalInput, 0, 4);
        destinationGroup.Controls.Add(destinationLayout);

        settingsPanel.Controls.Add(sourcesGroup, 0, 0);
        settingsPanel.Controls.Add(destinationGroup, 1, 0);

        var controlsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 14, 0, 0)
        };

        startStopButton.Text = "Start Backups";
        startStopButton.AutoSize = true;
        startStopButton.Height = 36;
        startStopButton.Click += StartStopButton_Click;

        runNowButton.Text = "Run Now";
        runNowButton.AutoSize = true;
        runNowButton.Height = 36;
        runNowButton.Click += RunNowButton_Click;

        cancelButton.Text = "Cancel Current Run";
        cancelButton.AutoSize = true;
        cancelButton.Height = 36;
        cancelButton.Click += CancelButton_Click;

        var minimizeButton = new Button
        {
            Text = "Minimize to Tray",
            AutoSize = true,
            Height = 36
        };
        minimizeButton.Click += (_, _) => MinimizeToTray();

        controlsPanel.Controls.Add(startStopButton);
        controlsPanel.Controls.Add(runNowButton);
        controlsPanel.Controls.Add(cancelButton);
        controlsPanel.Controls.Add(minimizeButton);

        var logGroup = new GroupBox
        {
            Text = "Activity",
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };

        logBox.Dock = DockStyle.Fill;
        logBox.Multiline = true;
        logBox.ReadOnly = true;
        logBox.ScrollBars = ScrollBars.Vertical;
        logBox.BackColor = Color.White;
        logBox.Font = new Font("Consolas", 10);
        logGroup.Controls.Add(logBox);

        main.Controls.Add(header, 0, 0);
        main.Controls.Add(settingsPanel, 0, 1);
        main.Controls.Add(controlsPanel, 0, 2);
        main.Controls.Add(logGroup, 0, 3);
        Controls.Add(main);

        ConfigureTrayIcon();

        scheduleTimer.Tick += async (_, _) => await RunBackupAsync("Scheduled backup started.");
        FormClosing += MainForm_FormClosing;
        Resize += MainForm_Resize;
    }

    private void ConfigureTrayIcon()
    {
        var showItem = new ToolStripMenuItem("Show", null, (_, _) => RestoreFromTray());
        var runItem = new ToolStripMenuItem("Run Backup Now", null, async (_, _) => await RunBackupAsync("Manual backup started from tray."));
        trayStartStopItem.Click += (_, _) => ToggleScheduler();
        var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitFromTray());

        trayMenu.Items.Add(showItem);
        trayMenu.Items.Add(runItem);
        trayMenu.Items.Add(trayStartStopItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(exitItem);

        trayIcon.Icon = appIcon;
        trayIcon.Text = "BackITUp";
        trayIcon.ContextMenuStrip = trayMenu;
        trayIcon.Visible = true;
        trayIcon.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void LoadSettingsIntoUi()
    {
        foreach (var source in settings.SourceFolders.Where(Directory.Exists))
        {
            sourceList.Items.Add(source);
        }

        destinationTextBox.Text = settings.DestinationFolder;
        intervalInput.Value = Math.Clamp(settings.IntervalMinutes, 1, 1440);
    }

    private void AddSourceButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a folder to back up",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!sourceList.Items.Cast<string>().Contains(dialog.SelectedPath, StringComparer.OrdinalIgnoreCase))
        {
            sourceList.Items.Add(dialog.SelectedPath);
            SaveSettingsFromUi();
            Log($"Added source: {dialog.SelectedPath}");
        }
    }

    private void RemoveSourceButton_Click(object? sender, EventArgs e)
    {
        if (sourceList.SelectedItem is not string selected)
        {
            return;
        }

        sourceList.Items.Remove(selected);
        SaveSettingsFromUi();
        Log($"Removed source: {selected}");
        UpdateUiState();
    }

    private void BrowseDestinationButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a local folder or mapped file share for backups",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        if (!string.IsNullOrWhiteSpace(destinationTextBox.Text) && Directory.Exists(destinationTextBox.Text))
        {
            dialog.SelectedPath = destinationTextBox.Text;
        }

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            destinationTextBox.Text = dialog.SelectedPath;
            SaveSettingsFromUi();
            Log($"Destination set: {dialog.SelectedPath}");
        }
    }

    private void StartStopButton_Click(object? sender, EventArgs e) => ToggleScheduler();

    private async void RunNowButton_Click(object? sender, EventArgs e)
    {
        await RunBackupAsync("Manual backup started.");
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        backupCancellation?.Cancel();
        Log("Cancel requested.");
    }

    private void ToggleScheduler()
    {
        if (isSchedulerStarted)
        {
            StopScheduler();
        }
        else
        {
            StartScheduler();
        }
    }

    private async void StartScheduler()
    {
        if (!ValidateBackupSettings())
        {
            return;
        }

        SaveSettingsFromUi();
        isSchedulerStarted = true;
        scheduleTimer.Interval = (int)intervalInput.Value * 60 * 1000;
        scheduleTimer.Start();
        Log($"Backups started. Next automatic run interval: every {intervalInput.Value} minutes.");
        UpdateUiState();

        await RunBackupAsync("Initial backup started.");
    }

    private void StopScheduler()
    {
        isSchedulerStarted = false;
        scheduleTimer.Stop();
        Log("Automatic backups stopped.");
        UpdateUiState();
    }

    private async Task RunBackupAsync(string message)
    {
        if (isBackupRunning)
        {
            Log("A backup is already running.");
            return;
        }

        if (!ValidateBackupSettings())
        {
            return;
        }

        SaveSettingsFromUi();
        Log(message);

        isBackupRunning = true;
        backupCancellation = new CancellationTokenSource();
        UpdateUiState();

        var progress = new Progress<string>(Log);
        var sources = sourceList.Items.Cast<string>().ToList();
        var destination = destinationTextBox.Text.Trim();

        try
        {
            await backupService.RunAsync(sources, destination, progress, backupCancellation.Token);
            Log("Backup run complete.");
        }
        catch (OperationCanceledException)
        {
            Log("Backup run canceled.");
        }
        catch (Exception ex)
        {
            Log($"Backup failed: {ex.Message}");
            ShowNotification("Backup failed", ex.Message);
        }
        finally
        {
            backupCancellation.Dispose();
            backupCancellation = null;
            isBackupRunning = false;
            UpdateUiState();
        }
    }

    private bool ValidateBackupSettings()
    {
        if (sourceList.Items.Count == 0)
        {
            MessageBox.Show(this, "Add at least one source folder.", "BackITUp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(destinationTextBox.Text))
        {
            MessageBox.Show(this, "Choose a destination folder or network share.", "BackITUp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private void SaveSettingsFromUi()
    {
        settings.SourceFolders = sourceList.Items.Cast<string>().ToList();
        settings.DestinationFolder = destinationTextBox.Text.Trim();
        settings.IntervalMinutes = (int)intervalInput.Value;
        settings.Save();
    }

    private void UpdateUiState()
    {
        startStopButton.Text = isSchedulerStarted ? "Stop Backups" : "Start Backups";
        trayStartStopItem.Text = isSchedulerStarted ? "Stop Backups" : "Start Backups";
        runNowButton.Enabled = !isBackupRunning;
        cancelButton.Enabled = isBackupRunning;
        removeSourceButton.Enabled = sourceList.SelectedItem is not null || sourceList.Items.Count > 0;

        var runningText = isBackupRunning ? "Backup running" : isSchedulerStarted ? "Running in background" : "Stopped";
        statusLabel.Text = $"{runningText} | {sourceList.Items.Count} source(s) | Destination: {DisplayDestination()}";
        trayIcon.Text = statusLabel.Text.Length > 63 ? statusLabel.Text[..60] + "..." : statusLabel.Text;
    }

    private string DisplayDestination()
    {
        return string.IsNullOrWhiteSpace(destinationTextBox.Text) ? "not selected" : destinationTextBox.Text.Trim();
    }

    private void Log(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(message));
            return;
        }

        logBox.AppendText($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void MainForm_Resize(object? sender, EventArgs e)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            MinimizeToTray();
        }
    }

    private void MainForm_FormClosing(object? sender, FormClosingEventArgs e)
    {
        SaveSettingsFromUi();

        if (allowExit)
        {
            return;
        }

        e.Cancel = true;
        MinimizeToTray();
        ShowNotification("BackITUp is still running", "Use the tray icon menu and choose Exit to close it.");
    }

    private void MinimizeToTray()
    {
        Hide();
        ShowInTaskbar = false;
        WindowState = FormWindowState.Minimized;
    }

    private void RestoreFromTray()
    {
        Show();
        ShowInTaskbar = true;
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        allowExit = true;
        scheduleTimer.Stop();
        backupCancellation?.Cancel();
        trayIcon.Visible = false;
        Close();
    }

    private void ShowNotification(string title, string message)
    {
        trayIcon.BalloonTipTitle = title;
        trayIcon.BalloonTipText = message;
        trayIcon.ShowBalloonTip(2500);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        SaveSettingsFromUi();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            scheduleTimer.Dispose();
            trayIcon.Dispose();
            trayMenu.Dispose();
            appIcon.Dispose();
            backupCancellation?.Dispose();
        }

        base.Dispose(disposing);
    }
}
