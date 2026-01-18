using Microsoft.WindowsAPICodePack.Shell;
using PinToDeck.Models;
using System.Runtime.Versioning;
using BarRaider.SdTools;

namespace PinToDeck.Core;

[SupportedOSPlatform("windows")]
public static class AppEnumerator
{
    // GUID for shell:AppsFolder
    private static readonly Guid AppsFolderGuid = new Guid("1e87508d-89c2-42f0-8a7e-645a0f50ca58");

    public static List<AppInfo> GetInstalledApps()
    {
        var apps = new List<AppInfo>();
        int processedCount = 0;
        int skippedCount = 0;

        try
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "[AppEnumerator] Starting enumeration...");

            // Note: KnownFolderHelper or similar might be needed depending on the exact version of the library.
            // But usually ShellObject.FromParsingName("shell:AppsFolder") works best for generic shell items.
            // Using the GUID directly with KnownFolder if possible.

            // Approach 1: ShellObject.FromParsingName
            // shell:AppsFolder is a virtual folder.
            using var appsFolder = (ShellObject)ShellObject.FromParsingName("shell:AppsFolder");

            // Use simple iteration if the library supports enumerating children of ShellObject
            // Typically appsFolder would be castable to ShellContainer or IKBownFolder

            // In WindowsAPICodePack 1.1, we might need KeyKnownFolder.
            // Let's try iterating if it's a container.
            if (appsFolder is ShellContainer container)
            {
                foreach (var item in container)
                {
                    try
                    {
                        var name = item.Name?.Trim();
                        if (string.IsNullOrEmpty(name)) continue;

                        var app = new AppInfo
                        {
                            DisplayName = name
                        };

                        // 新的識別邏輯（修正 Sprint 03.5 的問題）：
                        // 1. 優先查看 System.Link.TargetParsingPath（Win32 App 如 Chrome/Cursor 的真實路徑）
                        // 2. 查看 System.AppUserModel.ID（真正的 AUMID，必須包含 "!" 才是 UWP）
                        // 3. Fallback 到 ParsingName

                        bool identified = false;

                        // 1. 嘗試 System.Link.TargetParsingPath（解決 Chrome/Cursor 等短名稱問題）
                        try
                        {
                            var targetPath = item.Properties.System.Link.TargetParsingPath?.Value?.Trim();
                            // 跳過 CLSID 格式（如檔案總管的 ::{52205FD8-...}）
                            if (!string.IsNullOrEmpty(targetPath) && !targetPath.StartsWith("::"))
                            {
                                app.ExecutablePath = targetPath;
                                identified = true;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Instance.LogMessage(TracingLevel.WARN, $"[AppEnumerator] Failed to read TargetParsingPath for {name}: {ex.Message}");
                        }

                        // 2. 嘗試 System.AppUserModel.ID（真正的 UWP App 包含 "!"）
                        if (!identified)
                        {
                            try
                            {
                                var aumid = item.Properties.System.AppUserModel.ID?.Value?.Trim();
                                if (!string.IsNullOrEmpty(aumid) && aumid.Contains("!"))
                                {
                                    app.AppUserModelId = aumid;
                                    identified = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.Instance.LogMessage(TracingLevel.WARN, $"[AppEnumerator] Failed to read AppUserModelID for {name}: {ex.Message}");
                            }
                        }

                        // 3. Fallback 到 ParsingName
                        if (!identified)
                        {
                            var parsingName = item.ParsingName?.Trim();
                            if (!string.IsNullOrEmpty(parsingName))
                            {
                                // 特殊處理：檔案總管（系統元件）
                                if (parsingName.Equals("Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Explorer.exe 的路徑通常在 Windows 目錄
                                    var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                                    app.ExecutablePath = Path.Combine(windowsPath, "explorer.exe");
                                    identified = true;
                                }
                                else if (parsingName.Contains(":\\") || parsingName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    app.ExecutablePath = parsingName;
                                }
                                else if (parsingName.Contains("!"))
                                {
                                    app.AppUserModelId = parsingName;
                                }
                                // 如果都不是，則不設定（可能是系統元件或捷徑）
                            }
                        }

                        // 只加入有有效識別符的 App
                        if (!string.IsNullOrEmpty(app.ExecutablePath) || !string.IsNullOrEmpty(app.AppUserModelId))
                        {
                            apps.Add(app);
                            processedCount++;
                        }
                        else
                        {
                            skippedCount++;
                        }

                        // 每處理 20 個 App 就 log 一次進度
                        if ((processedCount + skippedCount) % 20 == 0)
                        {
                            Logger.Instance.LogMessage(TracingLevel.INFO, $"[AppEnumerator] Progress: {processedCount} added, {skippedCount} skipped");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log (or ignore) specific app failure
                        Logger.Instance.LogMessage(TracingLevel.WARN, $"[AppEnumerator] Error processing app item: {ex.Message}");
                        skippedCount++;
                    }
                    finally
                    {
                        item.Dispose();
                    }
                }
            }

            Logger.Instance.LogMessage(TracingLevel.INFO, $"[AppEnumerator] Enumeration complete: {processedCount} apps added, {skippedCount} skipped");
        }
        catch (Exception ex)
        {
            // Log error
            Logger.Instance.LogMessage(TracingLevel.ERROR, $"[AppEnumerator] Fatal error enumerating apps: {ex.Message}");
        }

        return apps;
    }
}
