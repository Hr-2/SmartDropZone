# Smart Drop Zone

A lightweight Windows desktop shelf that slides out from the edge of your screen to hold files, folders, links and text snippets for drag-and-drop reuse.

## Features

- Slide-out shelf docked to the **right / left / top** edge, or **free-floating** anywhere
- Drop files, folders, URLs and text onto the shelf to pin them
- Drag cards back out to send them to any app
- Hover to open, auto-collapse when idle, or pin always-open
- **Hold gestures**: hold the shelf out of its dock to detach to free mode, or hold it against another edge to switch docks
- Sort (name / type / date added) and view modes (list / icons), Explorer-style
- Resize with the visible corner handle; move by dragging the top bar
- Tray icon with quick controls
- Runs in the notification area; no window title or taskbar entry

## Requirements

- Windows 10 / 11
- .NET 6 Desktop Runtime (bundled with most Windows installs, otherwise available from Microsoft)

## Building

```bash
dotnet build SmartDropZone.csproj -c Release
```

The executable is produced at `bin/Release/net6.0-windows/SmartDropZone.exe`.

## Downloads

Ready-built releases (including a zip with the .exe) are published automatically on the **Releases** page of this repository.
