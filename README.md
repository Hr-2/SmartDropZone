# Smart Drop Zone

A lightweight Windows desktop shelf that slides out from the edge of your screen to hold files, folders, links and text snippets for drag-and-drop reuse.

> Just want the app? Grab the latest version from the **[Releases](https://github.com/Hr-2/SmartDropZone/releases)** page — download the zip, unzip it, and run `SmartDropZone.exe`. No install needed.

---

## ✨ Features

- **Slide-out shelf** docked to the **right**, **left**, or **top** edge — or **free-floating** anywhere you want
- **Drop anything on it** — files, folders, links and text snippets get pinned as cards
- **Drag cards back out** to send them to any app (Explorer, browser, chat, etc.)
- **Smart auto-hide** — it slides away when idle, pops back open when you hover, or you can pin it always-open
- **Hold gestures** — pull the shelf out of its dock and hold to detach it to free mode; hold it against another edge to switch docks
- **Explorer-style** sort (name / type / date added) and views (list / icons)
- **Fully resizable** with a visible corner handle; move it by dragging the top bar
- **Tray icon** with quick controls (show/hide, docks, settings, exit)
- Quiet in the background — no window title, no taskbar entry

## 🖱️ How to use it

| What | How |
| --- | --- |
| Pin an item | Drag a file / folder / link / text onto the shelf |
| Send an item | Drag a card off the shelf into any window |
| Open the shelf | Hover the edge handle or the floating pill |
| Move it | Drag the top bar |
| Resize it | Drag the corner handle |
| Dock it | Drag to the right / left / top edge and hold |
| Detach it | Pull it out into the middle of the screen and hold |
| Settings | Click the gear icon, or right-click the shelf |

## 📋 Requirements

- Windows 10 or 11
- .NET 6 Desktop Runtime — already present on most Windows installs. If a dialog asks for it, get it from [Microsoft's .NET download page](https://dotnet.microsoft.com/download/dotnet/6.0).

## 🛠️ Building from source

You'll need the **.NET 6 SDK** (not just the runtime).

```bash
dotnet build SmartDropZone.csproj -c Release
```

The app is produced at `bin/Release/net6.0-windows/SmartDropZone.exe`.

## 📦 Releases

Every push to `main` automatically builds the app in the cloud and publishes a downloadable **Release** — so you always have the latest version ready without building anything yourself.

- New versions are numbered simply: **v1.0.1**, **v1.0.2**, **v1.0.3**, ...
- See **[CHANGELOG.md](CHANGELOG.md)** for what changed in each version.

## 📄 License

This is a personal project — use it freely, modify it, and enjoy it.
