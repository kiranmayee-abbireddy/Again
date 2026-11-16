# Encore — Native Windows Desktop Workspace Manager

> **Tagline:** *Your workspace, back on stage.*  
> **Platform:** Windows 10 and Windows 11 (x64)  
> **Framework:** .NET 10 / C# / WPF  
> **Architecture:** MVVM, Modular Services, Local-First  
> **Storage:** SQLite + Local JSON Configuration  
> **Distribution:** Standalone Executable & Windows Installer (Inno Setup)

---

## 1. Overview

**Encore** is a native Windows desktop application that captures, organizes, and restores complete desktop workspaces with a single click or keyboard shortcut.

Instead of manually launching multiple applications and arranging windows across multiple monitors every time you switch contexts (e.g. from Development to Meetings to Learning), Encore snapshots your entire desktop layout and restores it on demand.

---

## 2. Key Features

- **Desktop Workspace Capture:** Automatically enumerates visible top-level windows, extracting process identities, executable paths, window coordinates $(X, Y, W, H)$, window states (Normal, Minimized, Maximized), and monitor assignments.
- **Workspace Library:** Manage workspaces with categorization (Development, Meetings, Learning, Design, Personal, Custom), search filtering, pin-to-top, duplication, and deletion.
- **Reliable Workspace Restoration:**
  - Validates executable paths on disk.
  - Detects running vs missing applications.
  - Launches missing apps with configurable order and startup delays.
  - Robust polling with timeout and cancellation tokens for window readiness.
  - Restores window dimensions, states, and coordinates with PerMonitorV2 DPI awareness.
  - Safe fallbacks for disconnected or changed display configurations so windows never render off-screen.
- **System Tray Integration:** Runs quietly in the background; minimize-to-tray on close; tray icon with quick actions and a dynamic sub-menu for pinned workspaces.
- **Global Keyboard Shortcuts:**
  - `Ctrl + Alt + C`: Instant Workspace Capture
  - `Ctrl + Alt + R`: Quick Restore Last Used Workspace
  - `Ctrl + Alt + E`: Toggle Show / Hide Encore Dashboard
- **Restoration History & Diagnostics:** Full audit log of restoration sessions, execution durations, individual application statuses, error messages, and one-click "Retry Failed" actions.
- **Modern Windows Desktop Aesthetic:** Dark and Light themes with Fluent-inspired design, rounded surfaces, smooth navigation, and crisp typography.

---

## 3. Technology Stack & Architecture

| Layer | Component / Technology |
|---|---|
| **Platform** | Windows 10 & Windows 11 (x64) |
| **Framework** | .NET 10 (`net10.0-windows`) |
| **UI Framework** | Windows Presentation Foundation (WPF) |
| **Architecture** | Model-View-ViewModel (MVVM) |
| **MVVM Toolkit** | `CommunityToolkit.Mvvm` |
| **Database** | SQLite via `Microsoft.Data.Sqlite` (`%LocalAppData%\Encore\encore.db`) |
| **Configuration** | Atomic JSON Serialization (`%LocalAppData%\Encore\settings.json`) |
| **Dependency Injection** | `Microsoft.Extensions.DependencyInjection` |
| **Logging** | `Microsoft.Extensions.Logging` |
| **Native Interop** | Win32 / User32 / Kernel32 / Dwmapi / Shcore P/Invoke APIs |
| **Installer** | Inno Setup 6 (`Encore-Setup-v1.0.0.exe`) |
| **Testing** | xUnit (`Encore.Tests`) |

---

## 4. Project Layout

```
d:\projects\Encore\
├── Encore.slnx                       # Modern .NET 10 Solution
├── src\
│   ├── Encore.Core\                  # Core Domain, Native Win32, SQLite Storage & Services
│   │   ├── Domain\                   # Workspace, Application, Placement, Session, Settings
│   │   ├── Native\                   # P/Invoke declarations, structs, DWM cloaking, DPI
│   │   ├── Storage\                  # SQLite Repository, Configuration Repository
│   │   └── Services\                 # Capture, Restore, Process, Window, Monitor, Shortcuts
│   └── Encore.App\                   # WPF Desktop Application
│       ├── Assets\                   # Icons & Brand graphics (app.ico, icon.png)
│       ├── Converters\               # WPF Value Converters
│       ├── Services\                 # SystemTrayManager (Forms.NotifyIcon integration)
│       ├── Styles\                   # DarkTheme.xaml, LightTheme.xaml, CommonStyles.xaml
│       ├── ViewModels\               # Main, Dashboard, Library, Details, Capture, Apps, History, Settings
│       └── Views\                    # UserControls for all navigation pages
├── tests\
│   └── Encore.Tests\                 # xUnit Test Suite for domain, storage, and services
├── dist\
│   ├── standalone\                   # Standalone release executable (Again.exe)
│   └── installer\                    # Inno Setup Windows installer (Again-Setup-v1.0.0.exe)
└── installer\
    └── encore.iss                    # Inno Setup compilation script
```

---

## 5. Building and Running

### Prerequisites
- Windows 10 (Build 19041+) or Windows 11
- .NET 10 SDK (installed at `C:\Users\LENOVO\.dotnet` or in system PATH)

### Run Unit Tests
```powershell
dotnet test d:\projects\Encore\Encore.slnx
```

### Build & Run from Source
```powershell
dotnet run --project d:\projects\Encore\src\Encore.App\Encore.App.csproj
```

### Build Standalone Executable
```powershell
dotnet publish d:\projects\Encore\src\Encore.App\Encore.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o d:\projects\Encore\dist\standalone
```

### Compile Windows Installer
```powershell
& "C:\Users\LENOVO\AppData\Local\Programs\Inno Setup 6\ISCC.exe" "d:\projects\Encore\installer\encore.iss"
```
Output: `d:\projects\Encore\dist\installer\Again-Setup-v1.0.0.exe` (installs `Again.exe`)
