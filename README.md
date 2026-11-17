# FrameNote

**Windows x64 视频与图片审阅工具 · A frame-accurate video and image review desk for Windows x64**

[下载 Windows 安装包 / Download for Windows](https://github.com/Meidios/FrameNote/releases/latest) · [更新日志 / Changelog](CHANGELOG.md) · [English](#english)

## 中文

把“这一声晚了一点”直接标在对应画面上。FrameNote 可以逐帧定位视频，在某一帧或一段时间上写反馈、画箭头、框选或贴参考图；也可以直接打开图片或剪贴板截图。导出一个 `.framenote` 文件，对方就能在 FrameNote 里打开原始媒体和可编辑的批注。审阅在本机完成，不需要账号。

### 安装与第一次审阅

安装后，右键视频 → **打开方式**。选 **FrameNote Player** 直接播放原片，选 **FrameNote** 进入原有审阅流程。要让某种视频双击时总是播放，在 Windows 的默认应用设置中为该扩展名选择 **FrameNote Player**。

在资源管理器多选照片和视频，右键选 **用 FrameNote Player 打开**，即可在同一个窗口浏览。左侧播放列表显示缩略图和文件名，单个文件默认收起，多个文件默认展开。向窗口拖入更多文件会追加到列表，不打断当前播放。

录制设置和混音器在左侧，批注列表和编辑器在右侧。中间时间线从上到下显示视频缩略图、混合音频波形和批注；选中一个区间后，可在播放栏开启循环。下次打开时会恢复窗口大小或最大化状态。

1. 在[最新版本](https://github.com/Meidios/FrameNote/releases/latest)的 **Assets** 下载以 `win-x64-Setup.exe` 结尾的安装包。安装包包含 .NET 运行时、VLC、FFmpeg 和 FFprobe；如果电脑缺少 Microsoft Edge WebView2，首次安装需要联网。Windows 的默认视频播放器不会被替换。
2. 拖入视频，用 **空格** 播放／暂停、**← / →** 逐帧移动。按 **M** 添加点批注，或在不同帧按 **I** 和 **O** 标记区间；在右栏底部编辑，修改自动保存。卡片右上角可删除该条批注；**Ctrl+Z** 撤销，**Ctrl+Shift+Z** 重做。
3. 点击 **导出审阅包** 交付可编辑版本；只想发一张图时，选择 **复制当前帧** 或 **复制批注图**，直接粘贴到聊天里。可用[示例审阅包](examples/SampleReview.framenote)体验。

| 任务 | 在 FrameNote 中怎么做 |
|---|---|
| 录屏与 GIF | 选择显示器、窗口或框选区域。在横向混音器中添加多个系统输出和一个可选麦克风；录制前按路调节增益、静音，成片为立体声混音；停止录制视频后直接进入审阅。临时录屏要在换片或退出前导出审阅包。GIF 最长 30 秒，无音频。 |
| 图片批注 | 空界面按 Ctrl+V 打开剪贴板图片；已有视频时，同一快捷键会添加图片批注。可缩放、平移，按原图分辨率复制或导出带批注 PNG；动图导出静态首帧。 |
| 播放与兼容 | 兼容的视频可直接逐帧审阅。其他编码可用内置 VLC 播放原片，进入批注时按需生成临时预览；导出审阅包仍保留原始媒体。 |
| 发送到 REAPER | 首次单击安装脚本；之后单击会检查更新，并发送到所选轨道的编辑光标处。也可将按钮直接拖到 REAPER 编排区，按落点插入。审阅包的 Marker 和 Region 保留批注颜色。 |

在原片播放器中，使用底部走带逐帧定位、复制当前帧或另存 PNG。图片查看器的画布直接位于菜单下方；按 **Ctrl+O** 打开文件，按 **Ctrl+S** 导出审阅包。

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

After installation, right-click a video and choose **Open with**. **FrameNote Player** plays the original video; **FrameNote** uses the existing review workflow. To play on double-click, choose **FrameNote Player** for the extension in Windows Default apps.

Recording settings and the mixer are on the left; the annotation list and editor are on the right. The central timeline stacks video thumbnails, the mixed audio waveform, and annotations. Select a range to loop it from the playback bar. FrameNote remembers the window size and maximized state between sessions.

1. Under **Assets** on the [latest release](https://github.com/Meidios/FrameNote/releases/latest), download the file ending in `win-x64-Setup.exe`. Setup includes .NET, VLC, FFmpeg and FFprobe. If Microsoft Edge WebView2 is missing, setup needs an internet connection. Your default video player is unchanged.
2. Drop in a video. Press **Space** to play or pause and **Left / Right** to step by frame. Press **M** for a point, or **I** and **O** on different frames for a range. Edit in the pinned details panel. Changes save automatically. Use the trash icon on a card to delete it, **Ctrl+Z** to undo, or **Ctrl+Shift+Z** to redo.
3. Choose **导出审阅包** to share an editable review. To send a still instead, use **复制当前帧** (copy the current video frame) or **复制批注图** (copy the annotated image), then paste it into chat. A [sample review package](examples/SampleReview.framenote) is included.

| Task | How it works |
|---|---|
| Screen capture and GIF | Record a display, window or selected region. Add multiple Windows outputs and an optional microphone in the horizontal mixer. Set gain and mute per source before recording one stereo mix. Stopping a video opens it for review; export the temporary recording before switching files or quitting. GIFs are silent and limited to 30 seconds. |
| Image annotation | Paste into an empty desk to open a clipboard image; paste while reviewing a video to attach a picture note. Zoom or pan without changing the dimensions of an exported PNG. Animated images export as a still of the first frame. |
| Playback compatibility | Compatible media opens directly in frame review. Other codecs can play in the built-in VLC player; entering review creates a temporary preview if needed. Packages keep the original media. |
| REAPER handoff | The first click installs the action. Later clicks check for updates and send to the selected track at the edit cursor. Drag the button onto the arrange view to choose a track and time instead. Review packages preserve marker and region colors. |

In the original-media player, use the bottom transport to step by frame, copy the current frame, or save it as PNG. In the image viewer, the canvas starts directly below the menus. Use **Ctrl+O** to open a file and **Ctrl+S** to export a review package.

Select images and videos together in Explorer and choose **用 FrameNote Player 打开** (Open with FrameNote Player) to browse them in one window. The thumbnail playlist starts collapsed for one file and expanded for multiple files. Drop more files onto the window to append them without interrupting playback.

### Build from source

On Windows x64, install an SDK that targets .NET 8, the Windows Desktop Runtime, WebView2 and the x64 Visual C++ runtime. Put FFmpeg and FFprobe on PATH or in a `tools` folder beside the app. Run the two PowerShell commands above from the repository root. Keep the resulting executable with its adjacent DLLs and folders.

For a self-contained installer, follow the [Windows packaging instructions](docs/windows-setup.md) and run `pwsh -File scripts/setup.ps1`. The installer and SHA-256 sidecar are written to `artifacts/release/`. See the [detailed user guide](docs/user-guide.en.md) for media compatibility, draft recovery, keyboard shortcuts and REAPER behavior, and the [verification notes](docs/verification.md) for tested scenarios and limitations.

### 第三方资源 / Third-party assets

[Noto Sans SC](src/FrameNote/ui/fonts/Noto-OFL.txt) · [Cascadia Mono](src/FrameNote/ui/fonts/Cascadia-OFL.txt) · [Fluent System Icons](src/FrameNote/ui/icons/LICENSE.txt) · [Fontaudio REAPER glyph](src/FrameNote/ui/reaper-LICENSE.txt) · [Linkify.js](src/FrameNote/ui/vendor/linkifyjs/LICENSE)
