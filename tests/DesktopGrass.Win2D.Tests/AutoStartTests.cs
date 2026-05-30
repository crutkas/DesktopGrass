using System.Runtime.Versioning;
using DesktopGrass.Win2D;
using Microsoft.Win32;
using Xunit;

namespace DesktopGrass.Win2D.Tests;

[SupportedOSPlatform("windows")]
public sealed class AutoStartTests : IDisposable
{
    private readonly string _subkey = $@"Software\DesktopGrass.Test.{Guid.NewGuid():N}";

    public AutoStartTests()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_subkey, throwOnMissingSubKey: false);
        AutoStart.SetRegistryKeyOverride(_subkey);
    }

    public void Dispose()
    {
        AutoStart.SetRegistryKeyOverride(_subkey);
        AutoStart.SetEnabled(false);
        Registry.CurrentUser.DeleteSubKeyTree(_subkey, throwOnMissingSubKey: false);
        AutoStart.SetRegistryKeyOverride(null);
    }

    [Fact]
    public void IsEnabledReturnsFalseWhenRegistryValueMissing()
    {
        Assert.False(AutoStart.IsEnabled);
    }

    [Fact]
    public void SetEnabledTrueCreatesRegistryValue()
    {
        AutoStart.SetEnabled(true);

        Assert.True(AutoStart.IsEnabled);
    }

    [Fact]
    public void SetEnabledFalseDeletesRegistryValue()
    {
        AutoStart.SetEnabled(true);
        AutoStart.SetEnabled(false);

        Assert.False(AutoStart.IsEnabled);
    }

    [Fact]
    public void RegistryValueContainsCurrentExePath()
    {
        AutoStart.SetEnabled(true);

        Assert.Equal(AutoStart.CurrentExePath, ReadRegistryValue());
    }

    [Fact]
    public void SetEnabledTrueIsIdempotent()
    {
        AutoStart.SetEnabled(true);
        AutoStart.SetEnabled(true);

        Assert.True(AutoStart.IsEnabled);
    }

    [Fact]
    public void SetEnabledFalseOnMissingValueIsNoOp()
    {
        AutoStart.SetEnabled(false);

        Assert.False(AutoStart.IsEnabled);
    }

    [Fact]
    public void PersistedTrueReconcilesRegistryOnStartup()
    {
        var state = new AppState(1, Scene.Grass, CritterKind.None, 0, AutoStart: true, []);

        AutoStart.ReconcileWithState(state.AutoStart);

        Assert.True(AutoStart.IsEnabled);
    }

    [Fact]
    public void PersistedFalseReconcilesRegistryOnStartup()
    {
        var state = new AppState(1, Scene.Grass, CritterKind.None, 0, AutoStart: false, []);
        AutoStart.SetEnabled(true);

        AutoStart.ReconcileWithState(state.AutoStart);

        Assert.False(AutoStart.IsEnabled);
    }

    private string ReadRegistryValue()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_subkey, writable: false);
        Assert.NotNull(key);
        return Assert.IsType<string>(key.GetValue(AutoStart.RegistryValueName));
    }

}
