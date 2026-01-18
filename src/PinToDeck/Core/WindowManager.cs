using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using BarRaider.SdTools;
using PinToDeck.Models;

namespace PinToDeck.Core
{
    public class WindowManager
    {
        private static readonly Lazy<WindowManager> _instance = new Lazy<WindowManager>(() => new WindowManager());
        public static WindowManager Instance => _instance.Value;

        // Cache Key: AppId
        private readonly Dictionary<string, (List<WindowInfo> Windows, DateTime LastCheck)> _windowsCache = new Dictionary<string, (List<WindowInfo>, DateTime)>();
        private readonly Dictionary<string, (bool IsRunning, DateTime LastCheck)> _processRunningCache = new Dictionary<string, (bool IsRunning, DateTime LastCheck)>();

        // New: PID Cache to avoid repeated OpenProcess calls
        // Since PIDs are stable for the lifetime of a process, allow 5 seconds cache
        private readonly Dictionary<uint, (string? Path, string? Aumid, DateTime LastCheck)> _processInfoCache = new Dictionary<uint, (string?, string?, DateTime)>();
        private const int PROCESS_CACHE_DURATION_MS = 5000;

        private const int CACHE_DURATION_MS = 500;

        private WindowManager() { }

        /// <summary>
        /// Shared logic for determining if a window matches the given appId.
        /// This is the Single Source of Truth (SSOT) for window matching.
        /// </summary>
        public static bool IsWindowMatchingAppId(WindowInfo w, string appId)
        {
            if (string.IsNullOrEmpty(appId)) return false;

            bool isFilePath = appId.Contains(Path.DirectorySeparatorChar);
            bool isAumid = appId.Contains("!");

            // Special handling for Explorer
            bool isWindowExplorer = w.ProcessPath != null &&
                                    w.ProcessPath.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase);
            bool isAppIdExplorer = appId.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) ||
                                   appId.Contains("Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(appId, "explorer", StringComparison.OrdinalIgnoreCase);

            if (isWindowExplorer)
            {
                if (isAppIdExplorer)
                {
                    // IMPORTANT: For Explorer, only count actual File Explorer windows
                    // CabinetWClass = Explorer windows with folder view
                    // ExplorerWClass = Explorer windows (alternate)
                    // We explicitly EXCLUDE: Shell_TrayWnd (Taskbar), Progman/Program Manager (Desktop)
                    return w.ClassName == "CabinetWClass" || w.ClassName == "ExplorerWClass";
                }
                // If appId is NOT explorer but window IS explorer, it's not a match
                return false;
            }

            // 1. Exact Path Match
            if (string.Equals(w.ProcessPath, appId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. AUMID Match
            if (string.Equals(w.Aumid, appId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. Filename Fallback (ONLY if appId is NOT a full path or AUMID)
            if (!isFilePath && !isAumid && w.ProcessPath != null)
            {
                string name = Path.GetFileNameWithoutExtension(w.ProcessPath);
                if (string.Equals(name, appId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public List<WindowInfo> GetWindowsByAppIdCached(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return new List<WindowInfo>();

            if (_windowsCache.TryGetValue(appId, out var cache))
            {
                if ((DateTime.Now - cache.LastCheck).TotalMilliseconds < CACHE_DURATION_MS)
                {
                    return cache.Windows;
                }
            }

            var all = GetAllWindows();
            var filtered = new List<WindowInfo>();

            foreach (var w in all)
            {
                // Use the shared SSOT helper for matching
                if (IsWindowMatchingAppId(w, appId))
                {
                    filtered.Add(w);
                }
            }

            _windowsCache[appId] = (filtered, DateTime.Now);
            return filtered;
        }

        public HashSet<string> GetAllRunningAppIds()
        {
            var runningApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var processes = Process.GetProcesses();

            foreach (var p in processes)
            {
                try
                {
                    try
                    {
                        if (p.MainModule != null) runningApps.Add(p.MainModule.FileName);
                    }
                    catch { }

                    var pid = (uint)p.Id;
                    // Try to reuse PID cache logic? Or separate helper?
                    // Let's reuse existing helper directly here
                    string? aumid = GetAumidFromProcess(pid);
                    if (aumid != null) runningApps.Add(aumid);
                }
                catch { }
                finally { p.Dispose(); }
            }
            return runningApps;
        }

        public bool IsAppRunningCached(string appId)
        {
            if (_processRunningCache.TryGetValue(appId, out var cache))
            {
                if ((DateTime.Now - cache.LastCheck).TotalMilliseconds < CACHE_DURATION_MS)
                {
                    return cache.IsRunning;
                }
            }
            bool isRunning = CheckAppRunning(appId);
            _processRunningCache[appId] = (isRunning, DateTime.Now);
            return isRunning;
        }

        private bool CheckAppRunning(string appId)
        {
            bool isFilePath = appId.Contains(Path.DirectorySeparatorChar);
            if (appId.Contains("Microsoft.Windows.Explorer") || appId.EndsWith("explorer.exe"))
                return Process.GetProcessesByName("explorer").Length > 0;

            var allProcesses = Process.GetProcesses();
            foreach (var process in allProcesses)
            {
                try
                {
                    bool match = false;
                    if (isFilePath)
                    {
                        try { if (process.MainModule?.FileName.Equals(appId, StringComparison.OrdinalIgnoreCase) == true) match = true; } catch { }
                        if (!match && process.ProcessName.Equals(Path.GetFileNameWithoutExtension(appId), StringComparison.OrdinalIgnoreCase)) match = true;
                    }
                    else
                    {
                        string? aumid = GetAumidFromProcess((uint)process.Id);
                        if (string.Equals(aumid, appId, StringComparison.OrdinalIgnoreCase)) match = true;
                    }
                    if (match) return true;
                }
                catch { }
                finally { process.Dispose(); }
            }
            return false;
        }

        /// <summary>
        /// Optimized Global Window Scan
        /// Replaces slow Process objects with Native Info Cache
        /// </summary>
        public List<WindowInfo> GetAllWindows()
        {
            var windows = new List<WindowInfo>();

            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                if (!NativeMethods.IsWindowVisible(hWnd)) return true;

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);

                StringBuilder sbClass = new StringBuilder(256);
                NativeMethods.GetClassName(hWnd, sbClass, 256);
                string className = sbClass.ToString();

                string? path = null;
                string? aumid = null;

                // --- Optimized Process Info Retrieval ---
                // 1. Check Cache
                if (_processInfoCache.TryGetValue(processId, out var cachedInfo) &&
                    (DateTime.Now - cachedInfo.LastCheck).TotalMilliseconds < PROCESS_CACHE_DURATION_MS)
                {
                    path = cachedInfo.Path;
                    aumid = cachedInfo.Aumid;
                }
                else
                {
                    // 2. Fetch via Native API (avoid C# Process object overhead)
                    IntPtr hProcess = NativeMethods.OpenProcess(
                        NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                        false,
                        processId);

                    if (hProcess != IntPtr.Zero)
                    {
                        try
                        {
                            // A. Get Path
                            StringBuilder buffer = new StringBuilder(1024);
                            int size = buffer.Capacity;
                            if (NativeMethods.QueryFullProcessImageName(hProcess, 0, buffer, ref size))
                            {
                                path = buffer.ToString();
                            }

                            // B. Get AUMID
                            uint length = 0;
                            NativeMethods.GetApplicationUserModelId(hProcess, ref length, null!);
                            if (length > 0)
                            {
                                StringBuilder sbAumid = new StringBuilder((int)length);
                                if (NativeMethods.GetApplicationUserModelId(hProcess, ref length, sbAumid) == 0)
                                {
                                    aumid = sbAumid.ToString();
                                }
                            }
                        }
                        finally
                        {
                            NativeMethods.CloseHandle(hProcess);
                        }

                        // C. Special Explorer Logic
                        // If path was found and is explorer, or if manual override needed
                        // Note: QueryFullProcessImageName usually returns correct path for explorer
                        if (path != null && path.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) &&
                            (className == "CabinetWClass" || className == "ExplorerWClass"))
                        {
                            // Keep path as is
                        }
                        else if (path == null && (className == "CabinetWClass" || className == "ExplorerWClass" || className == "Shell_TrayWnd"))
                        {
                            // Fallback for system windows if path lookup failed (rare with QueryFullProcessImageName)
                            path = @"C:\Windows\explorer.exe";
                        }

                        // Update Cache
                        _processInfoCache[processId] = (path, aumid, DateTime.Now);
                    }
                }
                // ----------------------------------------

                StringBuilder sbTitle = new StringBuilder(256);
                NativeMethods.GetWindowText(hWnd, sbTitle, 256);

                // --- Filtering Logic ---
                // 1. Title Check: Most valid main windows have titles.
                // Exceptions exist, but for task switching, we usually want titled windows.
                string title = sbTitle.ToString();
                if (string.IsNullOrWhiteSpace(title)) return true; // Skip empty titles

                // 2. Size Check: Filter out 0x0 or very small windows
                if (NativeMethods.GetWindowRect(hWnd, out var rect))
                {
                    if (rect.Width < 10 || rect.Height < 10) return true;
                }

                windows.Add(new WindowInfo
                {
                    Handle = hWnd,
                    Title = title,
                    ProcessId = processId,
                    ClassName = className,
                    ProcessPath = path,
                    Aumid = aumid
                });

                return true;
            }, IntPtr.Zero);

            return windows;
        }

        // Helper just wrappers Native call for other non-cached usages
        private string? GetAumidFromProcess(uint processId)
        {
            IntPtr hProcess = NativeMethods.OpenProcess(
                NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION,
                false,
                processId);

            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    uint length = 0;
                    NativeMethods.GetApplicationUserModelId(hProcess, ref length, null!);

                    if (length > 0)
                    {
                        StringBuilder aumid = new StringBuilder((int)length);
                        int result = NativeMethods.GetApplicationUserModelId(hProcess, ref length, aumid);
                        if (result == 0) return aumid.ToString();
                    }
                }
                finally { NativeMethods.CloseHandle(hProcess); }
            }
            return null;
        }

        public string GetWindowTitle(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            StringBuilder sb = new StringBuilder(256);
            NativeMethods.GetWindowText(hwnd, sb, 256);
            return sb.ToString();
        }

        public IntPtr GetForegroundWindow()
        {
            return NativeMethods.GetForegroundWindow();
        }

        /// <summary>
        /// Enhanced Bring Window to Foreground
        /// Uses multiple strategies to overcome Windows' anti-focus-stealing protection.
        /// Particularly important for system tray apps like Everything.
        /// </summary>
        public void BringWindowToForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) throw new ArgumentException("Invalid window handle", nameof(hwnd));

            IntPtr currentForeground = NativeMethods.GetForegroundWindow();
            if (currentForeground == hwnd)
            {
                Logger.Instance.LogMessage(TracingLevel.INFO, $"[WindowManager] Window 0x{hwnd:X} is already foreground.");
                return;
            }

            Logger.Instance.LogMessage(TracingLevel.INFO, $"[WindowManager] Attempting to bring 0x{hwnd:X} to foreground...");

            // Get thread IDs
            uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(currentForeground, out _);
            uint targetThreadId = NativeMethods.GetWindowThreadProcessId(hwnd, out uint targetProcessId);
            uint currentThreadId = NativeMethods.GetCurrentThreadId();

            bool attachedToForeground = false;
            bool attachedToTarget = false;

            try
            {
                // Strategy 1: Simulate Alt key press/release to gain foreground activation rights
                // This is a well-known workaround - pressing Alt gives temporary permission to SetForegroundWindow
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "[WindowManager] Strategy 1: Simulating Alt key...");
                NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_EXTENDEDKEY | NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

                // Strategy 2: Allow target process to set foreground window
                NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

                // Strategy 3: Attach thread inputs for better success rate
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "[WindowManager] Strategy 3: Attaching thread inputs...");
                if (foregroundThreadId != currentThreadId && foregroundThreadId != 0)
                {
                    attachedToForeground = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
                }
                if (targetThreadId != currentThreadId && targetThreadId != foregroundThreadId)
                {
                    attachedToTarget = NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, true);
                }

                // Strategy 4: Get current window placement and restore appropriately
                var placement = new NativeMethods.WINDOWPLACEMENT();
                placement.length = System.Runtime.InteropServices.Marshal.SizeOf(placement);
                NativeMethods.GetWindowPlacement(hwnd, ref placement);
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"[WindowManager] Window placement showCmd: {placement.showCmd}");

                // If window is minimized or hidden, restore it
                if (NativeMethods.IsIconic(hwnd) || placement.showCmd == NativeMethods.SW_MINIMIZE || placement.showCmd == NativeMethods.SW_SHOWMINNOACTIVE)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, "[WindowManager] Window is minimized, restoring...");
                    // Use SW_RESTORE to maintain maximized state if it was maximized before minimizing
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                }
                else
                {
                    // Show the window if it's hidden
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                }

                // Strategy 5: Use SetWindowPos with TOPMOST flag, then remove it
                // This is a powerful trick that often works when SetForegroundWindow fails
                Logger.Instance.LogMessage(TracingLevel.DEBUG, "[WindowManager] Strategy 5: SetWindowPos TOPMOST trick...");
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);
                NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

                // Strategy 6: BringWindowToTop
                NativeMethods.BringWindowToTop(hwnd);

                // Strategy 7: SetForegroundWindow
                bool success = NativeMethods.SetForegroundWindow(hwnd);
                Logger.Instance.LogMessage(TracingLevel.INFO, $"[WindowManager] SetForegroundWindow result: {success}");

                // Strategy 8: If still not foreground, try SwitchToThisWindow
                IntPtr newForeground = NativeMethods.GetForegroundWindow();
                if (newForeground != hwnd)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, "[WindowManager] SetForegroundWindow did not work, trying SwitchToThisWindow...");
                    NativeMethods.SwitchToThisWindow(hwnd, true);
                }

                // Strategy 9: Final check and SetActiveWindow + SetFocus as last resort
                newForeground = NativeMethods.GetForegroundWindow();
                if (newForeground != hwnd)
                {
                    Logger.Instance.LogMessage(TracingLevel.INFO, "[WindowManager] Trying SetActiveWindow + SetFocus as last resort...");
                    NativeMethods.SetActiveWindow(hwnd);
                    NativeMethods.SetFocus(hwnd);
                }

                // Log final result
                newForeground = NativeMethods.GetForegroundWindow();
                Logger.Instance.LogMessage(TracingLevel.INFO, $"[WindowManager] Final foreground check: Target=0x{hwnd:X}, Actual=0x{newForeground:X}, Success={newForeground == hwnd}");
            }
            finally
            {
                // Detach thread inputs
                if (attachedToForeground)
                {
                    NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
                if (attachedToTarget)
                {
                    NativeMethods.AttachThreadInput(currentThreadId, targetThreadId, false);
                }
            }
        }

        /// <summary>
        /// Flashes the window caption and tray to provide visual feedback.
        /// Useful when cycling windows to identify which one was just selected.
        /// </summary>
        public void FlashWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            var fInfo = new NativeMethods.FLASHWINFO();
            fInfo.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(fInfo);
            fInfo.hwnd = hwnd;
            fInfo.dwFlags = NativeMethods.FLASHW_ALL; // Flash caption and tray
            fInfo.uCount = 3; // Flash 3 times
            fInfo.dwTimeout = 0; // Default cursor blink rate

            NativeMethods.FlashWindowEx(ref fInfo);
        }

        public void RestoreMinimizedWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) throw new ArgumentException("Invalid window handle", nameof(hwnd));
            if (NativeMethods.IsIconic(hwnd)) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }

        /// <summary>
        /// Lightweight window switch for cycling between windows of the same app.
        /// Uses standard Win32 calls without the aggressive Alt key simulation.
        /// This is preferred when the app already has foreground status.
        /// </summary>
        public void SwitchToWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) throw new ArgumentException("Invalid window handle", nameof(hwnd));

            IntPtr currentForeground = NativeMethods.GetForegroundWindow();
            if (currentForeground == hwnd)
            {
                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"[WindowManager] SwitchToWindow: 0x{hwnd:X} is already foreground.");
                return;
            }

            Logger.Instance.LogMessage(TracingLevel.INFO, $"[WindowManager] SwitchToWindow: Switching to 0x{hwnd:X}...");

            // Get thread IDs for thread input attachment
            uint foregroundThreadId = NativeMethods.GetWindowThreadProcessId(currentForeground, out _);
            uint currentThreadId = NativeMethods.GetCurrentThreadId();

            bool attached = false;
            try
            {
                // Attach thread inputs for better success rate
                if (foregroundThreadId != currentThreadId && foregroundThreadId != 0)
                {
                    attached = NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, true);
                }

                // Restore if minimized
                if (NativeMethods.IsIconic(hwnd))
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                }
                else
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                }

                // Standard bring to front sequence
                NativeMethods.BringWindowToTop(hwnd);
                bool success = NativeMethods.SetForegroundWindow(hwnd);

                if (!success)
                {
                    // Fallback to SwitchToThisWindow (mimics Alt+Tab behavior)
                    NativeMethods.SwitchToThisWindow(hwnd, true);
                }

                Logger.Instance.LogMessage(TracingLevel.DEBUG, $"[WindowManager] SwitchToWindow result: {NativeMethods.GetForegroundWindow() == hwnd}");
            }
            finally
            {
                if (attached)
                {
                    NativeMethods.AttachThreadInput(currentThreadId, foregroundThreadId, false);
                }
            }
        }

        public IntPtr GetNextWindow(string appId, IntPtr currentWindow)
        {
            var windows = GetWindowsByAppIdCached(appId);

            if (windows.Count <= 1) return IntPtr.Zero;

            // Find current window index
            int currentIndex = windows.FindIndex(w => w.Handle == currentWindow);

            // If current not found (e.g. unknown state), start from first
            if (currentIndex == -1) return windows[0].Handle;

            // Standard Next (Index + 1)
            int nextIndex = (currentIndex + 1) % windows.Count;
            var candidate = windows[nextIndex];

            // Simple Z-order cycling is handled by the caller (PinAppAction) using Snapshot Strategy.
            // This method now purely returns the raw next Z-order window.

            return candidate.Handle;
        }

        /// <summary>
        /// Soft Close: Sends WM_CLOSE to all main windows of the app.
        /// </summary>
        public void CloseApp(string appId)
        {
            var windows = GetWindowsByAppIdCached(appId);
            if (windows.Count == 0)
            {
                // Fallback: if no visible windows found (e.g. background process), try to find processes and close main window
                // But GetWindowsByAppIdCached filters for visible windows usually.
                Logger.Instance.LogMessage(TracingLevel.INFO, $"[WindowManager] CloseApp: No visible windows found for {appId}.");
                return;
            }

            Logger.Instance.LogMessage(TracingLevel.INFO, $"[WindowManager] Closing {windows.Count} windows for {appId}...");
            foreach (var w in windows)
            {
                // Try SysCommand Close (0xF060) first - acts like Alt+F4 / Clicking X
                NativeMethods.PostMessage(w.Handle, 0x0112 /* WM_SYSCOMMAND */, (IntPtr)0xF060 /* SC_CLOSE */, IntPtr.Zero);

                // Also send standard WM_CLOSE as backup
                NativeMethods.PostMessage(w.Handle, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            }
        }

        /// <summary>
        /// Force Stop: Kills all processes associated with the AppId.
        /// </summary>
        public void KillApp(string appId)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, $"[WindowManager] Force stopping {appId}...");

            // Reuse the login in GetAllRunningAppIds but filtered for this app
            // 1. Get all running processes
            var processes = Process.GetProcesses();
            bool isFilePath = appId.Contains(Path.DirectorySeparatorChar);

            foreach (var p in processes)
            {
                try
                {
                    bool match = false;
                    if (isFilePath)
                    {
                        // Path match
                        if (p.MainModule != null && string.Equals(p.MainModule.FileName, appId, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                        }
                    }
                    else
                    {
                        // AUMID match
                        var pidAumid = GetAumidFromProcess((uint)p.Id);
                        if (string.Equals(pidAumid, appId, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                        }
                        // Name Fallback
                        else if (string.Equals(p.ProcessName, appId, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                        }
                    }

                    if (match)
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"[WindowManager] Killing PID {p.Id} ({p.ProcessName})...");
                        p.Kill();
                    }
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, $"[WindowManager] Failed to check/kill PID {p.Id}: {ex.Message}");
                }
                finally
                {
                    p.Dispose();
                }
            }

            // Invalidate Caches
            _windowsCache.Remove(appId);
            _processRunningCache.Remove(appId);
        }
    }
}

