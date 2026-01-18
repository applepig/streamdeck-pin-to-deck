# Technical Specification: Pin to Deck

> 📘 Technical details and architecture of the Pin to Deck plugin.

## 1. Project Goal

To create a Stream Deck plugin that emulates Windows Taskbar functionality on Stream Deck keys:
- **Pin** applications to physical keys.
- **Launch / Focus / Cycle / Close** application windows.
- Provide visual feedback (running status, window count).

## 2. Architecture

### 2.1 Environment

| Item | Specification |
|------|---------------|
| Language | C# |
| Framework | .NET 8.0-windows |
| SDK | [BarRaider StreamDeck-Tools](https://github.com/BarRaider/streamdeck-tools) v6.3+ |
| Target OS | Windows 10/11 |

### 2.2 Dependencies

- **StreamDeck-Tools**: Wrapper for Elgato Stream Deck SDK.
- **WindowsAPICodePackShell**: For enumerating installed apps via `shell:AppsFolder`.
- **System.Drawing.Common**: For generic GDI+ generic operations (Icon processing).

### 2.3 Project Structure

```text
PinToDeck/
├── Actions/
│   └── PinAppAction.cs           // Main Action logic (inherits KeypadBase)
├── Core/
│   ├── WindowManager.cs          // Win32 API wrapper (Window management)
│   ├── AppEnumerator.cs          // App enumeration (shell:AppsFolder)
│   ├── AppLauncher.cs            // App launching (Win32 Path / UWP AUMID)
│   └── IconProcessor.cs          // Icon processing (Grayscale/Badge)
├── Models/
│   ├── WindowInfo.cs             // Window data structure
│   └── AppInfo.cs                // Application data structure
├── PropertyInspector/            // Frontend UI
└── Images/                       // Assets
```

## 3. Core Logic specifications

### 3.1 State Machine

The plugin key changes visual state based on the application status:

```
┌─────────────────┐
│   NOT_RUNNING   │ ←──────────────────────────────┐
│ (Grayscale Icon)│                                │
└────────┬────────┘                                │
         │ Process Start                           │
         ▼                                         │
┌─────────────────┐       Focus        ┌─────────────────┐
│    BACKGROUND   │ ─────────────────▶ │   FOREGROUND    │
│   (Color Icon)  │ ◀───────────────── │   (Color Icon)  │
└────────┬────────┘     Lost Focus     └────────┬────────┘
         │                                      │
         │ Process Exit                         │ Process Exit
         └──────────────────────────────────────┘
```

### 3.2 Interaction Logic

| State | Short Press (KeyDown) | Long Press (KeyLongPress) |
|-------|-----------------------|---------------------------|
| **NOT_RUNNING** | Launch App (`Process.Start`) | - |
| **BACKGROUND** | Bring window to foreground | Close Window (`WM_CLOSE`) |
| **FOREGROUND** | Cycle to next window (if multiple) | Close Window (`WM_CLOSE`) |

### 3.3 Window Cycle Logic (Pseudo-code)

```csharp
var windows = WindowManager.GetWindowsByProcess(processName);
int currentIndex = windows.FindIndex(w => w == GetForegroundWindow());
int nextIndex = (currentIndex + 1) % windows.Count;
SetForegroundWindow(windows[nextIndex]);
```

## 4. Windows API Reference

### 4.1 Key P/Invoke APIs

| API | DLL | Purpose |
|-----|-----|---------|
| `EnumWindows` | user32.dll | Enumerate all top-level windows |
| `GetWindowThreadProcessId` | user32.dll | Get PID from Window Handle |
| `GetForegroundWindow` | user32.dll | Get current active window |
| `SetForegroundWindow` | user32.dll | Activate window |
| `ShowWindow` | user32.dll | Restore minimized windows |
| `IsIconic` | user32.dll | Check if minimized |
| `SendMessage` | user32.dll | Send `WM_CLOSE` message |
| `AttachThreadInput` | user32.dll | Bypass `SetForegroundWindow` restrictions |

### 4.2 Handling SetForegroundWindow Restrictions

Windows prevents non-foreground processes from stealing focus. We use `AttachThreadInput` as a workaround:

```csharp
uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
uint currentThread = GetCurrentThreadId();

// Attach input processing mechanism
AttachThreadInput(foregroundThread, currentThread, true);
SetForegroundWindow(targetWindow);
AttachThreadInput(foregroundThread, currentThread, false);
```

### 4.3 App Enumeration

We use `shell:AppsFolder` to support both legacy Win32 apps and modern UWP apps:
- Use `ParsingName` as the AppUserModelId (AUMID).
- Use `Thumbnail` interface to extract high-quality icons.

## 5. Icon Processing

### 5.1 Grayscale Conversion

We use a `ColorMatrix` to convert colorful icons to grayscale when the app is not running:

```csharp
ColorMatrix grayscaleMatrix = new ColorMatrix(new float[][] {
    new float[] {0.299f, 0.299f, 0.299f, 0, 0},
    new float[] {0.587f, 0.587f, 0.587f, 0, 0},
    new float[] {0.114f, 0.114f, 0.114f, 0, 0},
    new float[] {0, 0, 0, 1, 0},
    new float[] {0, 0, 0, 0, 1}
});
```

### 5.2 Window Badge

A circular badge is drawn on the top-right corner of the icon to indicate the number of open windows (when count > 1).

## 6. References

- [BarRaider StreamDeck-Tools](https://github.com/BarRaider/streamdeck-tools)
- [Elgato Stream Deck SDK](https://docs.elgato.com/sdk/plugins/overview)
- [pinvoke.net](http://pinvoke.net/)
- [Microsoft.WindowsAPICodePack-Shell](https://www.nuget.org/packages/Microsoft.WindowsAPICodePack-Shell)
