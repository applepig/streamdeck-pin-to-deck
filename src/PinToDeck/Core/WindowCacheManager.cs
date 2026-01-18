using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.IO;
using BarRaider.SdTools;
using PinToDeck.Models;

namespace PinToDeck.Core
{
    public class WindowCacheManager
    {
        private static readonly Lazy<WindowCacheManager> _instance = new Lazy<WindowCacheManager>(() => new WindowCacheManager());
        public static WindowCacheManager Instance => _instance.Value;

        public struct CachedAppState
        {
            public AppState State;
            public int WindowCount;

            public CachedAppState(AppState state, int windowCount)
            {
                State = state;
                WindowCount = windowCount;
            }

            public static bool operator ==(CachedAppState c1, CachedAppState c2)
            {
                return c1.State == c2.State && c1.WindowCount == c2.WindowCount;
            }
            public static bool operator !=(CachedAppState c1, CachedAppState c2)
            {
                return !(c1 == c2);
            }
            public override bool Equals(object obj) => obj is CachedAppState other && this == other;
            public override int GetHashCode() => (State, WindowCount).GetHashCode();
        }

        // Event for pushing updates to Actions
        public event Action<string, CachedAppState>? AppStateChanged;

        private readonly ConcurrentDictionary<string, int> _registeredApps = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, CachedAppState> _cache = new ConcurrentDictionary<string, CachedAppState>();

        private readonly System.Threading.Timer _timer;
        private readonly object _refreshLock = new object();
        private bool _isRunning = false;

        // Optimized Refresh Rate: 200ms (5Hz) for instant UI feedback
        private const int REFRESH_INTERVAL_MS = 200;
        private const int PROCESS_SCAN_INTERVAL_MS = 5000;
        private DateTime _lastProcessScanTime = DateTime.MinValue;
        private HashSet<string> _lastRunningApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private WindowCacheManager()
        {
            _timer = new System.Threading.Timer(RefreshCallback, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void RegisterApp(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return;
            _registeredApps.AddOrUpdate(appId, 1, (key, count) => count + 1);
            StartTimerIfNotRunning();
        }

        public void UnregisterApp(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return;
            int newCount = _registeredApps.AddOrUpdate(appId, 0, (key, count) => count > 0 ? count - 1 : 0);
            if (newCount == 0)
            {
                _registeredApps.TryRemove(appId, out _);
                _cache.TryRemove(appId, out _);
            }
            StopTimerIfNoApps();
        }

        public (AppState state, int count) GetCachedState(string appId)
        {
            if (string.IsNullOrWhiteSpace(appId)) return (AppState.NotRunning, 0);
            if (_cache.TryGetValue(appId, out var cached)) return (cached.State, cached.WindowCount);
            return (AppState.NotRunning, 0);
        }

        private void StartTimerIfNotRunning()
        {
            lock (_refreshLock)
            {
                if (!_isRunning && !_registeredApps.IsEmpty)
                {
                    _timer.Change(0, REFRESH_INTERVAL_MS);
                    _isRunning = true;
                    Logger.Instance.LogMessage(TracingLevel.INFO, "[WindowCacheManager] Timer Started (200ms)");
                }
            }
        }

        private void StopTimerIfNoApps()
        {
            lock (_refreshLock)
            {
                if (_isRunning && _registeredApps.IsEmpty)
                {
                    _timer.Change(Timeout.Infinite, Timeout.Infinite);
                    _isRunning = false;
                    Logger.Instance.LogMessage(TracingLevel.INFO, "[WindowCacheManager] Timer Stopped");
                }
            }
        }

        private void RefreshCallback(object? state)
        {
            if (Monitor.TryEnter(_refreshLock))
            {
                try
                {
                    RefreshAll();
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"[WindowCacheManager] Refresh Error: {ex.Message}");
                }
                finally
                {
                    Monitor.Exit(_refreshLock);
                }
            }
        }

        public void RefreshAll()
        {
            var appIds = _registeredApps.Keys.ToList();
            if (appIds.Count == 0) return;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool performedSlowScan = false;

            try
            {
                // 1. Fast Scan (Layer 1): Get all windows with details
                // Native API optimization makes this < 1-5 ms typically
                var allWindows = WindowManager.Instance.GetAllWindows();

                // 2. Slow Scan (Layer 2): Get all running processes
                if ((DateTime.Now - _lastProcessScanTime).TotalMilliseconds >= PROCESS_SCAN_INTERVAL_MS)
                {
                    _lastRunningApps = WindowManager.Instance.GetAllRunningAppIds();
                    _lastProcessScanTime = DateTime.Now;
                    performedSlowScan = true;
                }

                var foregroundWin = WindowManager.Instance.GetForegroundWindow();

                foreach (var appId in appIds)
                {
                    var newState = AppState.NotRunning;
                    int newCount = 0;

                    var matchedWindows = allWindows.Where(w => WindowManager.IsWindowMatchingAppId(w, appId)).ToList();

                    if (matchedWindows.Count > 0)
                    {
                        newCount = matchedWindows.Count;
                        newState = AppState.Background;
                        if (matchedWindows.Any(w => w.Handle == foregroundWin)) newState = AppState.Foreground;
                    }
                    else
                    {
                        // Special handling for Explorer:
                        // explorer.exe is ALWAYS running (it's the shell), but if there are no
                        // actual File Explorer windows, we should treat it as NotRunning for our purposes.
                        bool isExplorerApp = appId.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) ||
                                             appId.Contains("Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(appId, "explorer", StringComparison.OrdinalIgnoreCase);

                        if (isExplorerApp)
                        {
                            // For Explorer, no windows = NotRunning (don't check process)
                            newState = AppState.NotRunning;
                        }
                        else
                        {
                            // For other apps, check if they're running as background processes
                            bool isRunning = _lastRunningApps.Contains(appId);
                            if (!isRunning)
                            {
                                if (!appId.Contains(Path.DirectorySeparatorChar) && !appId.Contains("!"))
                                {
                                    foreach (var runningId in _lastRunningApps)
                                    {
                                        try
                                        {
                                            if (string.Equals(Path.GetFileNameWithoutExtension(runningId), appId, StringComparison.OrdinalIgnoreCase))
                                            {
                                                isRunning = true;
                                                break;
                                            }
                                        }
                                        catch { }
                                    }
                                }
                            }
                            if (isRunning) newState = AppState.Background;
                        }
                    }

                    var newCachedState = new CachedAppState
                    {
                        State = newState,
                        WindowCount = newCount
                    };

                    bool changed = false;
                    if (_cache.TryGetValue(appId, out var oldCachedState))
                    {
                        if (oldCachedState != newCachedState) changed = true;
                    }
                    else
                    {
                        changed = true;
                    }

                    _cache[appId] = newCachedState;

                    // Notify listeners if state changed
                    if (changed)
                    {
                        AppStateChanged?.Invoke(appId, newCachedState);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"[WindowCacheManager] Batch Refesh Error: {ex.Message}");
            }

            sw.Stop();
            long elapsed = sw.ElapsedMilliseconds;

            // Log warning only if significantly slow
            if (performedSlowScan)
            {
                if (elapsed > 2000) Logger.Instance.LogMessage(TracingLevel.WARN, $"[WindowCacheManager] Slow Scan took {elapsed}ms for {appIds.Count} apps.");
            }
            else
            {
                // Tighten threshold to 50ms now since we are aiming for ultra-fast
                // But keep it reasonable at 100ms for safety
                if (elapsed > 100) Logger.Instance.LogMessage(TracingLevel.WARN, $"[WindowCacheManager] Fast Scan took {elapsed}ms for {appIds.Count} apps.");
            }
        }
    }
}
