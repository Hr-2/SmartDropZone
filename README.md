# Smart Drop Zone

A lightweight Windows desktop shelf that slides out from the edge of your screen to hold files, folders, links and text snippets for drag-and-drop reuse.

> Just want the app? Grab the newest version from the **[Releases](https://github.com/Hr-2/SmartDropZone/releases)** page — download the newest zip (e.g. `1.0.3`), unzip it, and run `SmartDropZone.exe`. No install needed.

---

## ✨ Features

- **Slide-out shelf** docked to the **right**, **left**, or **top** edge, or **free-floating** anywhere you want
- **Drop anything on it** and it gets pinned as a card
- **Drag cards back out** to send them to any app (Explorer, browser, chat, etc.)
- **Smart auto-hide** that slides it away when idle and pops it open on hover, or pin it always-open
- **Hold gestures** to detach a docked shelf into free mode, or switch it to another edge
- **Explorer-style** sorting (name / type / date added) and views (list / icons)
- **Fully resizable** with a visible corner handle, and movable by dragging the top bar
- **Tray icon** with quick controls (show/hide, docks, settings, exit)
- Quiet in the background, with no title bar or taskbar entry

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
- .NET 6 Desktop Runtime (usually already installed; if prompted, get it from [Microsoft](https://dotnet.microsoft.com/download/dotnet/6.0))

## 🛠️ Building from source

You'll need the **.NET 6 SDK** (not just the runtime).

```bash
dotnet build SmartDropZone.csproj -c Release
```

The app is produced at `bin/Release/net6.0-windows/SmartDropZone.exe`.

## 📦 Releases

Every push to `main` automatically builds the app in the cloud, and versioned releases are published as tags. The newest version is always listed first on the [Releases](https://github.com/Hr-2/SmartDropZone/releases) page.

> **Tip:** the app checks for updates on launch automatically — you'll be prompted when a newer version is available.
>
> **Tip:** click **Check for updates now** in Settings to manually trigger a check.

## 🤖 AI assistance

I built this with the help of an AI coding assistant (opencode). I handled the design, direction and testing, and the AI helped me write, refactor and debug the code. I'm stating this openly so nobody has to guess.

## 📄 License

This is a personal project. Use it freely, modify it, and enjoy it.
