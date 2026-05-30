using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DesktopGrass.Win2D;

[SupportedOSPlatform("windows")]
public static class AutoStart
{
    public const string RegistryValueName = "DesktopGrass.Win2D";

    private const string DefaultRunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string? s_registryKeyOverride;

    private static string RegistrySubKey => s_registryKeyOverride ?? DefaultRunSubKey;

    public static string CurrentExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Environment.ProcessPath is unavailable.");

    public static bool IsEnabled
    {
        get
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistrySubKey, writable: false);
            return key?.GetValue(RegistryValueName) is not null;
        }
    }

    public static void SetEnabled(bool on)
    {
        if (on)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistrySubKey, writable: true)
                ?? throw new InvalidOperationException($"Unable to create HKCU\\{RegistrySubKey}.");
            key.SetValue(RegistryValueName, CurrentExePath, RegistryValueKind.String);
            return;
        }

        using RegistryKey? existing = Registry.CurrentUser.OpenSubKey(RegistrySubKey, writable: true);
        existing?.DeleteValue(RegistryValueName, throwOnMissingValue: false);
    }

    public static void ReconcileWithState(bool autoStart)
    {
        if (IsEnabled != autoStart)
        {
            SetEnabled(autoStart);
        }
    }

    public static void SetRegistryKeyOverride(string? subkey)
    {
        s_registryKeyOverride = string.IsNullOrWhiteSpace(subkey) ? null : subkey;
    }
}
