# Again — Native Windows Desktop Workspace Orchestrator

> **Tagline:** *Your workspace, back on stage.*  
> **Platform:** Windows 10 and Windows 11 (x64)  
> **Framework:** .NET 10 / C# / WPF  
> **Architecture:** MVVM, Modular Services, Local-First  
> **Storage:** SQLite + Local JSON Configuration  
> **Repository:** [https://github.com/kiranmayee-abbireddy/Again.git](https://github.com/kiranmayee-abbireddy/Again.git)  
> **Showcase & Branding Page:** [https://kiranmayee-abbireddy.github.io/Again/](https://kiranmayee-abbireddy.github.io/Again/)  
> **Distribution:** Standalone Executable & Windows Installer (Inno Setup)

---

## 1. Overview

**Again** is a native Windows desktop application that captures, organizes, and restores complete desktop workspaces with a single click or keyboard shortcut.

Instead of manually launching multiple applications and arranging windows across multiple monitors every time you switch contexts (e.g. from Development to Meetings to Learning), **Again** snapshots your entire multi-window desktop arrangement and restores your entire flow on demand.

---

## 2. Key Features

- **Desktop Workspace Capture:** Automatically enumerates visible top-level windows, extracting process identities, executable paths, window coordinates $(X, Y, W, H)$, window states (Normal, Minimized, Maximized), and monitor assignments.
- **Intelligent Restoration Engine:**
  - **No Duplicate Spawning:** Reuses and repositions currently running application windows without launching duplicate instances.
  - **Smart Browser Tab Sync:** If Google Chrome, Microsoft Edge, Brave, or Mozilla Firefox is already running, active tabs are inspected and only missing URLs are opened into the current browser process before repositioning.
  - **Order & Delay Execution:** Launches missing apps with configurable order and startup delays.
  - **Window Readiness Polling:** Robust polling with timeout and cancellation tokens for window readiness.
  - **DPI-Calibrated Coordinates:** Restores dimensions and coordinates with PerMonitorV2 DPI awareness.
  - **Safe Multi-Monitor Fallbacks:** Automatically clamps coordinates to visible work areas when monitor configurations change or displays disconnect.
- **Offline Application Staging:**
  - Add and configure applications that are not currently running during workspace creation or stage customization.
  - Discover installed software via Start Menu shortcuts, system tools, and Registry App Paths, or browse executables directly.
  - Configure target window states (Normal, Maximized, Minimized) and test launch staging.
- **Workspace Library:** Manage workspaces with categorization (Development, Meetings, Learning, Design, Personal, Custom), search filtering, pin-to-top, duplication, and deletion.
- **System Tray Integration:** Runs quietly in the background; minimize-to-tray on close; tray icon with quick actions and dynamic sub-menu for pinned workspaces.
- **Global Keyboard Shortcuts:**
  - `Ctrl + Alt + C`: Instant Workspace Capture
  - `Ctrl + Alt + R`: Quick Restore Last Used Workspace
- **Restoration History & Diagnostics:** Full audit log of restoration sessions, execution durations, individual application statuses, error messages, and one-click "Retry Failed" actions.
- **Modern Adaptive Theme Suite:**
  - 1-Click theme gallery including **Nordic Teal (Dark)**, **Cyberpunk Slate**, **Emerald Forest**, **Crimson Dawn**, and **Light Mode** with high-contrast text and crisp typography.
  - Clean display device labels (e.g., `Display 1`, `Display 2`) without cryptic Win32 device namespaces.

---

## 3. Technology Stack & Architecture

| Layer | Component / Technology |
|---|---|
| **Platform** | Windows 10 & Windows 11 (x64) |
| **Framework** | .NET 10 (`net10.0-windows`) |
| **UI Framework** | Windows Presentation Foundation (WPF) |
| **Architecture** | Model-View-ViewModel (MVVM) |
| **MVVM Toolkit** | `CommunityToolkit.Mvvm` |
| **Database** | SQLite via `Microsoft.Data.Sqlite` (`%LocalAppData%\Again\again.db`) |
| **Configuration** | Atomic JSON Serialization (`%LocalAppData%\Again\settings.json`) |
| **Dependency Injection** | `Microsoft.Extensions.DependencyInjection` |
| **Logging** | `Microsoft.Extensions.Logging` |
| **Native Interop** | Win32 / User32 / Kernel32 / Dwmapi / Shcore P/Invoke APIs |
| **Installer** | Inno Setup 6 (`Again-Setup-v1.0.0.exe`) |
| **Testing** | xUnit (`Again.Tests`) |

---

## 4. Project Layout

```
d:\projects\Again\ (or workspace root)
├── Again.slnx                        # Modern .NET 10 Solution
├── src\
│   ├── Again.Core\                   # Core Domain, Native Win32, SQLite Storage & Services
│   │   ├── Domain\                   # Workspace, Application, Placement, Session, Settings
│   │   ├── Native\                   # P/Invoke declarations, structs, DWM cloaking, DPI
│   │   ├── Storage\                  # SQLite Repository, Configuration Repository
│   │   └── Services\                 # Capture, Restore, Process, Window, Monitor, Shortcuts
│   └── Again.App\                    # WPF Desktop Application
│       ├── Assets\                   # Icons & Brand graphics (app.ico, icon.png)
│       ├── Converters\               # WPF Value Converters
│       ├── Services\                 # SystemTrayManager (Forms.NotifyIcon integration)
│       ├── Styles\                   # DarkTheme.xaml, LightTheme.xaml, CommonStyles.xaml
│       ├── ViewModels\               # Main, Dashboard, Library, Details, Capture, Apps, History, Settings
│       └── Views\                    # UserControls for all navigation pages
├── tests\
│   └── Again.Tests\                  # xUnit Test Suite for domain, storage, and services
├── dist\
│   ├── standalone\                   # Standalone release binaries (Again.exe)
│   └── installer\                    # Inno Setup Windows installer (Again-Setup-v1.0.0.exe)
└── installer\
    └── again.iss                     # Inno Setup compilation script
```

---

## 5. Building and Running

### Prerequisites
- Windows 10 (Build 19041+) or Windows 11
- .NET 10 SDK

### Run Unit Tests
```powershell
dotnet test Again.slnx
```

### Build & Run from Source
```powershell
dotnet run --project src/Again.App/Again.App.csproj
```

### Build Standalone Executable
```powershell
dotnet publish src/Again.App/Again.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist/standalone
```

### Compile Windows Installer
```powershell
& "C:\Users\LENOVO\AppData\Local\Programs\Inno Setup 6\ISCC.exe" "installer/again.iss"
```
Output: `dist/installer/Again-Setup-v1.0.0.exe` (installs `Again.exe`)

---

## 6. License

Distributed under the MIT License. See [LICENSE](LICENSE) for details.
