# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.0] - 2026-01-18

### Added
- **Pin Applications**: Support for pinning both Win32 and UWP applications via `shell:AppsFolder`.
- **Status Display**: 
  - Grayscale icon for non-running apps.
  - Color icon for running apps.
  - Number badge for multiple open windows.
- **Quick Launch**: Launch application or bring window to foreground with a short press.
- **Window Cycle**: Cycle through multiple windows of the same app with repeated short presses (Z-Order).
- **Close Window**: Long press to close the current foreground window.
- **Property Inspector**:
  - App selector dropdown with search.
  - Live preview of app selection.
- **Development Tools**:
  - `dev.ps1` script for automated build, test, and deployment.
  - `install` command for one-step environment setup.
  - GitHub Actions CI workflow (planned).

### Technical
- Built with .NET 8.0 (Windows).
- Uses BarRaider StreamDeck-Tools SDK.
- Win32 API integration via P/Invoke for window management.
- xUnit test suite for core logic validation.
