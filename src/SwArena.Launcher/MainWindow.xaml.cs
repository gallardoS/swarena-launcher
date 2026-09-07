using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace SwArena.Launcher;

public partial class MainWindow : Window
{
    private const string DefaultManifestUrl = "https://updates.swami.dev/manifest.json";
    private const string DefaultGameExecutable = "Wow.exe";

    private readonly string _launcherVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    private readonly JsonSerializerOptions _stateJsonOptions = new() { WriteIndented = true };
    private LauncherSettings? _settings;
    private string _clientRoot = "";

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Launcher v{_launcherVersion}";
        Loaded += async (_, _) => await CheckForUpdatesAsync();
    }

    private async Task CheckForUpdatesAsync(string? preferredClientRoot = null)
    {
        PlayButton.IsEnabled = false;
        PlayButton.Content = "updating…";
        ChangeFolderButton.IsEnabled = false;
        RetryButton.Visibility = Visibility.Collapsed;
        VersionText.Text = $"Launcher v{_launcherVersion}";
        ServerStatusText.Text = "checking…";
        ServerStatusText.Foreground = BrushFrom("#978D7D");
        StatusDot.Fill = BrushFrom("#98713A");

        try
        {
            _settings = await LoadLauncherSettingsAsync();
            _clientRoot = IsValidClientRoot(preferredClientRoot)
                ? Path.GetFullPath(preferredClientRoot!)
                : await FindClientRootAsync() ?? "";

            if (_clientRoot.Length == 0)
            {
                ShowFolderRequiredState();
                return;
            }

            await SaveClientRootAsync(_clientRoot);
            ShowClientRoot(_clientRoot);

            if (IsGameRunning())
                throw new InvalidOperationException("Close World of Warcraft before checking for or installing updates.");

            var progress = new Progress<UpdateProgress>(p =>
            {
                StatusText.Text = p.Status;
                DetailText.Text = p.Detail;
                UpdateProgress.Value = Math.Clamp(p.Percentage, 0, 100);
            });

            using var updater = new UpdateService(_clientRoot, _settings.ManifestUrl);
            var manifest = await updater.UpdateAsync(progress, CancellationToken.None);
            VersionText.Text = $"Launcher v{_launcherVersion}  •  Client {manifest.Version}";
            ServerStatusText.Text = "online";
            ServerStatusText.Foreground = BrushFrom("#978D7D");
            StatusDot.Fill = BrushFrom("#7EA66D");
            PlayButton.Content = "play";
            PlayButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Update failed";
            DetailText.Text = ex.Message;
            ServerStatusText.Text = "offline";
            ServerStatusText.Foreground = BrushFrom("#978D7D");
            StatusDot.Fill = BrushFrom("#C76B5C");
            RetryButton.Visibility = Visibility.Visible;

            if (_settings?.AllowOfflineLaunch == true && IsValidClientRoot(_clientRoot))
            {
                PlayButton.Content = "play without updating";
                PlayButton.IsEnabled = true;
            }
        }
        finally
        {
            ChangeFolderButton.IsEnabled = true;
        }
    }

    private async Task<LauncherSettings> LoadLauncherSettingsAsync()
    {
        var settingsPath = Path.Combine(AppContext.BaseDirectory, "launcher-settings.json");
        if (!File.Exists(settingsPath))
            return new LauncherSettings(DefaultManifestUrl, DefaultGameExecutable, "", false);

        return JsonSerializer.Deserialize<LauncherSettings>(await File.ReadAllTextAsync(settingsPath))
            ?? throw new InvalidDataException("Could not read launcher-settings.json.");
    }

    private async Task<string?> FindClientRootAsync()
    {
        var besideLauncher = Path.GetFullPath(AppContext.BaseDirectory);
        if (IsValidClientRoot(besideLauncher))
            return besideLauncher;

        var savedClientRoot = await LoadSavedClientRootAsync();
        if (IsValidClientRoot(savedClientRoot))
            return Path.GetFullPath(savedClientRoot!);

        return null;
    }

    private string? SelectClientRoot()
    {
        while (true)
        {
            var dialog = new OpenFolderDialog
            {
                Title = $"Select the folder that contains {_settings?.GameExecutable ?? DefaultGameExecutable}",
                Multiselect = false
            };

            if (Directory.Exists(_clientRoot))
                dialog.InitialDirectory = _clientRoot;

            if (dialog.ShowDialog(this) != true)
                return null;

            var selectedPath = Path.GetFullPath(dialog.FolderName);
            if (IsValidClientRoot(selectedPath))
                return selectedPath;

            MessageBox.Show(
                this,
                $"The selected folder does not contain {_settings?.GameExecutable ?? DefaultGameExecutable}.",
                "Invalid game folder",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private bool IsValidClientRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return Directory.Exists(path) &&
                   File.Exists(Path.Combine(path, _settings?.GameExecutable ?? DefaultGameExecutable));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private async Task<string?> LoadSavedClientRootAsync()
    {
        var statePath = GetStatePath();
        if (!File.Exists(statePath)) return null;

        try
        {
            var state = JsonSerializer.Deserialize<UserLauncherState>(await File.ReadAllTextAsync(statePath));
            return state?.ClientPath;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task SaveClientRootAsync(string clientRoot)
    {
        var statePath = GetStatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        var json = JsonSerializer.Serialize(new UserLauncherState(clientRoot), _stateJsonOptions);
        await File.WriteAllTextAsync(statePath, json);
    }

    private static string GetStatePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "swArena Launcher",
        "settings.json");

    private void ShowFolderRequiredState()
    {
        StatusText.Text = "Game folder required";
        DetailText.Text = $"Select the folder that contains {_settings?.GameExecutable ?? DefaultGameExecutable}.";
        ServerStatusText.Text = "folder required";
        ServerStatusText.Foreground = BrushFrom("#978D7D");
        StatusDot.Fill = BrushFrom("#98713A");
        PlayButton.Content = "select a game folder";
        GamePathText.Text = "no game folder selected";
    }

    private void ShowClientRoot(string clientRoot)
    {
        GamePathText.Text = clientRoot;
        GamePathText.ToolTip = clientRoot;
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || !IsValidClientRoot(_clientRoot)) return;
        var executable = Path.GetFullPath(Path.Combine(_clientRoot, _settings.GameExecutable));
        Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = _settings.LaunchArguments,
            WorkingDirectory = _clientRoot,
            UseShellExecute = true
        });
        Close();
    }

    private bool IsGameRunning()
    {
        var expected = Path.GetFullPath(Path.Combine(_clientRoot, _settings!.GameExecutable));
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(_settings.GameExecutable)))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, expected, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { /* A protected process must not block another client. */ }
            finally { process.Dispose(); }
        }
        return false;
    }

    private async void RetryButton_Click(object sender, RoutedEventArgs e) =>
        await CheckForUpdatesAsync(_clientRoot);

    private async void ChangeFolderButton_Click(object sender, RoutedEventArgs e)
    {
        _settings ??= await LoadLauncherSettingsAsync();
        var selectedClientRoot = SelectClientRoot();
        if (selectedClientRoot is null) return;

        _clientRoot = selectedClientRoot;
        await SaveClientRootAsync(_clientRoot);
        ShowClientRoot(_clientRoot);
        await CheckForUpdatesAsync(_clientRoot);
    }

    private static System.Windows.Media.SolidColorBrush BrushFrom(string color) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
}
