# Pin to Deck

> 🎮 Stream Deck Plugin for Windows - Pin and control Windows/UWP apps like a taskbar

**Pin to Deck** is a Stream Deck plugin that allows you to "pin" Windows applications to your Stream Deck keys, functioning just like the Windows taskbar.

## Features

- 📌 **Pin Applications** - Select from a list of installed Win32/UWP applications.
- 🎨 **Status Display** - Grayscale icon when not running, color icon when running, and a number badge for multiple windows.
- 🚀 **Quick Launch** - Short press to launch the application if it's not running.
- 🔄 **Window Switching** - Short press to cycle through multiple open windows of the pinned app.
- ❌ **Close Windows** - Long press to send a close request to the application window.

## Installation

### From Release (Recommended)
1. Download the latest `.streamDeckPlugin` file from the [Releases](https://github.com/applepig/streamdeck-pin-to-deck/releases) page.
2. Double-click the file to install it in Stream Deck.

### System Requirements
- Windows 10 or Windows 11
- Stream Deck Software 6.0+
- **.NET 8.0 Runtime** (The plugin is self-contained if downloaded from releases, but runtime is recommended)

## Development

### Prerequisites
- .NET 8.0 SDK
- PowerShell 7 (recommended) or Windows PowerShell 5.1

### Getting Started

This project uses a PowerShell script `dev.ps1` to automate development tasks.

1. **Setup Environment**:
   ```powershell
   .\dev.ps1 install
   # This will check for .NET SDK, configure NuGet sources, and restore packages.
   ```

2. **Build**:
   ```powershell
   .\dev.ps1 build
   ```

3. **Deploy to Stream Deck** (Requires Stream Deck to be installed):
   ```powershell
   .\dev.ps1 full
   # This performs: Clean -> Build (Release) -> Deploy -> Restart Stream Deck
   ```

### Development Commands

| Command | Description |
|---------|-------------|
| `.\dev.ps1 install` | Setup development environment (check SDK, sources). |
| `.\dev.ps1 build` | Build the plugin (Debug configuration). |
| `.\dev.ps1 clean` | Clean build artifacts. |
| `.\dev.ps1 test` | Run unit tests. |
| `.\dev.ps1 watch` | Watch for file changes and rebuild automatically. |
| `.\dev.ps1 deploy` | Build (Release) and deploy to Stream Deck. |
| `.\dev.ps1 full` | Full pipeline: Clean -> Stop SD -> Build -> Deploy -> Start SD. |
| `.\dev.ps1 restart` | Restart the Stream Deck application. |

### Project Structure (Default)
This project follows a standard Stream Deck C# plugin structure:

```
streamdeck-pin-to-deck/
├── src/PinToDeck/          # Main plugin source code
│   ├── Actions/            # Stream Deck Actions implementation
│   ├── Core/               # Core logic (WindowManager, AppLauncher)
│   ├── Models/             # Data models
│   ├── PropertyInspector/  # Frontend (HTML/JS/CSS)
│   └── manifest.json       # Plugin Manifest
├── tests/PinToDeck.Tests/  # Unit tests (xUnit)
└── dev.ps1                 # Development automation script
```

## Tech Stack

- **Language**: C#
- **Framework**: .NET 8.0-windows
- **SDK**: [BarRaider StreamDeck-Tools](https://github.com/BarRaider/streamdeck-tools)
- **Shell API**: WindowsAPICodePackShell
- **Testing**: xUnit

## Troubleshooting

### Build Failed: File in Use
If Stream Deck is running, it might lock the plugin files.
Use `.\dev.ps1 restart` to cycle Stream Deck, or ensure it's closed before deploying.

### Property Inspector Not Updating
If you modify `pi.html` or `pi.js` and don't see changes:
1. Ensure you used `.\dev.ps1 deploy` or `.\dev.ps1 full` (files are only copied on deploy).
2. Restart Stream Deck.

## License

MIT License

## Author

applepig (https://github.com/applepig)
