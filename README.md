# Smart Drop Zone

A small Windows app that adds a shelf on the edge of your screen. You can drop files, folders, links and text onto it to keep them handy, then drag them back out to use them anywhere.

If you just want the app, grab the latest build from the [Releases](https://github.com/Hr-2/SmartDropZone/releases) page, unzip it, and run `SmartDropZone.exe`.

## Features

- Slide-out shelf that docks to the right, left or top edge, or floats free anywhere on screen
- Drop files, folders, links or text onto it to pin them as cards
- Drag cards back out to send them to any app
- Slides away when idle, opens when you hover it, or stays open if you pin it
- Hold a docked shelf out of its edge to detach it, or hold it against another edge to switch docks
- Sort by name, type or date added, with list and icon views
- Resize with the corner handle, move it by dragging the top bar
- Tray icon with quick options
- No title bar, no taskbar entry, stays out of the way

## How to use it

- Pin an item: drag a file, folder, link or text onto the shelf
- Send an item: drag a card off the shelf into any window
- Open the shelf: hover the edge handle or the floating pill
- Move it: drag the top bar
- Resize it: drag the corner handle
- Dock it: drag it to the right, left or top edge and hold
- Detach it: pull it out into the middle of the screen and hold
- Settings: click the gear icon, or right-click the shelf

## Requirements

- Windows 10 or 11
- .NET 6 Desktop Runtime (usually already installed; if prompted, grab it from [Microsoft](https://dotnet.microsoft.com/download/dotnet/6.0))

## Building

You need the .NET 6 SDK.

```
dotnet build SmartDropZone.csproj -c Release
```

The app is built to `bin/Release/net6.0-windows/SmartDropZone.exe`.

## Releases

Every push to `main` builds the app automatically and updates the "Latest build" release, so there is always a fresh download ready. The release notes list what changed since the last build. The full history is in [CHANGELOG.md](CHANGELOG.md).

## AI assistance

I built this with the help of an AI coding assistant (opencode). I handled the design, direction and testing, and the AI helped me write and debug the code. I am stating this openly so nobody has to ask.

## License

This is a personal project. Use it freely.
