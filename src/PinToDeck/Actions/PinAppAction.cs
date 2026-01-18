using BarRaider.SdTools;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PinToDeck.Core;
using PinToDeck.Models;

namespace PinToDeck.Actions
{
    /// <summary>
    /// Pin App Action - 主要的 Stream Deck Action，用於釘選和控制應用程式
    /// </summary>
    [PluginActionId("tw.idv.applepig.pin-to-deck.action")]
    public class PinAppAction : KeypadBase
    {
        private class PluginSettings
        {
            public static PluginSettings CreateDefaultSettings()
            {
                var instance = new PluginSettings
                {
                    AppId = string.Empty,
                    AppArgs = string.Empty,
                    BadgePosition = BadgePosition.BottomRight,
                    ShowOverlay = true,
                    EnableWindowCycling = true,
                    LongPressAction = LongPressAction.DoNothing
                };
                return instance;
            }

            [JsonProperty(PropertyName = "app_id")]
            public string AppId { get; set; }

            [JsonProperty(PropertyName = "app_args")]
            public string AppArgs { get; set; }

            [JsonProperty(PropertyName = "cachedIconBase64")]
            public string? CachedIconBase64 { get; set; }

            [JsonProperty(PropertyName = "badge_position")]
            public BadgePosition BadgePosition { get; set; } = BadgePosition.BottomRight;

            [JsonProperty(PropertyName = "show_overlay")]
            public bool ShowOverlay { get; set; } = true;

            [JsonProperty(PropertyName = "enable_window_cycling")]
            public bool EnableWindowCycling { get; set; } = true;

            [JsonProperty(PropertyName = "long_press_action")]
            public LongPressAction LongPressAction { get; set; } = LongPressAction.DoNothing;
        }

        private PluginSettings settings;
        private Bitmap? _originalIcon;
        private AppState _lastState = AppState.NotRunning;
        private int _lastCount = -1;
        private readonly IconProcessor _iconProcessor = new IconProcessor();

        // Cycling Session Management
        private List<IntPtr> _sessionWindows = new List<IntPtr>();
        private int _sessionIndex = 0;
        private DateTime _lastCycleTime = DateTime.MinValue;
        private const int CYCLE_SESSION_TIMEOUT_MS = 3000;

        // Long Press Management
        private DateTime _keyDownStart = DateTime.MinValue;
        private const int LONG_PRESS_THRESHOLD_MS = 800;
        private CancellationTokenSource? _longPressToken;
        private bool _longPressExecuted = false;

        private int _tickCounter = 0;

        public PinAppAction(ISDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
            {
                this.settings = PluginSettings.CreateDefaultSettings();
            }
            else
            {
                this.settings = payload.Settings.ToObject<PluginSettings>();
            }

            // Register app for state tracking
            WindowCacheManager.Instance.RegisterApp(settings.AppId);

            // Subscribe to state changes (Event-driven UI update)
            WindowCacheManager.Instance.AppStateChanged += Instance_AppStateChanged;

            Connection.OnSendToPlugin += Connection_OnSendToPlugin;

            // Offload initialization to background thread
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(settings.CachedIconBase64))
                    {
                        await LoadIconFromCacheAsync(settings.CachedIconBase64);
                    }
                    else
                    {
                        await UpdateAppIcon();
                    }
                }
                finally
                {
                }
            });
        }

        public override void Dispose()
        {
            // Cancel any pending long press timer
            _longPressToken?.Cancel();
            _longPressToken?.Dispose();
            _longPressToken = null;

            Connection.OnSendToPlugin -= Connection_OnSendToPlugin;
            WindowCacheManager.Instance.AppStateChanged -= Instance_AppStateChanged;
            WindowCacheManager.Instance.UnregisterApp(settings.AppId);
            _originalIcon?.Dispose();
            Logger.Instance.LogMessage(TracingLevel.INFO, "PinAppAction Disposed");
        }

        public override void KeyPressed(KeyPayload payload)
        {
            _keyDownStart = DateTime.Now;
            _longPressExecuted = false;

            // Start long press timer if LongPressAction is configured
            if (settings.LongPressAction != LongPressAction.DoNothing && !string.IsNullOrEmpty(settings.AppId))
            {
                _longPressToken?.Cancel();
                _longPressToken?.Dispose();
                _longPressToken = new CancellationTokenSource();
                _ = WaitForLongPressAsync(_longPressToken.Token);
            }
        }

        private async Task WaitForLongPressAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(LONG_PRESS_THRESHOLD_MS, token);

                // If we reach here without cancellation, trigger long press immediately
                _longPressExecuted = true;
                Logger.Instance.LogMessage(TracingLevel.INFO, $"[LongPress] Threshold reached ({LONG_PRESS_THRESHOLD_MS}ms). Action: {settings.LongPressAction}");
                ExecuteLongPress();
            }
            catch (OperationCanceledException)
            {
                // Timer was cancelled (key released before threshold), do nothing here
            }
        }

        public override void KeyReleased(KeyPayload payload)
        {
            try
            {
                // Cancel long press timer if still running
                _longPressToken?.Cancel();
                _longPressToken?.Dispose();
                _longPressToken = null;

                if (string.IsNullOrEmpty(settings.AppId))
                {
                    Logger.Instance.LogMessage(TracingLevel.WARN, "KeyReleased: AppId is empty");
                    return;
                }

                // If long press was already executed, skip short press
                if (_longPressExecuted)
                {
                    _longPressExecuted = false;
                    Logger.Instance.LogMessage(TracingLevel.INFO, "[KeyReleased] Long press already executed, skipping short press.");
                    return;
                }

                // Execute short press
                double duration = (DateTime.Now - _keyDownStart).TotalMilliseconds;
                Logger.Instance.LogMessage(TracingLevel.INFO, $"[ShortPress] Duration: {duration}ms. Executing Standard Action.");
                ExecuteShortPress();
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"KeyReleased error: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void ExecuteLongPress()
        {
            switch (settings.LongPressAction)
            {
                case LongPressAction.OpenNewInstance:
                    // Force Launch
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"[LongPress] Exceuting OpenNewInstance for {settings.AppId}");
                    AppLauncher.Instance.LaunchApp(settings.AppId, settings.AppArgs);
                    break;

                case LongPressAction.QuitApplication:
                    // Soft Close
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"[LongPress] Executing QuitApplication for {settings.AppId}");
                    WindowManager.Instance.CloseApp(settings.AppId);
                    break;

                case LongPressAction.ForceStop:
                    // Force Kill
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"[LongPress] Executing ForceStop for {settings.AppId}");
                    WindowManager.Instance.KillApp(settings.AppId);
                    break;

                case LongPressAction.DoNothing:
                default:
                    // Should not happen due to if check, but fallback to nothing
                    break;
            }
        }

        private void ExecuteShortPress()
        {
            // Get current state from cache directly for logic
            (var state, var count) = WindowCacheManager.Instance.GetCachedState(settings.AppId);
            Logger.Instance.LogMessage(TracingLevel.INFO, $"ExecuteShortPress: {settings.AppId}, State={state}, Count={count}");

            switch (state)
            {
                case AppState.NotRunning:
                    // 啟動 App
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Action: Launching {settings.AppId}");
                    AppLauncher.Instance.LaunchApp(settings.AppId, settings.AppArgs);
                    break;

                case AppState.Background:
                    // 將視窗帶到前台
                    var windows = WindowManager.Instance.GetWindowsByAppIdCached(settings.AppId);
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"Action: Bringing to foreground. Found {windows.Count} windows.");

                    if (windows.Count > 0)
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"Target Window: {windows[0].Title} (0x{windows[0].Handle:X})");
                        WindowManager.Instance.BringWindowToForeground(windows[0].Handle);

                        // Trigger overlay if enabled
                        if (settings.ShowOverlay)
                        {
                            string winTitle = WindowManager.Instance.GetWindowTitle(windows[0].Handle);
                            OverlayManager.Instance.Show(winTitle);
                        }
                    }
                    else
                    {
                        // 特殊情況：Process 正在執行但沒有可見視窗
                        Logger.Instance.LogMessage(TracingLevel.INFO, "Action: Process running but no window. Launching...");
                        AppLauncher.Instance.LaunchApp(settings.AppId, settings.AppArgs);
                    }
                    break;

                case AppState.Foreground:
                    // Sprint 05: Session-based Window Cycling (Snapshot Strategy)

                    // If window cycling is disabled, just show overlay for current window
                    if (!settings.EnableWindowCycling)
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, "[Cycle] Disabled by user setting.");
                        if (settings.ShowOverlay)
                        {
                            var fgHwnd = WindowManager.Instance.GetForegroundWindow();
                            string winTitle = WindowManager.Instance.GetWindowTitle(fgHwnd);
                            OverlayManager.Instance.Show(winTitle);
                        }
                        break;
                    }

                    var currentHwnd = WindowManager.Instance.GetForegroundWindow();
                    IntPtr targetHwnd = IntPtr.Zero;

                    // Check session timeout or uninitialized
                    if ((DateTime.Now - _lastCycleTime).TotalMilliseconds > CYCLE_SESSION_TIMEOUT_MS || _sessionWindows == null || _sessionWindows.Count == 0)
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"[Cycle] New session started (Snapshot taken)");

                        // Snapshot current windows z-order
                        var currentWindows = WindowManager.Instance.GetWindowsByAppIdCached(settings.AppId);
                        _sessionWindows = currentWindows.Select(w => w.Handle).ToList();

                        // Find current index
                        _sessionIndex = _sessionWindows.IndexOf(currentHwnd);
                        if (_sessionIndex == -1) _sessionIndex = 0; // Should not happen if current makes sense, but fallback
                    }

                    // Just in case list is still empty (no windows found)
                    if (_sessionWindows.Count <= 1)
                    {
                        Logger.Instance.LogMessage(TracingLevel.INFO, "[Cycle] Single window, nothing to cycle.");
                        // Even if single window, trigger overlay if requested to confirm "Current Window"
                        if (settings.ShowOverlay && _sessionWindows.Count == 1)
                        {
                            string winTitle = WindowManager.Instance.GetWindowTitle(_sessionWindows[0]);
                            OverlayManager.Instance.Show(winTitle);
                        }
                        break;
                    }

                    // Move to next index
                    _sessionIndex = (_sessionIndex + 1) % _sessionWindows.Count;
                    targetHwnd = _sessionWindows[_sessionIndex];

                    if (targetHwnd != IntPtr.Zero)
                    {
                        _lastCycleTime = DateTime.Now; // Update timestamp
                        Logger.Instance.LogMessage(TracingLevel.INFO, $"[Cycle] Switching to Index {_sessionIndex}/{_sessionWindows.Count} (0x{targetHwnd:X})");
                        WindowManager.Instance.BringWindowToForeground(targetHwnd);

                        // Trigger overlay if enabled
                        if (settings.ShowOverlay)
                        {
                            string winTitle = WindowManager.Instance.GetWindowTitle(targetHwnd);
                            OverlayManager.Instance.Show(winTitle);
                        }
                    }
                    else if (settings.ShowOverlay && currentHwnd != IntPtr.Zero)
                    {
                        // Fallback: If for some reason we didn't switch but are foreground, show current title
                        string winTitle = WindowManager.Instance.GetWindowTitle(currentHwnd);
                        OverlayManager.Instance.Show(winTitle);
                    }
                    break;
            }
        }

        private void Instance_AppStateChanged(string appId, WindowCacheManager.CachedAppState newState)
        {
            // Only handle updates for this action's AppId
            if (appId != settings.AppId) return;
            if (_originalIcon == null) return;

            try
            {
                // UI Update on separate thread (safe with BarRaider SDK)
                using (var processed = _iconProcessor.Process(_originalIcon, newState.State, newState.WindowCount, settings.BadgePosition))
                {
                    Connection.SetImageAsync(processed);
                }
                _lastState = newState.State;
                _lastCount = newState.WindowCount;
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"UI Update Error: {ex.Message}");
            }
        }

        public override void OnTick()
        {
            // Debug info only
            _tickCounter++;
            if (_tickCounter >= 3)
            {
                _tickCounter = 0;
                (var state, var count) = WindowCacheManager.Instance.GetCachedState(settings.AppId);
                _ = SendDebugInfoToPI(state, count);
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            string oldAppId = settings.AppId;
            BadgePosition oldBadgePosition = settings.BadgePosition;

            Tools.AutoPopulateSettings(settings, payload.Settings);
            Logger.Instance.LogMessage(TracingLevel.INFO, $"Settings loaded: AppId={settings.AppId}");

            // Only update if AppId changed
            if (settings.AppId != oldAppId)
            {
                WindowCacheManager.Instance.UnregisterApp(oldAppId);
                WindowCacheManager.Instance.RegisterApp(settings.AppId);
            }

            if (settings.AppId != oldAppId || string.IsNullOrEmpty(settings.CachedIconBase64) || _originalIcon == null)
            {
                _ = UpdateAppIcon();
            }
            else if (settings.BadgePosition != oldBadgePosition && _originalIcon != null)
            {
                // Redraw if only badge position changed
                (var state, var count) = WindowCacheManager.Instance.GetCachedState(settings.AppId);
                Instance_AppStateChanged(settings.AppId, new WindowCacheManager.CachedAppState(state, count));
            }
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
        {
        }

        private async void Connection_OnSendToPlugin(object? sender, BarRaider.SdTools.Wrappers.SDEventReceivedEventArgs<BarRaider.SdTools.Events.SendToPlugin> e)
        {
            var payload = e.Event.Payload;
            var eventName = payload["event"]?.ToString();
            var cmd = payload["cmd"]?.ToString();

            if (eventName == "getAppList" || cmd == "getAppList")
            {
                await SendAppListAsync("getAppList");
            }
            else if (cmd == "updateIcon" && payload["appId"] != null)
            {
                var appId = payload["appId"]?.ToString();
                if (!string.IsNullOrEmpty(appId))
                {
                    settings.AppId = appId;
                    await UpdateAppIcon();
                }
            }
        }

        private async Task SendAppListAsync(string eventName)
        {
            await Task.Run(async () =>
            {
                try
                {
                    var apps = AppEnumerator.GetInstalledApps();

                    // 分組：Win32 (有 ExecutablePath) 和 UWP (只有 AppUserModelId)
                    var win32Apps = apps
                        .Where(a => !string.IsNullOrEmpty(a.DisplayName) && !string.IsNullOrEmpty(a.ExecutablePath))
                        .OrderBy(a => a.DisplayName)
                        .Select(a => new
                        {
                            label = a.DisplayName,
                            value = a.ExecutablePath!
                        })
                        .ToList();

                    var uwpApps = apps
                        .Where(a => !string.IsNullOrEmpty(a.DisplayName) &&
                                    string.IsNullOrEmpty(a.ExecutablePath) &&
                                    !string.IsNullOrEmpty(a.AppUserModelId))
                        .OrderBy(a => a.DisplayName)
                        .Select(a => new
                        {
                            label = a.DisplayName,
                            value = a.AppUserModelId!
                        })
                        .ToList();

                    // 建立分組結構 (optgroup format)
                    var groups = new List<object>();

                    if (win32Apps.Count > 0)
                    {
                        groups.Add(new
                        {
                            group = "Win32 Applications",
                            children = win32Apps
                        });
                    }

                    if (uwpApps.Count > 0)
                    {
                        groups.Add(new
                        {
                            group = "UWP Applications",
                            children = uwpApps
                        });
                    }

                    var response = new
                    {
                        @event = eventName,
                        items = groups
                    };

                    await Connection.SendToPropertyInspectorAsync(JObject.FromObject(response));
                }
                catch (Exception ex)
                {
                    Logger.Instance.LogMessage(TracingLevel.ERROR, $"[OnSendToPlugin] Error: {ex.Message}");
                }
            });
        }

        private async Task SendDebugInfoToPI(AppState state, int count)
        {
            try
            {
                var debugInfo = new
                {
                    @event = "debugInfo",
                    appId = settings.AppId,
                    state = state.ToString(),
                    windowCount = count,
                    timestamp = DateTime.Now.ToString("HH:mm:ss")
                };

                await Connection.SendToPropertyInspectorAsync(JObject.FromObject(debugInfo));
            }
            catch { }
        }

        private int? _cachedTargetSize;

        private int GetTargetIconSize()
        {
            if (_cachedTargetSize.HasValue) return _cachedTargetSize.Value;

            int size = 144; // Default Standard 72x72 @ 2x
            try
            {
                var info = Connection.DeviceInfo();
                if (info != null)
                {
                    // info.Type is StreamDeckDeviceType enum
                    switch (info.Type)
                    {
                        case DeviceType.StreamDeckXL:
                        case DeviceType.StreamDeckPlus:
                            size = 192; // 96 * 2 (XL and Plus keys are 96x96)
                            break;
                        default:
                            size = 144; // 72 * 2 (Standard, Mini, Neo)
                            break;
                    }
                    Logger.Instance.LogMessage(TracingLevel.INFO, $"[PinAppAction] Device detected: {info.Type}, Target Size: {size}px");
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.WARN, $"[PinAppAction] Failed to detect device size: {ex.Message}");
            }

            _cachedTargetSize = size;
            return size;
        }

        private async Task UpdateAppIcon()
        {
            if (string.IsNullOrEmpty(settings.AppId)) return;

            try
            {
                string? base64Icon = IconExtractor.ExtractIconBase64(settings.AppId);
                if (!string.IsNullOrEmpty(base64Icon))
                {
                    settings.CachedIconBase64 = base64Icon;
                    await Connection.SetSettingsAsync(JObject.FromObject(settings));
                    await LoadIconFromCacheAsync(base64Icon);
                }
            }
            catch (Exception ex)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to update app icon: {ex.Message}");
            }
        }

        private async Task LoadIconFromCacheAsync(string base64Icon)
        {
            try
            {
                var base64Data = base64Icon.Contains(",") ? base64Icon.Substring(base64Icon.IndexOf(",") + 1) : base64Icon;
                byte[] byteBuffer = Convert.FromBase64String(base64Data);

                var oldIcon = _originalIcon;
                _originalIcon = new Bitmap(new MemoryStream(byteBuffer));

                oldIcon?.Dispose();

                // Initial render
                (var state, var count) = WindowCacheManager.Instance.GetCachedState(settings.AppId);

                int targetSize = GetTargetIconSize();
                using (var processed = _iconProcessor.Process(_originalIcon, state, count, settings.BadgePosition, targetSize))
                {
                    await Connection.SetImageAsync(processed);
                }
                _lastState = state;
                _lastCount = count;
            }
            catch (Exception iconEx)
            {
                Logger.Instance.LogMessage(TracingLevel.ERROR, $"Failed to load icon from cache: {iconEx.Message}");
            }
        }
    }
}
