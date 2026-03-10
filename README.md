<p align="center">
  <img src="https://img.shields.io/badge/TubeLoad-v1.0.0-00B4D8?style=for-the-badge&labelColor=0D1117" alt="Version"/>
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet&labelColor=0D1117" alt=".NET 8"/>
  <img src="https://img.shields.io/badge/WPF-Desktop-7C3AED?style=for-the-badge&labelColor=0D1117" alt="WPF"/>
  <img src="https://img.shields.io/badge/License-MIT-00E676?style=for-the-badge&labelColor=0D1117" alt="License"/>
</p>

<h1 align="center">
  🎬 TubeLoad
</h1>

<p align="center">
  <strong>Modern Video Downloader for Windows</strong><br/>
  <em>Download videos from YouTube & TikTok with a beautiful dark-themed interface</em>
</p>

<p align="center">
  <a href="#-features">Features</a> •
  <a href="#-screenshots">Screenshots</a> •
  <a href="#-installation">Installation</a> •
  <a href="#-usage">Usage</a> •
  <a href="#-architecture">Architecture</a> •
  <a href="#-tech-stack">Tech Stack</a> •
  <a href="#-license">License</a>
</p>

---

## ✨ Features

### 🎯 Core Functionality
- **Multi-Platform Support** — Download from YouTube and TikTok
- **Quality Selection** — Visual card-based format picker with quality tags (4K, Full HD, HD, SD)
- **Audio Extraction** — Download audio-only in MP3 format
- **Smart Format Detection** — Auto-detects available qualities with file sizes
- **Download Queue** — Sequential processing with queue management

### 🎨 Modern UI/UX
- **Dark Theme** — Sleek dark interface with cyan & purple accent gradients
- **Animated Progress** — Real-time progress bar with speed & ETA display
- **Custom Scrollbars** — Themed scrollbars with gradient hover effects
- **Borderless Window** — Custom title bar with minimize/maximize/close controls
- **Responsive Layout** — Clean, organized sections for each workflow step

### 📋 History & Management
- **Download History** — SQLite-backed history with thumbnails & status tracking
- **Thumbnail Preview** — Video thumbnails displayed in both info cards and history
- **Re-download** — One-click re-download from history entries
- **Platform Badges** — Visual YouTube/TikTok platform indicators

### 🛠 Developer Tools
- **Debug Console** — Built-in diagnostic window (Debug builds only)
- **20 Diagnostic Checks** — System, dependencies, network, database, performance
- **10 Functional Tests** — Automated test suite for core functionality
- **Singleton Debug Window** — Prevents duplicate debug window instances

---

## 🖥 Screenshots

> **Dark-themed interface with gradient accents and card-based quality selector**

```
┌─────────────────────────────────────────────────────┐
│  🎬 TubeLoad                           ─  □  ✕     │
├─────────────────────────────────────────────────────┤
│                                                     │
│  ┌───────────────────────────────────┐  ┌────────┐  │
│  │ 🔗 Paste video URL here...       │  │ Fetch  │  │
│  └───────────────────────────────────┘  └────────┘  │
│                                                     │
│  ┌─────────────────────────────────────────────────┐│
│  │ 📹 Video Title                                  ││
│  │ ┌──────────┐  Channel Name                      ││
│  │ │ 🖼 Thumb │  Duration: 10:25                   ││
│  │ └──────────┘  Platform: YouTube                 ││
│  └─────────────────────────────────────────────────┘│
│                                                     │
│  📊 Select Quality (5 formats)                      │
│  ┌─────────────────────────────────────────────────┐│
│  │ ⭐ Best Quality     Auto Select    MP4   25 MB  ││
│  │ ▶ 1920x1080        Full HD        MP4   20 MB  ││
│  │ ▶ 1280x720         HD             MP4   12 MB  ││
│  │ ▶ 640x360          SD             MP4    6 MB  ││
│  │ ♫ Audio Only       MP3 128kbps    M4A    3 MB  ││
│  └─────────────────────────────────────────────────┘│
│                                                     │
│  ✅ Selected: 1080p (MP4)        [⬇ Download]       │
│                                                     │
│  ═══════════════════════════════════════════════════ │
│  📥 Downloads                                       │
│  ┌─────────────────────────────────────────────────┐│
│  │ Video Title                    Status: 45.2%    ││
│  │ ████████████░░░░░░░░░░░  ⚡ 2.5 MB/s  ⏱ 00:30  ││
│  └─────────────────────────────────────────────────┘│
│                                                     │
│  ═══════════════════════════════════════════════════ │
│  📜 History                                         │
│  ┌─────────────────────────────────────────────────┐│
│  │ ┌──────┐  Video Title           ✅              ││
│  │ │Thumb │  YouTube · Full HD                     ││
│  │ └──────┘  10 Mar 2026  22:00       [↻ Retry]   ││
│  └─────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────┘
```

---

## 📦 Installation

### Prerequisites

| Requirement | Version | Notes |
|------------|---------|-------|
| **Windows** | 10/11 (x64) | Required for WPF |
| **yt-dlp** | Latest | Must be in system PATH |
| **ffmpeg** | Latest | Required for audio/video merging |

### Install Dependencies

```powershell
# Install yt-dlp via winget
winget install yt-dlp.yt-dlp

# Install ffmpeg via winget
winget install Gyan.FFmpeg

# Verify installation
yt-dlp --version
ffmpeg -version
```

### Build from Source

```powershell
# Clone the repository
git clone https://github.com/xjanova/Tubeload.git
cd Tubeload

# Build the project
dotnet build TubeLoad/TubeLoad.csproj -c Release

# Run the application
dotnet run --project TubeLoad/TubeLoad.csproj
```

### Publish as Single Executable

```powershell
# Create self-contained single-file executable
dotnet publish TubeLoad/TubeLoad.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

The published executable will be in the `./publish` folder.

---

## 🚀 Usage

### Basic Workflow

1. **Paste URL** — Copy a YouTube or TikTok video URL into the input field
2. **Fetch Info** — Click "Fetch" to retrieve video information and available formats
3. **Select Quality** — Choose your preferred quality from the visual format cards
4. **Download** — Click the download button and monitor progress in real-time

### Supported Platforms

| Platform | URL Format | Features |
|----------|-----------|----------|
| **YouTube** | `youtube.com/watch?v=...` | All qualities, audio extraction |
| **YouTube** | `youtu.be/...` | Short URL support |
| **TikTok** | `tiktok.com/@user/video/...` | Video download |

### Quality Options

| Tag | Resolution | Description |
|-----|-----------|-------------|
| 🌟 4K Ultra HD | 3840×2160 | Highest quality available |
| 🎬 2K QHD | 2560×1440 | Quad HD |
| 📺 Full HD | 1920×1080 | Standard high definition |
| 🖥 HD | 1280×720 | HD ready |
| 📱 SD | 640×480 | Standard definition |
| 🎵 Audio | MP3/M4A | Audio-only extraction |

### Download Location

Videos are saved to: `Downloads/TubeLoad/` in your user profile directory.

---

## 🏗 Architecture

```
TubeLoad/
├── 📄 App.xaml                    # Application resources, themes & styles
├── 📄 App.xaml.cs                 # Application entry point
├── 📄 MainWindow.xaml             # Main UI layout (XAML)
├── 📄 MainWindow.xaml.cs          # Main window logic & event handlers
├── 📄 DebugWindow.xaml            # Debug/diagnostic console UI
├── 📄 DebugWindow.xaml.cs         # Diagnostic & test runner logic
├── 📄 AssemblyInfo.cs             # Assembly metadata
├── 📄 TubeLoad.csproj             # Project configuration
│
├── 📂 Models/
│   ├── 📄 VideoInfo.cs            # VideoInfo, VideoFormat, DownloadItem, DownloadStatus
│   └── 📄 HistoryItem.cs          # Download history data model
│
├── 📂 Services/
│   ├── 📄 YtDlpService.cs         # yt-dlp process wrapper (fetch info, download)
│   ├── 📄 DatabaseService.cs      # SQLite database operations (history CRUD)
│   ├── 📄 QueueService.cs         # Download queue with sequential processing
│   └── 📄 DiagnosticService.cs    # 20 diagnostic checks across 5 categories
│
├── 📂 Converters/
│   └── 📄 Converters.cs           # WPF value converters (visibility, color, progress)
│
├── 📂 Tests/
│   └── 📄 TestRunner.cs           # 10 automated functional tests
│
└── 📂 Assets/                     # Application icons & resources
```

### Design Patterns

| Pattern | Usage |
|---------|-------|
| **MVVM-Lite** | Data binding with INotifyPropertyChanged |
| **Service Layer** | YtDlpService, DatabaseService, QueueService |
| **Singleton** | Debug window instance management |
| **Observer** | Event-driven queue state changes |
| **Command** | Button click handlers with async operations |

### Data Flow

```
User Input → MainWindow → YtDlpService → yt-dlp Process
                              ↓
                        Parse JSON Output
                              ↓
                     VideoInfo + Formats
                              ↓
                    Quality Card Selection
                              ↓
                   QueueService (Sequential)
                              ↓
                  YtDlpService.DownloadAsync()
                              ↓
              Real-time Progress (stdout parsing)
                              ↓
                DatabaseService (Save History)
```

---

## 🔧 Tech Stack

| Technology | Purpose |
|-----------|---------|
| **.NET 8** | Runtime & SDK |
| **WPF** | Desktop UI framework |
| **C# 12** | Programming language |
| **yt-dlp** | Video download engine |
| **ffmpeg** | Audio/video merging |
| **SQLite** | Local database (history) |
| **Newtonsoft.Json** | JSON parsing for yt-dlp output |
| **Microsoft.Data.Sqlite** | ADO.NET SQLite provider |

### NuGet Packages

```xml
<PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.*" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
```

---

## 🎨 Theme & Design

TubeLoad uses a custom dark theme with carefully chosen colors:

| Color | Hex | Usage |
|-------|-----|-------|
| 🔵 Primary Dark | `#0D1117` | Main background |
| 🔵 Secondary Dark | `#161B22` | Section backgrounds |
| 🔵 Card Dark | `#1C2333` | Card backgrounds |
| 🔵 Accent Blue | `#00B4D8` | Primary accent |
| 🟣 Accent Purple | `#7C3AED` | Gradient accent |
| 🟢 Success Green | `#00E676` | Success states |
| 🔴 Error Red | `#FF5252` | Error states |
| ⚪ Text Primary | `#E6EDF3` | Main text |
| ⚪ Text Secondary | `#8B949E` | Muted text |

---

## 🧪 Testing & Diagnostics

### Built-in Test Suite (Debug Mode)

Access the debug console by pressing the 🔧 button (visible in Debug builds only).

**20 Diagnostic Checks:**
- 💻 System — OS, .NET runtime, memory, disk space
- 📦 Dependencies — yt-dlp availability, ffmpeg availability, version checks
- 🌐 Network — YouTube connectivity, TikTok connectivity, DNS resolution
- 💾 Database — SQLite connection, read/write operations, schema validation
- ⚡ Performance — UI thread responsiveness, memory usage, startup time

**10 Functional Tests:**
- Model creation & property validation
- Converter logic verification
- Service initialization
- Queue enqueue/dequeue operations
- Database CRUD operations
- Format display text generation
- Status text formatting
- History item display helpers
- Download status workflow
- Video info parsing

---

## 📝 Changelog

### v1.0.0 (March 2026)
- 🎬 Initial release
- 📥 YouTube & TikTok video downloading
- 🎨 Dark theme with gradient accents
- 📊 Visual card-based quality selector
- 📋 Download queue with sequential processing
- 💾 SQLite download history with thumbnails
- ⚡ Real-time progress with speed & ETA
- 🛠 Built-in diagnostic console (Debug mode)
- 🎯 Custom themed scrollbars
- 🔧 20 diagnostic checks + 10 functional tests

---

## 📄 License

```
MIT License

Copyright (c) 2026 Xman Studio

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

---

<p align="center">
  <strong>Built with ❤️ by Xman Studio</strong><br/>
  <sub>Powered by .NET 8 • WPF • yt-dlp</sub>
</p>
