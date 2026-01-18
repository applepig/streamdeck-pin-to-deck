using PinToDeck.Models;

namespace PinToDeck.Tests;

/// <summary>
/// Unit tests for PinToDeck plugin.
/// 
/// Note: Some tests need Windows runtime and may not work in all environments.
/// For pure logic tests (like model validation), they should run anywhere.
/// </summary>
public class AppInfoTests
{
    [Fact]
    public void AppInfo_DefaultValues_ShouldBeEmpty()
    {
        // Arrange & Act
        var appInfo = new AppInfo();

        // Assert
        Assert.Equal(string.Empty, appInfo.DisplayName);
        Assert.Null(appInfo.AppUserModelId);
        Assert.Null(appInfo.ExecutablePath);
        Assert.Null(appInfo.IconPath);
    }

    [Fact]
    public void AppInfo_CanSetDisplayName()
    {
        // Arrange
        var appInfo = new AppInfo();
        var expected_name = "Test Application";

        // Act
        appInfo.DisplayName = expected_name;

        // Assert
        Assert.Equal(expected_name, appInfo.DisplayName);
    }

    [Theory]
    [InlineData("C:\\Program Files\\App.exe", null)]
    [InlineData(null, "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App")]
    public void AppInfo_CanSetPathOrAumid(string? exePath, string? aumid)
    {
        // Arrange
        var appInfo = new AppInfo
        {
            ExecutablePath = exePath,
            AppUserModelId = aumid
        };

        // Assert
        Assert.Equal(exePath, appInfo.ExecutablePath);
        Assert.Equal(aumid, appInfo.AppUserModelId);
    }

    [Fact]
    public void AppInfo_IsDesktopApp_WhenHasExecutablePath()
    {
        // Arrange
        var appInfo = new AppInfo
        {
            DisplayName = "Notepad",
            ExecutablePath = @"C:\Windows\System32\notepad.exe"
        };

        // Act - check if it's a desktop app (has exe path, no AUMID)
        bool is_desktop_app = !string.IsNullOrEmpty(appInfo.ExecutablePath);

        // Assert
        Assert.True(is_desktop_app);
        Assert.Null(appInfo.AppUserModelId);
    }

    [Fact]
    public void AppInfo_IsUwpApp_WhenHasAppUserModelId()
    {
        // Arrange
        var appInfo = new AppInfo
        {
            DisplayName = "Calculator",
            AppUserModelId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
        };

        // Act - check if it's a UWP app (has AUMID)
        bool is_uwp_app = !string.IsNullOrEmpty(appInfo.AppUserModelId);

        // Assert
        Assert.True(is_uwp_app);
        Assert.Null(appInfo.ExecutablePath);
    }
}
