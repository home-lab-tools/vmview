# VmView — Hyper-V console browser

One window, two levels, like a phone app: a **wall** of running VMs — live previews in a fixed
columns × rows grid, like a bank of security cameras — and one level down the **console** of the VM you opened — its own monitor live, auto-scaled or
fullscreen. Closing the window parks the app in the tray with that state intact; opening it again brings
you back to the same page, reconnected. View-only by default: the input gate is shut and no key, click or
mouse move reaches a VM until you open it on that console — an off-by-default opt-in that shuts whenever
the session drops.

.NET 10 · Avalonia 11 (Fluent, light) · FreeRDP 3 behind a native shim. Ships as one self-contained exe,
lives in the tray, and can start with Windows.

## How it shows the screen

| Surface | Source | Why |
|---|---|---|
| Wall tiles | `GetVirtualSystemThumbnailImage` (WMI, `rootirtualization2`) at ~1 fps, at the tile's own pixel size | read-only host API, no session |
| Console page | Hyper-V console service, TCP 2179, VM id as preconnection blob — the Basic Session VMConnect opens — decoded by FreeRDP inside `native\vmrdp.dll` | event-driven frames at the VM's own rate (~60 fps seen); guest needs no RDP, no network, no integration services; BIOS/boot visible |

`vmrdp.dll` (`native\vmrdp.c`) exports `vmrdp_open/close`, a frame callback, and — behind a gate that starts
shut — `vmrdp_set_input` / `vmrdp_mouse` / `vmrdp_key`. While the gate is shut the shim drops every input call,
so a fresh session is a picture. Sign-on is the current user through the native SSPI (FreeRDP is handed no
identity), exactly like VMConnect: run the app as a Hyper-V administrator. A session is up ~35 ms after
`vmrdp_open`, first frame within ~100 ms.

## Using it

- **Wall:** only running VMs (those with a screen) are shown, one tile each; a click on a tile (or Enter for
  the first) opens its console. The two dropdowns at the top set the grid, 2–8 across × 2–8 down; the wall
  itself keeps its size, tiles rescale to the cell, the picture keeps its aspect over a translucent light
  wash, and cells with no VM are simply empty. Previews are requested from the host at the tile's pixel width.
- **Console:** the app bar has Back, the VM's name and a LIVE chip; on the right the input gate (keyboard
  icon), zoom **Fit** / **1:1** / **2×**, and Fullscreen. **Esc** or **Alt+←** go back; **F11** or a
  double-click on the stage toggle fullscreen, **Esc** leaves fullscreen first and goes back on the second press.
- **Input gate:** off by default; the button turns red and a red "INPUT ON" badge shows while it is open.
  Absolute mouse, wheel and set-1 scancodes; it shuts whenever the session drops.
- The console follows its VM: when it goes off the page says so; when it comes back it reconnects on its own
  (every 3 s); a refused logon stops retrying until you press **Reconnect**.
- Below ~840 DIPs the app bars drop their secondary labels (tile resolution, button captions, guest OS, fps);
  the controls never move or overlap.

## Tray, close, autostart

- **Closing the window does not quit.** It hides into the tray and suspends everything the host could
  notice: the console session is dropped (no TCP 2179 connection stays open), inventory polling and card
  previews stop. The page stack, the opened VM, zoom — all stay. Click the tray icon or pick
  **Open VM Browser** to come back: the same page shows and the console reconnects at once.
- The tray icon shows a green dot while a console is streaming; plain while idle or parked.
- **Start with Windows** (tray menu) registers a Task Scheduler logon task `VmView` for the current user,
  `RunLevel=Highest`, no execution time limit, running `VmView.exe --tray`. A Run-key entry would not do:
  the manifest asks for the highest available privilege and Windows silently skips Run entries that need
  elevation. The check mark reflects whether the task exists **and points at this exe** — move the exe
  and re-tick it.
- `--tray` starts parked, with no window. One instance per session: launching the exe again just brings
  the resident window up (a second `--tray` launch stays quiet).
- **Exit** is in the tray menu only.

## Build

```powershell
.\native\build.ps1      # once: vcpkg builds FreeRDP 3 (minutes with the binary cache), then cmake builds vmrdp.dll
dotnet build -c Release  # framework-dependent, for development
.\publish.ps1            # one self-contained exe -> dist\VmView.exe (~50 MB)
```

`native\build.ps1` uses the vcpkg bundled with Visual Studio in manifest mode (`native\vcpkg.json`).
`publish.ps1` runs `dotnet publish` with the single-file settings from the csproj (`win-x64`, self-contained,
compressed). The managed side runs from inside the exe; vmrdp.dll and the FreeRDP/OpenSSL DLLs are native, so
the host extracts them once to `%TEMP%\.net\VmView\<hash>\` and loads them from there. `vmview.json` and the
autostart task are keyed on the exe's real location (`Environment.ProcessPath`), not that folder.

## Release

`.github/workflows/release.yml` builds the same exe on every push and PR (`windows-latest`, VS 2022, FreeRDP
from vcpkg cached on the manifest hash) and uploads it as a workflow artifact. Pushing a `v*` tag publishes a
GitHub Release with `VmView.exe` and its SHA-256, the tag stamped into the exe's file version:

```powershell
git tag v0.1.0
git push origin v0.1.0
```

Icons: `tools\make-icon.py` (Pillow) draws `Assets\vmview.ico` and `Assets\vmview-live.ico` — window, tray
and exe icon — and drops previews into `tools\icon-preview\`.

## Configuration — `vmview.json` beside the exe (optional)

```json
{ "Hosts": ["."], "TileFps": 1, "TileWidth": 320, "InventorySeconds": 2 }
```

## Layout

```
Program.cs                    single-instance gate, --tray, Avalonia bootstrap
App.axaml(.cs)                light palette; the one window shown/hidden, tray icon + menu, exit
Assets/                       vmview.ico, vmview-live.ico (tools/make-icon.py)
Styles/Theme.axaml            app bar, cards, pills, segmented control, buttons, narrow-window rules
Models/                       VmSummary (WMI shape), Options
Services/HyperVInventory      VM list + counters, one WMI call per host
Services/VmCatalog            every VM as a long-lived VmItem, polled on a timer; Polling / PreviewsEnabled switches
Services/ThumbnailSource      card preview thread (RGB565 → FrameBuffer)
Services/ConsoleClient        P/Invoke to vmrdp, status → observable state
Services/Autostart            "Start with Windows" as a Task Scheduler logon task (COM, RunLevel=Highest)
Services/SingleInstance       named mutex + event: second launch wakes the resident window
Rendering/FrameBuffer         any-thread BGRA sink → WriteableBitmap on the UI thread
Controls/ScreenView           paints a FrameBuffer: Fit / 1:1 / 2×; hit-testable only while InputEnabled
Controls/Scancodes            physical key → PC/AT set-1 scancode
Controls/SlideTransition      page slide whose direction follows the navigation (deeper / back)
ViewModels/ShellViewModel     the page stack (List → Console), Suspend/Resume for the tray, IsLive, Title
ViewModels/ListPageViewModel  the wall: running VMs + blank cells for a columns × rows grid, PreviewWidth
ViewModels/ConsolePageViewModel  one VM's session: connect/retry, zoom, fullscreen, input gate, Back
ViewModels/VmItem             a VM: summary + preview worker (PreviewsEnabled)
Views/ShellWindow             the window: key bindings, TransitioningContentControl over the pages, fullscreen
Views/ListPage, ConsolePage   the two pages (UserControls with their own app bars); ListPage pushes the tile size to the model
native/                       vmrdp.c, CMake, vcpkg manifest, build.ps1
publish.ps1                   single-file publish -> dist\VmView.exe
```

## Notes learned the hard way

- The thumbnail API returns 4 header bytes then RGB565 rows; asking for more than the VM's current
  resolution (`Msvm_VideoHead`) fails with 32775.
- FreeRDP's core leaves `WSAStartup` to the client; without it every connect fails with "DNS host name not found".
- **Connect took 5 s flat** until `FreeRDP_NetworkAutoDetect` was switched off: the client advertises
  connect-time network auto-detect, vmms never answers it, and FreeRDP sits in
  `CONNECTION_STATE_CONNECT_TIME_AUTO_DETECT_REQUEST` until its 5 s timeout. Without it the handshake
  completes in ~35 ms.
- A black stage *and* a black card means the guest switched its monitor off (power settings), not a viewer fault.
- Reflection bindings cannot cast (`((vm:Type)DataContext)`): bind `$parent[ListBox].DataContext.Command` instead.
- mstscax (VMConnect's ActiveX) was tried and dropped: its input window lives on its own thread and its zoom
  cannot be changed after connecting. A real display-only client was the only clean answer.
