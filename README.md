# FrameNote

**Windows x64 视频与图片审阅工具 · A frame-accurate video and image review desk for Windows x64**

[下载 Windows 安装包 / Download for Windows](https://github.com/Meidios/FrameNote/releases/latest) · [更新日志 / Changelog](CHANGELOG.md) · [English](#english)

## 中文

把“这一声晚了一点”直接标在对应画面上。FrameNote 可以逐帧定位视频，在某一帧或一段时间上写反馈、画箭头、框选或贴参考图；也可以直接打开图片或剪贴板截图。导出一个 `.framenote` 文件，对方就能在 FrameNote 里打开原始媒体和可编辑的批注。审阅在本机完成，不需要账号。

### 安装与第一次审阅

安装后，右键视频 → **打开方式**。选 **FrameNote Player** 直接播放原片，选 **FrameNote** 进入原有审阅流程。要让某种视频双击时总是播放，在 Windows 的默认应用设置中为该扩展名选择 **FrameNote Player**。

在资源管理器多选照片和视频，右键选 **用 FrameNote Player 打开**，即可在同一个窗口浏览。左侧播放列表显示缩略图和文件名，单个文件默认收起，多个文件默认展开。向窗口拖入更多文件会追加到列表，不打断当前播放。

在审阅画面按住 **Ctrl**，左键拖动画框，右键拖动画箭头，中键点击可在落点创建文字框并直接输入。每次操作都在当前播放头新建独立批注。

在框选内部滚动可等比缩放框线。放大镜的取景框内滚轮调大小，预览框内滚轮调倍率，每格 0.5 倍。箭头和线可直接拖出，也可单击连续落点，以右键、双击或 Enter 收笔，按 Esc 取消。拖动节点可改变线形，在线条上滚动可调节曲率。选中画笔即可连续绘制，滚轮调整笔宽，再点一次画笔或按 Esc 结束。

鼠标指向批注时，**Alt＋滚轮** 向下切到下一种预设颜色，向上切到上一种，到两端停止。**Shift＋滚轮** 每格调整 5% 不透明度，向下更透明。两种手势都可在快捷键中改绑或反转。**设置 → 批注工具** 可调整新批注的默认不透明度（80%）、放大镜默认倍率（3 倍）和最多 10 个预设颜色，也可修改工具的顺序、显隐和宽度，或插入分隔线。

在 **文件 → 设置** 的快捷键列表中，点一项操作即可编辑。键盘快捷键可以直接按下录入，鼠标手势可以选择修饰键和鼠标键。同一操作可添加多组组合，也可以删除全部绑定。点「确定」返回列表，再点「保存」即可在 FrameNote 和 Player 中使用。每个分组标题右侧的按钮可恢复该组默认值。设置中也可以打开 Windows 默认应用设置。

视频和声音捕获来源在左侧，批注列表和编辑器在右侧。帧率、压缩品质和录制副本可在「设置 → 录制设置」中调整。中间时间线显示视频缩略图、混合音频波形和批注。右键拖出时间选区，可在时间尺上左拖选区边缘微调。点「区间循环」开启循环，再按播放键试听。双击时间线或右侧列表中的 Region 批注，会选中其时长并立即循环。按 Esc 清除选区并关闭循环。下次打开时会恢复窗口大小或最大化状态。

1. 在[最新版本](https://github.com/Meidios/FrameNote/releases/latest)的 **Assets** 下载以 `win-x64-Setup.exe` 结尾的安装包。安装包包含 .NET 运行时、VLC、FFmpeg 和 FFprobe；如果电脑缺少 Microsoft Edge WebView2，首次安装需要联网。Windows 的默认视频播放器不会被替换。
2. 拖入视频，用 **空格** 播放／暂停、**← / →** 逐帧移动。直接选框选、箭头等工具在画面上绘制，就会在播放头位置新建点批注。选图和录音也各自新建批注。右键画面可选工具，右键已有批注可编辑、换色、复制或删除。**M** 可添加纯文字点批注，**I** 和 **O** 可标记区间。批注和剪辑只保留到窗口关闭，需要留存时请先导出 `.framenote` 审阅包。卡片右上角可删除该条批注；**Ctrl+Z** 撤销，**Ctrl+Shift+Z** 重做。
3. 点击 **导出审阅包** 交付可编辑版本；只想发一张图时，选择 **复制当前帧** 或 **复制批注图**，直接粘贴到聊天里。可用[示例审阅包](examples/SampleReview.framenote)体验。

| 任务 | 在 FrameNote 中怎么做 |
|---|---|
| 框选多个片段 | 从轨道空白处或片段上方的窄条按住左键拖动。轨道排满时，也可按住 **Shift** 从片段内部起框。拖动任一选中片段可整组移动，连续片段拖过相邻片段的中点可整组换序。按 **Delete** 批量删除并保留空隙，**Ctrl+Z** 可一步撤销。 |
| 对齐时间线片段 | 选中 Clip，点击 **贴到上一片段** 将开头贴到同轨上一段的结尾，或点击 **贴到下一片段** 将尾部贴到同轨下一段的开头。首段向左贴到零点，末段向右贴到时间线末尾。拖动时片段限于当前时间线，片段和批注块的头尾都可吸附到时间指针。默认按住 Alt 可暂时关闭吸附。 |
| 调整已有批注 | 在画面上拖动图形或附图即可移动。选中后拖端点调整线长和方向，拖顶点改变角度，拖角点调整框或图片大小。双击文字框可修改文字。 |
| 移动批注区间和调整时长 | 拖住 Region 标签中间可整体移动区间，关联语音会一起移动。拖动非语音批注标签的左边缘改起点，拖右边缘改终点。点批注可直接拉成区间，两端合拢可恢复成点。 |
| 录制语音批注 | 在左侧启用麦克风，点批注工具栏的麦克风开始录音。系统默认输出会暂时静音，再点一次保存为 MP3，按 Esc 放弃。 |
| 添加音频批注 | 选中批注后点击音频文件工具附加声音，或从资源管理器直接拖入批注音轨。Excel 随附链接到录音 MP3 或导入的原始音频。 |
| 剪贴视频片段 | 在轨道上选中一个或多个片段，Ctrl+X 剪切、Ctrl+C 复制、Ctrl+V 粘贴到播放头，Ctrl+D 在选中范围之后重复。Link 开启时随关联内容一起编辑。 |
| Excel 批注清单 | 在「设置 → 表格设置」选择导出列。画面预览嵌在表格中，含语音批注或导入音频批注的片段链接到 MP4，其余区间链接到 GIF，普通点批注链接到 PNG。在视频审阅页点 **导出 Excel** 后，默认生成文件夹，也可开启 ZIP 打包，一次发送工作簿及所选附件。 |
| 录屏与 GIF | 选择显示器、窗口或框选区域。在横向混音器中添加多个系统输出和一个可选麦克风；录制前按路调节增益、静音，成片为立体声混音；停止录制视频后直接进入审阅。临时录屏要在换片或退出前导出审阅包。GIF 最长 30 秒，无音频。 |
| 图片批注 | 空界面按 Ctrl+V 打开剪贴板图片；已有视频时，同一快捷键会添加图片批注。可缩放、平移，按原图分辨率复制或导出带批注 PNG；动图导出静态首帧。 |
| 播放与兼容 | 欢迎页点“打开文件”并选择视频后直接进入批注。需要生成批注预览时，FrameNote 和 Player 都会在窗口中央显示百分比进度，点下方的“取消加载”可中止。可手动切换到内置 VLC 播放原片；导出审阅包保留原始媒体。 |
| 发送到 REAPER | 单击图标发送到编辑光标，或拖到编排区按落点插入。剪辑后的视频、透明批注图和音频是同轨的独立 Item。点批注图使用完整图片源长度，区间批注图循环至区间末尾。批注保留颜色，另有一个以视频文件命名的总 Region。「设置 → REAPER集成」可查看安装状态、选择目录及安装或卸载脚本。 |

在原片播放器中，使用底部走带逐帧定位、复制当前帧或另存 PNG。图片查看器的画布直接位于菜单下方；按 **Ctrl+O** 打开文件，按 **Ctrl+S** 导出审阅包。审阅包、GIF、Excel 和 MP4 直接保存到「设置 → 通用 → 导出路径」，完成后可打开文件所在目录。

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

Recording settings and the mixer are on the left, and the annotation list and editor are on the right. The central timeline stacks video thumbnails, the mixed audio waveform, and annotations. Right-drag to select a time range. Drag either edge on the ruler to refine it. Enable **Loop selection** and press Play when ready. Double-click a Region on the timeline or in the sidebar to select its duration and start looping. Press **Esc** to clear the selection and turn off looping. FrameNote remembers the window size and maximized state between sessions.

1. Under **Assets** on the [latest release](https://github.com/Meidios/FrameNote/releases/latest), download the file ending in `win-x64-Setup.exe`. Setup includes .NET, VLC, FFmpeg and FFprobe. If Microsoft Edge WebView2 is missing, setup needs an internet connection. Your default video player is unchanged.
2. Drop in a video. Press **Space** to play or pause and **Left / Right** to step by frame. Select a drawing tool, choose a picture or start a voice recording to create a note at the playhead. Right-click the canvas for tools or right-click an annotation to edit, recolor, duplicate or delete it. **M** adds a text Mark. **I** and **O** set a Region. Use **Ctrl+Z** to undo and **Ctrl+Shift+Z** to redo. Export a review package before closing to keep your changes.
3. Choose **导出审阅包** to share an editable review. To send a still instead, use **复制当前帧** (copy the current video frame) or **复制批注图** (copy the annotated image), then paste it into chat. A [sample review package](examples/SampleReview.framenote) is included.

| Task | How it works |
|---|---|
| Select multiple clips | Drag from empty track space or the narrow strip above a clip. Hold **Shift** to start inside a clip. Drag a selected clip to move the group or drag consecutive clips past a neighbor’s midpoint to reorder them. **Delete** removes the selection and leaves a gap. **Ctrl+Z** undoes the whole edit. |
| Align timeline clips | Select a clip and click **贴到上一片段** to align its start with the previous clip’s end, or **贴到下一片段** to align its end with the next clip’s start on the same track. The first clip can align to zero and the last clip can align to the timeline end. Clips stay within the timeline when dragged. Clip and annotation edges snap to the playhead. Hold Alt to suspend snapping by default. |
| Adjust existing annotations | Drag a drawing or picture to move it. Drag an endpoint to change a line, a vertex to change an angle, or a corner to resize a box or picture. Double-click a text box to edit its text. |
| Move a Region or adjust its duration | Drag the middle of a Region label to move the whole interval and its attached voice clips. For a note without a voice recording, drag either edge to adjust the interval. Drag a Mark into a Region, or bring the edges together to restore a Mark. |
| Record a voice annotation | Enable a microphone and click the microphone tool to record. Default playback devices are muted until recording finishes. Click again to save an MP3 or press Esc to discard. Drag external audio onto the same track to attach the original file. Excel exports link to both recordings and imported files. |
| Excel review sheet | Choose the columns in **Settings → Spreadsheet**, then click **Export Excel** in video review. Previews are embedded in the workbook. Segments with recorded or imported annotation audio link to MP4. Other regions link to GIF and ordinary marks link to PNG. Share the generated folder or enable ZIP export to send one archive. |
| Screen capture and GIF | Record a display, window or selected region. Add multiple Windows outputs and an optional microphone in the horizontal mixer. Set gain and mute per source before recording one stereo mix. Stopping a video opens it for review; export the temporary recording before switching files or quitting. GIFs are silent and limited to 30 seconds. |
| Image annotation | Paste into an empty desk to open a clipboard image; paste while reviewing a video to attach a picture note. Zoom or pan without changing the dimensions of an exported PNG. Animated images export as a still of the first frame. |
| Playback compatibility | Opening a video from the welcome screen starts frame review. FrameNote prepares a temporary preview in the same window when needed. You can switch to VLC to play the original. Packages keep the original media. |
| REAPER handoff | Click to send to the edit cursor or drag to the arrange view. The edited video, transparent annotation overlays and audio remain independent items on one track. Point images use the full still length. Range images loop for the duration of their region. A region named after the video spans the import. Manage the script and its installation folder in Settings → REAPER integration. |

In the original-media player, use the bottom transport to step by frame, copy the current frame, or save it as PNG. In the image viewer, the canvas starts directly below the menus. Use **Ctrl+O** to open a file and **Ctrl+S** to export a review package.

Select images and videos together in Explorer and choose **用 FrameNote Player 打开** (Open with FrameNote Player) to browse them in one window. The thumbnail playlist starts collapsed for one file and expanded for multiple files. Drop more files onto the window to append them without interrupting playback.

### Build from source

On Windows x64, install an SDK that targets .NET 8, the Windows Desktop Runtime, WebView2 and the x64 Visual C++ runtime. Put FFmpeg and FFprobe on PATH or in a `tools` folder beside the app. Run the two PowerShell commands above from the repository root. Keep the resulting executable with its adjacent DLLs and folders.

For a self-contained installer, follow the [Windows packaging instructions](docs/windows-setup.md) and run `pwsh -File scripts/setup.ps1`. The installer and SHA-256 sidecar are written to `artifacts/release/`. See the [detailed user guide](docs/user-guide.en.md) for media compatibility, saving editable review packages, keyboard shortcuts and REAPER behavior, and the [verification notes](docs/verification.md) for tested scenarios and limitations.

### 第三方资源 / Third-party assets

[Noto Sans SC](src/FrameNote/ui/fonts/Noto-OFL.txt) · [Inter 4.1](src/FrameNote/ui/fonts/Inter-OFL.txt) · [Fluent System Icons](src/FrameNote/ui/icons/LICENSE.txt) · [Fontaudio REAPER glyph](src/FrameNote/ui/reaper-LICENSE.txt) · [Linkify.js](src/FrameNote/ui/vendor/linkifyjs/LICENSE)
