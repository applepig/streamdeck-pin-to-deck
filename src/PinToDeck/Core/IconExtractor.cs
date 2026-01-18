using Microsoft.WindowsAPICodePack.Shell;
using System.Drawing;
using System.Drawing.Imaging;

namespace PinToDeck.Core;

public static class IconExtractor
{
    public static string? ExtractIconBase64(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return null;

        // 策略：優先從 shell:AppsFolder 搜尋（有更好的 tile icon），
        // 失敗才直接從路徑/AUMID 提取

        // 1. 先嘗試從 AppsFolder 搜尋（優先，因為有 tile icon）
        var iconFromAppsFolder = FindAndExtractFromAppsFolder(appId);
        if (iconFromAppsFolder != null)
        {
            return iconFromAppsFolder;
        }

        // 2. AppsFolder 找不到，才用直接解析
        ShellObject? shellObj = null;

        try
        {
            // 2a. 完整路徑（Win32 App）: 直接用路徑
            if (appId.Contains(":\\") || appId.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    shellObj = ShellObject.FromParsingName(appId);
                }
                catch
                {
                    // 路徑可能無效
                }
            }
            // 2b. AUMID (UWP App): 需要加上 shell:AppsFolder\ 前綴
            else if (appId.Contains("!"))
            {
                try
                {
                    shellObj = ShellObject.FromParsingName($@"shell:AppsFolder\{appId}");
                }
                catch
                {
                    // AUMID 可能無效
                }
            }
            // 2c. 其他格式（舊版資料或短名稱）: 嘗試兩種方式
            else
            {
                try
                {
                    // 先嘗試直接解析
                    shellObj = ShellObject.FromParsingName(appId);
                }
                catch
                {
                    // 再嘗試加上 shell:AppsFolder\ 前綴
                    try
                    {
                        shellObj = ShellObject.FromParsingName($@"shell:AppsFolder\{appId}");
                    }
                    catch
                    {
                        // 完全失敗
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IconExtractor] Error parsing AppId '{appId}': {ex.Message}");
        }

        if (shellObj != null)
        {
            using (shellObj)
            {
                return GetBase64FromShellObject(shellObj);
            }
        }

        return null;
    }

    private static string? FindAndExtractFromAppsFolder(string targetAppId)
    {
        try
        {
            using var appsFolder = (ShellObject)ShellObject.FromParsingName("shell:AppsFolder");
            if (appsFolder is ShellContainer container)
            {
                foreach (var item in container)
                {
                    bool match = false;
                    try
                    {
                         // 多種匹配方式（與 AppEnumerator 一致）

                         // 1. 比對 System.Link.TargetParsingPath（完整路徑，跳過 CLSID）
                         try
                         {
                             var targetPath = item.Properties.System.Link.TargetParsingPath?.Value;
                             if (!string.IsNullOrEmpty(targetPath) &&
                                 !targetPath.StartsWith("::") &&  // 跳過 CLSID 格式
                                 string.Equals(targetPath, targetAppId, StringComparison.OrdinalIgnoreCase))
                             {
                                 match = true;
                             }
                         }
                         catch { }

                         // 2. 比對 System.AppUserModel.ID（AUMID）
                         if (!match)
                         {
                             try
                             {
                                 var aumid = item.Properties.System.AppUserModel.ID?.Value;
                                 if (!string.IsNullOrEmpty(aumid) &&
                                     string.Equals(aumid, targetAppId, StringComparison.OrdinalIgnoreCase))
                                 {
                                     match = true;
                                 }
                             }
                             catch { }
                         }

                         // 3. 特殊處理：檔案總管（匹配 explorer.exe 路徑）
                         if (!match &&
                             targetAppId.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(item.ParsingName, "Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase))
                         {
                             match = true;
                         }

                         // 4. 比對 ParsingName（舊版相容）
                         if (!match &&
                             string.Equals(item.ParsingName, targetAppId, StringComparison.OrdinalIgnoreCase))
                         {
                             match = true;
                         }

                         // 5. 比對 Name（最後手段）
                         if (!match &&
                             string.Equals(item.Name, targetAppId, StringComparison.OrdinalIgnoreCase))
                         {
                             match = true;
                         }
                    }
                    catch {}

                    if (match)
                    {
                         using (item)
                         {
                             return GetBase64FromShellObject(item);
                         }
                    }
                    item.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
             Console.WriteLine($"[IconExtractor] Error searching AppsFolder: {ex.Message}");
        }
        return null;
    }

    private static string? GetBase64FromShellObject(ShellObject shellObj)
    {
        try
        {
            ShellThumbnail thumbnail = shellObj.Thumbnail;
            Bitmap? bitmap = null;
            
            try 
            {
                thumbnail.FormatOption = ShellThumbnailFormatOption.IconOnly; 
                bitmap = thumbnail.ExtraLargeBitmap;
            }
            catch {}

            if (bitmap == null)
            {
                try { bitmap = thumbnail.LargeBitmap; } catch {}
            }

            if (bitmap != null)
            {
                using (bitmap)
                using (var ms = new MemoryStream())
                {
                    bitmap.Save(ms, ImageFormat.Png);
                    var bytes = ms.ToArray();
                    return "data:image/png;base64," + Convert.ToBase64String(bytes);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IconExtractor] Error extracting icon bitmap: {ex.Message}");
        }
        return null;
    }
}
