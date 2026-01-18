using System;
using System.Diagnostics;

namespace PinToDeck.Core
{
    /// <summary>
    /// 負責啟動 Win32 和 UWP 應用程式
    /// </summary>
    public class AppLauncher
    {
        private static readonly Lazy<AppLauncher> _instance = new Lazy<AppLauncher>(() => new AppLauncher());
        public static AppLauncher Instance => _instance.Value;

        private AppLauncher() { }

        /// <summary>
        /// 啟動應用程式 (自動判斷 Win32 或 UWP)
        /// </summary>
        /// <param name="appPath">應用程式路徑或 AUMID</param>
        /// <param name="args">啟動參數 (可選)</param>
        public void LaunchApp(string appPath, string args = "")
        {
            if (string.IsNullOrWhiteSpace(appPath))
            {
                throw new ArgumentException("App path cannot be empty", nameof(appPath));
            }

            // 判斷是 UWP (AUMID 包含 "!") 或 Win32
            if (appPath.Contains("!"))
            {
                LaunchUwpApp(appPath);
            }
            else
            {
                LaunchWin32App(appPath, args);
            }
        }

        /// <summary>
        /// 啟動 Win32 應用程式
        /// </summary>
        /// <param name="exePath">執行檔路徑</param>
        /// <param name="args">啟動參數</param>
        public void LaunchWin32App(string exePath, string args = "")
        {
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new ArgumentException("Executable path cannot be empty", nameof(exePath));
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args ?? string.Empty,
                    UseShellExecute = true,
                    Verb = "open" // Explicitly use 'open' verb, helpful for Explorer
                };

                // Special handling for Explorer: usually requires ShellExecute=true (already set)
                // but sometimes explicit explorer.exe execution works better if paths are involved
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to launch Win32 app: {exePath}", ex);
            }
        }

        /// <summary>
        /// 啟動 UWP 應用程式
        /// </summary>
        /// <param name="aumid">Application User Model ID</param>
        public void LaunchUwpApp(string aumid)
        {
            if (string.IsNullOrWhiteSpace(aumid))
            {
                throw new ArgumentException("AUMID cannot be empty", nameof(aumid));
            }

            try
            {
                // 使用 shell:AppsFolder 啟動 UWP App
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"shell:AppsFolder\\{aumid}",
                    UseShellExecute = true
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to launch UWP app: {aumid}", ex);
            }
        }
    }
}
