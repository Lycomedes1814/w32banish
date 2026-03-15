# CursorHider

Hides the mouse cursor while you type and restores it when you move the mouse. Runs as a Windows system tray app.

## How it works

- Global low-level keyboard hook detects any keypress and hides the cursor
- Global low-level mouse hook detects real mouse movement and restores it
- Uses `MagShowSystemCursor` (Windows Magnification API) to hide the cursor at the DWM compositor level — works across all windows including WSLg (WSL2 GUI apps)

## Build

Requires .NET 8 SDK.

```
dotnet build
dotnet run
```

Or publish a self-contained exe:

```
dotnet publish -c Release -r win-x64 --self-contained
```

## Usage

Run `CursorHider.exe`. It appears in the system tray. Right-click the tray icon and choose **Exit** to quit.
