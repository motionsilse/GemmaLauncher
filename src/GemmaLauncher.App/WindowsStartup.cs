using Microsoft.Win32;

namespace GemmaLauncher.App;

public sealed class WindowsStartup(string executablePath, string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
{
    public const string TrayArgument = "--start-in-tray";
    private const string EntryName = "Cat Loader";
    private string CommandLine => $"\"{Path.GetFullPath(executablePath)}\" {TrayArgument}";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(EntryName) is string { Length: > 0 };
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue(EntryName, CommandLine, RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(EntryName, throwOnMissingValue: false);
        }
    }

    public void RefreshExecutable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        if (key?.GetValue(EntryName) is string command && !string.Equals(command, CommandLine, StringComparison.OrdinalIgnoreCase))
            SetEnabled(true);
    }
}
