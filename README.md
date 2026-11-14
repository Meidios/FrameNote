# FrameNote

**Windows x64 视频与图片审阅工具 · A frame-accurate video and image review desk for Windows x64**

[下载 Windows 安装包 / Download for Windows](https://github.com/Meidios/FrameNote/releases/latest) · [更新日志 / Changelog](CHANGELOG.md) · [English](#english)

## 中文

把“这一声晚了一点”直接标在对应画面上。FrameNote 可以逐帧定位视频，在某一帧或一段时间上写反馈、画箭头、框选或贴参考图；也可以直接打开图片或剪贴板截图。导出一个 `.framenote` 文件，对方就能在 FrameNote 里打开原始媒体和可编辑的批注。审阅在本机完成，不需要账号。

### 安装与第一次审阅

1. 在[最新版本](https://github.com/Meidios/FrameNote/releases/latest)的 **Assets** 下载以 `win-x64-Setup.exe` 结尾的安装包。安装包包含 .NET 运行时、VLC、FFmpeg 和 FFprobe；如果电脑缺少 Microsoft Edge WebView2，首次安装需要联网。Windows 的默认视频播放器不会被替换。
2. 拖入视频，用 **空格** 播放／暂停、**← / →** 逐帧移动。按 **M** 添加点批注，或在不同帧按 **I** 和 **O** 标记区间；输入文字、添加图形或参考图后用 **Ctrl+Enter** 保存。
3. 点击 **导出审阅包** 交付可编辑版本；只想发一张图时，选择 **复制当前帧** 或 **复制批注图**，直接粘贴到聊天里。可用[示例审阅包](examples/SampleReview.framenote)体验。

| 任务 | 在 FrameNote 中怎么做 |
|---|---|
| 录屏与 GIF | 选择显示器、窗口或框选区域。在横向混音器中添加多个系统输出和一个可选麦克风；录制前按路调节增益、静音，成片为立体声混音；停止录制视频后直接进入审阅。临时录屏要在换片或退出前导出审阅包。GIF 最长 30 秒，无音频。 |
| 图片批注 | 空界面按 Ctrl+V 打开剪贴板图片；已有视频时，同一快捷键会添加图片批注。可缩放、平移，按原图分辨率复制或导出带批注 PNG；动图导出静态首帧。 |
| 播放与兼容 | 兼容的视频可直接逐帧审阅。其他编码可用内置 VLC 播放原片，进入批注时按需生成临时预览；导出审阅包仍保留原始媒体。 |
| 发送到 REAPER | 首次点击 REAPER 图标选择 Lua 脚本目录。打开目标工程，选择轨道并放置编辑光标，再发送：点批注变成 Marker，区间变成 Region。 |

### 从源码构建

在 Windows x64 安装支持 .NET 8 的 SDK、Windows Desktop 运行时、WebView2 和 x64 Visual C++ 运行库；将 FFmpeg 和 FFprobe 放在 PATH 或程序旁的 `tools` 目录。安装版用户不需要手动配置这些开发依赖。

```powershell
pwsh -File scripts/build.ps1
& ./artifacts/app/FrameNote.exe
```

构建安装包：按[打包说明](docs/windows-setup.md)准备 Inno Setup 6 等依赖，然后运行 `pwsh -File scripts/setup.ps1`。安装包与 SHA-256 校验文件生成在 `artifacts/release/`。

## English

Pin feedback such as “this impact lands late” to the frame where it happens. Step through a video, mark a point or a range, and add text, arrows, boxes or reference images. You can also review a still image or a clipboard screenshot. Export a single `.framenote` file to share the original media and editable annotations. Reviewing works locally without an account.

### Install and review your first clip

1. Under **Assets** on the [latest release](https://github.com/Meidios/FrameNote/releases/latest), download the file ending in `win-x64-Setup.exe`. Setup includes .NET, VLC, FFmpeg and FFprobe. If Microsoft Edge WebView2 is missing, setup needs an internet connection. Your default video player is unchanged.
2. Drop in a video. Press **Space** to play or pause and **Left / Right** to step by frame. Press **M** for a point, or **I** and **O** on different frames for a range. Add feedback, then press **Ctrl+Enter** to save.
3. Choose **导出审阅包** to share an editable review. To send a still instead, use **复制当前帧** (copy the current video frame) or **复制批注图** (copy the annotated image), then paste it into chat. A [sample review package](examples/SampleReview.framenote) is included.

| Task | How it works |
|---|---|
| Screen capture and GIF | Record a display, window or selected region. Add multiple Windows outputs and an optional microphone in the horizontal mixer. Set gain and mute per source before recording one stereo mix. Stopping a video opens it for review; export the temporary recording before switching files or quitting. GIFs are silent and limited to 30 seconds. |
| Image annotation | Paste into an empty desk to open a clipboard image; paste while reviewing a video to attach a picture note. Zoom or pan without changing the dimensions of an exported PNG. Animated images export as a still of the first frame. |
| Playback compatibility | Compatible media opens directly in frame review. Other codecs can play in the built-in VLC player; entering review creates a temporary preview if needed. Packages keep the original media. |
| REAPER handoff | On first use, choose a folder for the Lua action. In REAPER, select a track and place the edit cursor in the target project, then send from FrameNote. Points become markers and ranges become regions. |

### Build from source

On Windows x64, install an SDK that targets .NET 8, the Windows Desktop Runtime, WebView2 and the x64 Visual C++ runtime. Put FFmpeg and FFprobe on PATH or in a `tools` folder beside the app. Run the two PowerShell commands above from the repository root. Keep the resulting executable with its adjacent DLLs and folders.

For a self-contained installer, follow the [Windows packaging instructions](docs/windows-setup.md) and run `pwsh -File scripts/setup.ps1`. The installer and SHA-256 sidecar are written to `artifacts/release/`. See the [detailed user guide](docs/user-guide.en.md) for media compatibility, draft recovery, keyboard shortcuts and REAPER behavior, and the [verification notes](docs/verification.md) for tested scenarios and limitations.

### 第三方资源 / Third-party assets

[Noto Sans SC](src/FrameNote/ui/fonts/Noto-OFL.txt) · [Cascadia Mono](src/FrameNote/ui/fonts/Cascadia-OFL.txt) · [Fluent System Icons](src/FrameNote/ui/icons/LICENSE.txt) · [Fontaudio REAPER glyph](src/FrameNote/ui/reaper-LICENSE.txt) · [Linkify.js](src/FrameNote/ui/vendor/linkifyjs/LICENSE)
