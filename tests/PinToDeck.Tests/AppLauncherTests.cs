using System;
using Xunit;
using PinToDeck.Core;

namespace PinToDeck.Tests
{
    public class AppLauncherTests
    {
        [Fact]
        public void LaunchWin32App_WithValidPath_ShouldSucceed()
        {
            // Arrange
            var launcher = AppLauncher.Instance;
            var notepadPath = "notepad.exe"; // System app, should always exist

            // Act & Assert
            // 注意：實際啟動會開啟程式，這是整合測試
            // 在 CI 環境中可能需要 mock
            var exception = Record.Exception(() => launcher.LaunchWin32App(notepadPath));

            // Should not throw
            Assert.Null(exception);
        }

        [Fact]
        public void LaunchWin32App_WithEmptyPath_ShouldThrow()
        {
            // Arrange
            var launcher = AppLauncher.Instance;

            // Act & Assert
            Assert.Throws<ArgumentException>(() => launcher.LaunchWin32App(""));
        }

        [Fact]
        public void LaunchWin32App_WithArguments_ShouldSucceed()
        {
            // Arrange
            var launcher = AppLauncher.Instance;
            var notepadPath = "notepad.exe";
            var args = "test.txt";

            // Act & Assert
            var exception = Record.Exception(() => launcher.LaunchWin32App(notepadPath, args));

            // Should not throw
            Assert.Null(exception);
        }

        [Fact]
        public void LaunchUwpApp_WithValidAumid_ShouldSucceed()
        {
            // Arrange
            var launcher = AppLauncher.Instance;
            var calculatorAumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

            // Act & Assert
            var exception = Record.Exception(() => launcher.LaunchUwpApp(calculatorAumid));

            // Should not throw (Calculator should be installed on Windows 10/11)
            Assert.Null(exception);
        }

        [Fact]
        public void LaunchUwpApp_WithEmptyAumid_ShouldThrow()
        {
            // Arrange
            var launcher = AppLauncher.Instance;

            // Act & Assert
            Assert.Throws<ArgumentException>(() => launcher.LaunchUwpApp(""));
        }

        [Fact]
        public void LaunchApp_AutoDetectsWin32()
        {
            // Arrange
            var launcher = AppLauncher.Instance;
            var notepadPath = "notepad.exe";

            // Act & Assert
            var exception = Record.Exception(() => launcher.LaunchApp(notepadPath));

            // Should not throw
            Assert.Null(exception);
        }

        [Fact]
        public void LaunchApp_AutoDetectsUwp()
        {
            // Arrange
            var launcher = AppLauncher.Instance;
            var calculatorAumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

            // Act & Assert
            var exception = Record.Exception(() => launcher.LaunchApp(calculatorAumid));

            // Should not throw
            Assert.Null(exception);
        }

        [Fact]
        public void LaunchApp_WithEmptyPath_ShouldThrow()
        {
            // Arrange
            var launcher = AppLauncher.Instance;

            // Act & Assert
            Assert.Throws<ArgumentException>(() => launcher.LaunchApp(""));
        }
    }
}
