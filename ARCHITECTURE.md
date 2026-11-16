# Again System Architecture

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-0078D4.svg)](https://github.com/kiranmayee-abbireddy/Again)
[![Maintainer](https://img.shields.io/badge/Maintainer-%40kiranmayee--abbireddy-22D3EE.svg)](https://github.com/kiranmayee-abbireddy)

**Again** is a deterministic desktop workspace restorer for Windows that snapshots and restores running applications, exact window coordinates, and browser tabs in under 600ms without spawning duplicate processes.

---

## 1. High-Level Architecture

Again is structured into a clean layered architecture with decoupled Core services, Win32 P/Invoke interop, and a lightweight WPF presentation shell.

```
+-------------------------------------------------------------+
|                   Again.App (Presentation)                  |
|    - SystemTrayManager       - MainWindow / Workspace UI    |
|    - HotkeyListener          - Modern WPF TealDark Theme    |
+-------------------------------------------------------------+
                              |
+-------------------------------------------------------------+
|                   Again.Core (Domain & Engine)               |
|  - WorkspaceCaptureService    - WorkspaceRestoreService     |
|  - WindowsProcessService      - ChromiumBrowserService      |
|  - WindowsMonitorService      - ApplicationCatalogService   |
|  - WindowsExplorerService     - WindowsShortcutService      |
+-------------------------------------------------------------+
                              |
+-------------------------------------------------------------+
|              Storage & Platform Interop Layer               |
|  - SqliteWorkspaceRepository  - ConfigurationRepository     |
|  - P/Invoke: user32.dll       - P/Invoke: dwmapi.dll        |
|  - P/Invoke: kernel32.dll     - UIAutomationClient          |
+-------------------------------------------------------------+
```

---

## 2. Core Subsystems & Services

| Service | Responsibility | Win32 / API Implementation |
| :--- | :--- | :--- |
| `WorkspaceCaptureService` | Orchestrates instantaneous desktop topology snapshot. | Coordinates `EnumWindows` and browser queries. |
| `WorkspaceRestoreService` | Concurrently applies window placements and deduplicates. | Win32 `SetWindowPlacement` & process reuse. |
| `WindowsProcessService` | Identifies active PIDs and process executable paths. | `QueryFullProcessImageName`, `OpenProcess`. |
| `WindowsWindowService` | Enumerates and filters top-level desktop windows. | `EnumWindows`, `GetWindowPlacement`, `IsWindowVisible`. |
| `ChromiumBrowserService` | Queries and injects tabs into Chrome / Edge windows. | Windows UI Automation (`IUIAutomation`), IPC arguments. |
| `WindowsMonitorService` | Clamps coordinates across multi-monitor setups. | `GetDpiForMonitor`, `EnumDisplayMonitors`, PerMonitorV2. |
| `ApplicationCatalogService` | Discovers and stages unlaunched/offline applications. | Start Menu, Registry, and PATH scanning. |
| `WindowsExplorerService` | Restores Explorer folders and navigation paths. | Shell Windows COM interface (`IShellWindows`). |
| `WindowsShortcutService` | Manages global hotkey hooks (`Ctrl+Alt+C`, `Ctrl+Alt+R`). | `RegisterHotKey`, `UnregisterHotKey`, `WM_HOTKEY`. |
| `SqliteWorkspaceRepository` | Stores local offline workspace manifests. | SQLite database + JSON export in `%LOCALAPPDATA%`. |

---

## 3. The 3-Stage Restoration Pipeline

```
  [01: Capture] (15ms)
        |
        v
  Query EnumWindows + GetWindowPlacement + Chromium UI Automation
        |
        v
  [02: Reconcile & Deduplicate] (30ms)
        |
        +---> Running Browser Instance? ---> Attach missing URL into existing window
        |
        +---> Running Application PID?  ---> Bring existing window to focus
        |
        +---> Disconnected Display?     ---> Clamp bounds into visible PerMonitorV2 DPI
        |
        +---> Offline Staged Tool?      ---> Queue executable for cold start
        |
        v
  [03: Restore Execution] (< 600ms)
        |
        v
  Concurrent SetWindowPlacement + Cold Start Launches + State Verified
```

### Stage 1: Fast Capture (< 15ms)
- `EnumWindows` walks the top-level window hierarchy.
- Cloaked windows (Windows 10/11 virtual desktop artifacts) are filtered using `DwmGetWindowAttribute(DWMWA_CLOAKED)`.
- Active placement is captured via `GetWindowPlacement` (`WPF_RESTORETOMAXIMIZED`, normal position rect, showCmd).
- For Chromium processes (`chrome.exe`, `msedge.exe`), UI Automation reads tab URL collections.

### Stage 2: Process Deduplication & Tab Attachment
- Traditional window managers open a new instance for every captured window, resulting in duplicate processes and fragmented memory.
- Again checks `WindowsProcessService`:
  - If `chrome.exe` is already open, it queries open tab URLs.
  - Missing URLs are injected directly into the running instance.
  - Running IDEs (VS Code, Cursor, Visual Studio) are focused and repositioned to target coordinates, preventing file lock collisions.
  - Closed applications configured in the **Offline Catalog** are queued for execution.

### Stage 3: Multi-Monitor Coordinate Clamping
- When an external monitor is disconnected, windows saved at secondary coordinates (`X: 1920`, `Y: 0`) would otherwise spawn off-screen.
- `WindowsMonitorService` intercepts target bounds against `EnumDisplayMonitors`:
  - Coordinates are clamped into visible screen coordinates.
  - DPI scaling adjusts automatically via `PerMonitorV2` awareness.

---

## 4. Local Storage Schema

All workspaces are stored 100% locally with zero cloud dependencies or telemetry:

- **Location:** `%LOCALAPPDATA%\Again\workspaces\`
- **Database:** SQLite (`workspaces.db`) with direct JSON import/export
- **Sample Schema:**
```json
{
  "id": "c7a8e9d0-1234-5678-90ab-cdef12345678",
  "name": "Engineering Sprint",
  "createdAt": "2026-11-16T20:30:00Z",
  "windows": [
    {
      "processName": "chrome",
      "executablePath": "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
      "windowTitle": "GitHub: Pull Requests and Issues",
      "displayIndex": 0,
      "bounds": { "x": 0, "y": 0, "width": 1920, "height": 540 },
      "isMaximized": false,
      "tabs": ["https://github.com/kiranmayee-abbireddy/Again"]
    },
    {
      "processName": "code",
      "executablePath": "C:\\Users\\Developer\\AppData\\Local\\Programs\\Microsoft VS Code\\Code.exe",
      "windowTitle": "workspace.ts: frontend runtime",
      "displayIndex": 0,
      "bounds": { "x": 0, "y": 540, "width": 960, "height": 540 },
      "isMaximized": false
    }
  ]
}
```

---

## 5. Security & Privacy

- **Zero Network Traffic:** Again performs no network calls, telemetry collection, or remote logging.
- **Local Credential Safety:** Browser tab capture only records top-level URLs; no session cookies, passwords, or POST body payloads are ever inspected or stored.
- **Principle of Least Privilege:** Operates in standard user mode without requiring Windows Administrator elevation.

---

## 6. Repository & Contributions

- **Repository:** [https://github.com/kiranmayee-abbireddy/Again](https://github.com/kiranmayee-abbireddy/Again)
- **Maintainer:** [@kiranmayee-abbireddy](https://github.com/kiranmayee-abbireddy)
- **License:** MIT License
