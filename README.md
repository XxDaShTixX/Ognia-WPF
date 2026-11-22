# 🔥 Ognia (WPF Client)

<p align="center">
  <img src="/Wpf/Assets/Brand/Logo/Ognia.png" alt="Ognia logo" width="200"/>
</p>

A Windows music player that turns YouTube links into a local, offline-capable library. Built in WPF with .NET 10, NAudio, and `YoutubeExplode`. Works completely standalone in guest mode, or syncs playlists and metadata with [Ognia-Server](https://github.com/XxDaShTixX/Ognia-Server).

## Screenshots

| Login | Library |
|:---:|:---:|
| <img src="https://github.com/user-attachments/assets/86eeb897-f972-4196-b861-edccf7a2a38b" width="400" /> | <img src="https://github.com/user-attachments/assets/3165d5d5-70d2-4b0a-b6d2-30e3b888feab" width="400"/> |

| Playlist | Settings |
|:---:|:---:|
| <img src="https://github.com/user-attachments/assets/d16d8f42-f1f8-4e40-8271-2846e2eb0544" width="400"/> | <img width="400" src="https://github.com/user-attachments/assets/f596fd7b-c906-4f42-91bc-65e44477b395" /> |

| Mini Player (Active) | Mini Player (Inactive) |
|:---:|:---:|
| <img width="400" alt="image" src="https://github.com/user-attachments/assets/59c66794-54da-4a83-97c7-122b9f219ace" /> | <img width="400" src="https://github.com/user-attachments/assets/f50fdd56-57e3-4069-ad65-88450d97ecf1" /> |



## ✨ Features

- **Offline-first playback:** Download audio locally and play offline
- **Guest mode:** Use the app with no backend required, stored locally
- **Account sync:** Email/password login with JWT + refresh tokens, password reset/change, and one-click guest-to-account library upload (requires [Ognia-Server](https://github.com/XxDaShTixX/Ognia-Server))
- **Library portability:** Export and import the guest library as JSON
- **Playlists:** Create, rename, delete, reorder, and batch-download all tracks with status bar progress
- **Tracks:** Add by YouTube URL, rename, remove
- **Playback controls:** Play/pause, stop, next/previous, seek, volume, mute, repeat (off/all/one), and shuffle
- **Auto-generated covers:** 2×2 playlist covers stitched from track thumbnails
- **Mini player:** Compact, always-on-top window with hover-to-reveal fade
- **Custom themes:** Default, Dark, and Light themes (JSON-based and user-editable)

## 🏗️ Architecture

```
+----------------+   HTTPS/JSON   +------------------+
|  Ognia-WPF     | <------------> |  Ognia-Server    |
|                |  JWT bearer    |  (Optional)      |
+----------------+                +------------------+
       |
       v 
  Local audio +
  settings + library
  + themes + cache
```
The client owns all local audio files, playback, settings, themes, and (in guest mode) the local library. Audio bytes never pass through a server — `Ognia-WPF` downloads directly from YouTube via `YoutubeExplode`. In guest mode, track metadata lookups (title, duration, thumbnail) also run locally via `YoutubeExplode`.

## 🧰 Tech Stack

- **.NET 10** & **WPF**
- **Architecture:** Plain MVVM (`INotifyPropertyChanged`, `ICommand`)
- **Audio & Media:** `NAudio`, `YoutubeExplode`

## 🚀 Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows (WPF is Windows-only)

### Run the Client

```bash
dotnet dev-certs https --trust     # one-time (needed if connecting to a local HTTPS server)
dotnet run
```

By default, the client looks for the backend at `https://localhost:7235`. You don't need the server running to use **Guest Mode**.

To point the client to a different backend URL:
- Edit `api.baseUrl` in `%LOCALAPPDATA%\Ognia\settings.json` after first launch, **or**
- Update `ApiSettings.BaseUrl` in `Helpers/AppSettings.cs` before building.

### 🎧 First Use

1. Click **Continue as Guest** to start immediately without a server, or sign in if you have [Ognia-Server](https://github.com/XxDaShTixX/Ognia-Server) running.
2. Create a playlist.
3. Paste a YouTube URL (`youtube.com/watch?v=...`, `youtu.be/...`, or `music.youtube.com/watch?v=...`).
4. Click **Download** to save the audio locally.
5. Click **Play**.

### 📦 Building installers and releases

Builds are packaged with [Velopack](https://velopack.io/). The `Ognia/Velopack/` folder holds the tooling:

- `Run.bat` prompts for a version, publishes `Wpf`, and packs the release into `Ognia/Velopack/Releases/`.
- `Build.ps1` is the script `Run.bat` invokes — clean publish, `vpk pack`.

To cut a release:

1. Run `Ognia/Velopack/Run.bat` and enter the version (e.g. `0.0.2`).
2. Go to the GitHub Releases page, create a new tag `v<version>`, and upload **all** files from `Ognia/Velopack/Releases/` — `releases.win.json`, `assets.win.json`, `Ognia-<version>-full.nupkg`, and the installer.
3. Publish the release as **normal**, not pre-release, for stable users. Mark it **pre-release** to only offer it to users on the pre-release channel.

Installed apps check GitHub on startup, download any newer release in the background, and apply it on exit. The new version runs the next time the app launches.

## 🔖 Versioning

Ognia loosely follows Semantic Versioning while the project remains pre-1.0.

### Release guidelines

```text
0.x.0   → New user-facing features
0.x.y   → Bug fixes, polish, refactors, UI/UX improvements
```

Examples:

```text
0.1.0
- Search
- Preview playback
- Auto-updates

0.1.1
- Playlist drag-and-drop
- Playlist deletion improvements
- Window maximize fix

0.2.0
- Smart playlists
- Lyrics support
- Other major functionality
```

### Pre-releases

```text
0.2.0-alpha.1
0.2.0-beta.1
```

- Alpha = active testing
- Beta = feature complete, needs broader testing
- No suffix = stable release

### Quick reference

```text
New feature?       → 0.x.0
Fix or polish?     → 0.x.y
Testing build?     → -alpha.n / -beta.n
Ready for users?   → Stable release
```

## 📁 Where files live

**Audio** — `%USERPROFILE%\Music\Ognia\<playlist>\<track>.m4a` (per-playlist folder).

**App data** — `%APPDATA%\Ognia\`:

| File | Purpose |
|---|---|
| `settings.json` | App mode, active theme; API base URL; playback state (volume, repeat, shuffle, last track + position); window geometry and opacity; update preferences; last opened playlist |
| `token.json` | JWT + refresh token + expiry |
| `library.json` | Guest-mode playlists and tracks |
| `Themes/` | Theme JSON files (seeded from embedded defaults) |
| `playlist-covers/` | Generated 2×2 cover collages |
| `previews/` | Temporary search-result previews, wiped on exit |

**Install** — `%LOCALAPPDATA%\Ognia\` (Velopack's install directory; separate from app data to avoid collisions on update).

**Database** — `Server/database.db` (SQLite, created by `dotnet ef database update`).

## 🎵 Track sources

Each track has a `(SourceName, SourceId)` pair — e.g. `("youtube", "dQw4w9WgXcQ")`. All of these URLs normalize to the same track:

- `https://www.youtube.com/watch?v=dQw4w9WgXcQ`
- `https://youtu.be/dQw4w9WgXcQ`
- `https://music.youtube.com/watch?v=dQw4w9WgXcQ&t=42s`

### ➕ Adding a new source

Implement `ITrackSourceService` in the server (see `Server/Services/TrackSourceService/`), add a `TrackSource` enum member, add its wire name to `TrackSourceExtensions`, and register it in `Program.cs`.

Note: the server currently registers a single `ITrackSourceService` (`YouTubeTrackSourceService`) and the add-track endpoint uses that one instance directly. To support multiple sources, change the registration to a collection (`IEnumerable<ITrackSourceService>`) and update the endpoint to dispatch by `CanHandle(url)` — the interface is already designed for this.

## 🤝 Contributing

Contributions are welcome — this started as a personal project, and it's better with more eyes on it.

### 🐛 Found a bug or want a feature?

Open an [issue](https://github.com/XxDaShTixX/Ognia-WPF/issues). Both are equally welcome.

- **Bugs** — describe what you expected, what happened, and how to reproduce it. Screenshots or stack traces help.
- **Features** — describe the use case, not just the solution. "I want to X so that Y" is more useful than "add a button called Z."

### 🛠️ Submitting code

1. **Fork** the repo (or, if you have access, branch directly).
2. **Create a branch named after yourself and the change:**
   ```bash
   git checkout -b YourName-ShortDescription
   # e.g. Dash-FixPlaylistRename, Dash-AddKeyboardShortcuts
   ```
3. **Make your change.** Keep commits focused; a single feature or fix per branch is ideal. The smaller the change, the better.
4. **Test it.** `dotnet build` must pass. Run the client and test your changes in both **Guest Mode** and with an account (if applicable).
5. **Open a pull request** against `main`. Describe what changed and why. Reference the issue number if there is one (`Closes #12`).

### 🤖 AI-assisted contributions

Using AI tools (Copilot, Claude, ChatGPT, Cursor, etc.) is fine. You don't need to disclose it. What matters is that you understand what you're submitting — you should be able to explain every line of your diff, debug it if it breaks, and stand behind it in review. "The model wrote it" is not a defense when something goes wrong.

### 🔍 What I'll look at in a PR

- **Does it work?** Clean build, no errors or warnings, no obvious regressions.
- **Does it fit the existing structure?** Plain MVVM (`INotifyPropertyChanged`, `ICommand`), clean separation of views, view models, and local repositories/services.
- **Does it work in guest mode too?** Anything user-facing should either function with `LocalMusicRepo` or be explicitly hidden when no backend is configured.
- **Is the diff small?** Large sweeping refactors are harder to review than focused changes.

### 🚫 What I won't merge

- Secrets or personal tokens in commits.
- Breaking or bypassing guest mode when it's trivial to support.
- Integrations that violate a platform's terms of service.

### 🧭 Not sure where to start?

[Open issues](https://github.com/XxDaShTixX/Ognia-WPF/issues) tagged `General` are a good entry point. Or open an issue describing what you'd like to build and we'll figure out the approach together before you write code.

### 🌱 No contribution is too small

Typo fixes, README improvements, clarifying a comment — all welcome. You don't need to rewrite a subsystem to help.

## 🔗 Related

- **[Ognia-Server](https://github.com/XxDaShTixX/Ognia-Server)** — Optional ASP.NET Core backend for user accounts, cloud playlist sync, and server-side metadata lookups.

## ⚖️ Legal

This is an educational project. It does **not** host, distribute, or own any copyrighted audio. Audio is downloaded by the client from YouTube, subject to YouTube's terms of service and applicable copyright law. `YoutubeExplode` is used for interoperability.

If you distribute a build:

- YouTube's terms prohibit downloading content for most uses.
- App stores (Apple, Google) generally reject apps with built-in YouTube-downloading.
- Distributing on GitHub sidesteps store review but does not change the underlying legal risk.

Use it for personal learning and experimentation. Don't redistribute content you don't have rights to.

## 📜 License

MIT — see [LICENSE](LICENSE).
