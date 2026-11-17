# 更新日志 · Changelog

版本按从新到旧排列。早期条目来自随应用分发的离线更新记录，不代表每个版本都曾在 GitHub 上发布安装包。

Versions are listed newest first. Earlier entries summarize the offline release history shipped with the app; they do not imply that each version had a public GitHub installer.

## 0.6.5

**中文**

- 图片查看器和视频播放器共用带缩略图的播放列表。单个文件默认收起列表，多选默认展开；拖入其他文件会追加并展开列表，当前媒体继续显示。
- 在资源管理器里右键选择「用 FrameNote Player 打开」，可在同一窗口打开多张图片、多个视频或混合选择。优化了大图片和本地视频的首次显示。
- VBMatrix 回送可直接作为系统输出录制；录制中的电平表显示实际采集信号。修复了图片查看器切图箭头，并调整了按钮透明度。

**English**

- The image viewer and video player share a thumbnail playlist. It starts collapsed for one file and expanded for a selection. Dropping more files appends them without interrupting the current item.
- The Explorer context menu opens selected images and videos together in FrameNote Player. Large images and local videos appear sooner.
- VBMatrix returns can be recorded as system outputs. Meters show captured samples while recording. Image navigation arrows now render correctly and use translucent buttons.

## 0.6.4

**中文**

- FrameNote Player 在单个窗口里直接播放视频，标题为「FrameNote Player - 视频名」。关闭窗口即可退出；进入批注时仍使用同一窗口。
- 图片查看器新增左右切图箭头。复制批注图时，图片下方会附上批注编号、颜色及完整正文；没有批注时保持原尺寸。复制与另存按钮改为与主程序一致的图标。

**English**

- FrameNote Player opens videos directly in one window titled “FrameNote Player - filename”. Closing it exits the player; entering review uses the same window.
- The image viewer adds previous and next arrows. Copying an annotated image appends note numbers, colors and full text below the image; unannotated copies keep their original dimensions. Copy and Save As now use the same icons as the main application.

## 0.6.3

**中文**

- 时间线支持 Ctrl＋滚轮以鼠标位置为中心缩放，Shift＋滚轮横移；缩放不再挤动预览或批注轨。点击时间尺、视频或音频轨时保留播放状态；点击 Mark／Region 会暂停，空格可继续播放。Region 两端与区间块连为一体。
- 批注详情固定在右栏底部，自动保存；每张卡片可独立删除，Ctrl＋Z 撤销，Ctrl＋Shift＋Z 重做。时间线上的「图片批注」可用 P 打开。
- 复制当前帧或另存 PNG 时保留图形和参考图，并在画面下方列出当前帧的批注编号、颜色、时间与完整正文。没有批注的帧保持原尺寸。
- 单击 REAPER 按钮可安装或自动更新脚本，并按原方式发送；也可把按钮拖到 REAPER 编排区，在落点插入。审阅包的 Marker 和 Region 保留各自颜色。
- Windows「打开方式」新增 FrameNote Player，可直接播放原片；FrameNote 继续用于审阅。安装不会更改现有默认应用。审阅音量集中在底部播放栏，录制混音器的电平表随画面刷新，界面强调色固定为 #A8DDFD。

**English**

- Use Ctrl+wheel to zoom the timeline around the pointer and Shift+wheel to pan without resizing the preview or annotation track. Seeking on the ruler, video or audio preserves playback state. Clicking a Mark or Region pauses; Space resumes. Region end caps now join the block.
- Annotation details stay pinned below the list and edits save automatically. Delete individual cards, undo with Ctrl+Z, and redo with Ctrl+Shift+Z. Press P on the timeline to add a picture annotation.
- Copied frames and saved PNGs include drawings and reference images, followed by the number, color, time and full text of each active note. Unannotated frames retain their original dimensions.
- Click the REAPER button to install or refresh the receiver and send at the edit cursor, or drag it onto the arrange view to insert at the drop position. Review packages preserve marker and region colors.
- Windows Open with now offers FrameNote Player for original video playback alongside FrameNote for review, without changing existing defaults. Review volume stays on the bottom transport, recording meters refresh with the display, and the interface uses a fixed #A8DDFD accent.

## 0.6.2

**中文：** Windows 的视频打开方式新增 **FrameNote Player**，用于直接播放原片；**FrameNote** 保留原有审阅流程。安装不更改现有的视频默认应用。视频审阅只保留底部播放栏的音量控制。

**English:** Windows now offers **FrameNote Player** for original video playback alongside **FrameNote** for review. Setup preserves existing video defaults. Review volume controls now appear only in the bottom transport.

## 0.6.1

**中文**

- 原片播放器的底部走带新增前后逐帧、毫秒时间码、复制当前帧、另存 PNG、静音、音量和全屏。变帧率视频按原片帧时间戳定位，逐帧时复制和保存的是画面上显示的那一帧。移除了顶部重复的音量条、文件名后的 VLC 字样和常驻提示。
- 图片查看器移除了「FrameNote · 图片审阅」工具栏，画布和批注列表直接从「文件／帮助」菜单下方开始。仍可用 Ctrl+O 打开文件，用 Ctrl+S 导出审阅包。视频审阅的顶部工具栏保持不变。

**English**

- The original-media player now has previous/next frame controls, millisecond timecodes, copy frame, save as PNG, mute, volume, and fullscreen on its bottom transport. Frame stepping follows the source timestamps, including for variable-frame-rate video; copying or saving a stepped frame uses the displayed pixels. The duplicate volume slider, VLC label, and persistent hint have been removed.
- The image viewer now places the canvas and annotations directly below the File and Help menus. Ctrl+O still opens files and Ctrl+S exports a review package. The video review toolbar is unchanged.

## 0.6.0

**中文**

- 时间线分为视频缩略图、混合音频波形和批注三轨。点批注标出单帧，区间批注的两端分别标出起止位置。
- 中栏播放条集中放置逐帧、倍速、所选区间循环、当前帧操作、音量和全屏。左栏集中录制设置与横向混音器；右栏在批注列表下方编辑。
- 重新打开时恢复上次的窗口大小或最大化状态。从最小化或视频全屏退出不会覆盖普通窗口尺寸。

**English**

- The timeline now has three tracks: video thumbnails, the mixed audio waveform, and annotations. Point markers indicate a single frame; range markers indicate both endpoints.
- The central playback bar groups frame stepping, playback speed, looping the selected range, frame actions, volume, and fullscreen. Recording controls and the horizontal mixer sit on the left; annotations can be edited below the list on the right.
- FrameNote restores the previous window size or maximized state at startup. Closing from minimized or fullscreen video preserves the normal window size.

## 0.5.1

**中文：** 修复某些视频撑高预览区的问题。录屏和视频现在随窗口等比缩放，整幅画面、播放栏和时间线可同时看见。

**English:** Video previews now fit the available window without pushing playback controls or the timeline below the viewport.

## 0.5.0

**中文：** 录屏可以同时采集多个 Windows 输出设备和一个麦克风。横向混音器为每路显示电平，支持录制前分别调节增益和静音。成片保留一条混合立体声音轨。

**English:** Record multiple Windows output devices and an optional microphone together. Each horizontal mixer strip has a peak meter, gain and mute. Set the mix before recording; the MP4 contains one stereo mix.

欢迎页现在只显示一个大号 **FRAMENOTE** 标题，提示和打开按钮的位置不变。

The welcome screen now has a single **FRAMENOTE** heading. The hint and Open button stay in place.

## 0.4.4

**中文：** 顶部和播放栏的十项操作改为图标，保留中文悬停提示和键盘操作，并区分播放／暂停、录制／停止状态。

**English:** Ten capture, file and playback actions now use icons with Chinese tooltips and accessible names. Play/pause and record/stop reflect the current action.

## 0.4.3

**中文：** 调整主窗口原生控件的渲染方式，处理深色菜单和音量栏出现白带的问题；保留原有视频渲染路径。新增「文件 → 打开文件…／退出」。

**English:** Adjusted rendering of the main window's native controls to address a white band across the dark menu and volume bar. Video rendering is unchanged. Added Open File and Exit to the new File menu.

## 0.4.2

**中文：** 窗口标题栏、帮助菜单、更新记录、网页工作区和 VLC 播放控件统一跟随 Windows 深浅主题。重新绘制按钮、下拉框、滚动条、音量和时间线滑杆；随包提供 Noto Sans SC 与 Cascadia Mono。工作区布局、快捷键和审阅包格式保持不变。

**English:** The native window chrome, Help menu, release history, web workspace and VLC controls share the Windows light/dark theme. Buttons, drop-downs, scrollbars and sliders have matching styles. Noto Sans SC and Cascadia Mono are bundled. Workspace layout, shortcuts and review-package format are unchanged.

[本版完整说明 / Full release notes](docs/releases/v0.4.2.md)

## 0.4.1

**中文：** 可将带批注的图片复制到剪贴板或保存为 PNG。导出保留原图尺寸、透明度、编号、箭头、框和附图，不受缩放或平移影响；动图导出首帧。

**English:** Copy an annotated image to the clipboard or save it as a PNG at its source dimensions, preserving transparency, numbered badges, arrows, boxes and attached pictures. Zoom and pan do not affect output. Animated images export their first frame.

## 0.4.0

**中文：** 可在文件图片和剪贴板截图上编辑文字、图形及图片批注。标记跟随图片缩放和平移；文件图片支持本地草稿，审阅包可交付原图与可编辑批注。看图时隐藏音量控件。

**English:** Annotate image files and clipboard screenshots with text, shapes and pictures. Marks follow zoom and pan. Image files support local drafts; review packages carry the original and editable notes. Volume controls are hidden in image mode.

## 0.3.1

**中文：** 图片支持以光标为中心缩放、放大后拖动，以及用方向键按文件名自然顺序浏览同目录图片。

**English:** Zoom images around the pointer, pan when enlarged, and browse neighboring images in natural filename order with the arrow keys.

## 0.3.0

**中文：** 视频打开后自动播放，拖动进度保留播放或暂停状态；点击批注会暂停定位。审阅与 VLC 共用音量并记住上次设置。新增静态图、动图和剪贴板图片查看，以及 Windows 图片“打开方式”入口。

**English:** Videos autoplay; scrubbing preserves the playback state and selecting a note pauses at its start. Review and VLC share a persistent volume setting. Added still, animated and clipboard image viewing, plus Windows Open with registration for images.

## 0.2.0

**中文：** 安装后可双击审阅包。帮助菜单提供离线帮助、版本记录和更新检查。内置 VLC 可播放原片，并按需进入逐帧批注。

**English:** Installed builds can open review packages by double-click. The Help menu adds offline help, release history and update checks. The bundled VLC player opens original media and hands it off to frame review on request.

## 0.1.0

**中文：** 初版提供逐帧视频审阅、点与区间批注、审阅包导出、REAPER 交付、录屏和 GIF 录制。

**English:** The initial development build introduced frame stepping, point and range notes, portable review packages, REAPER handoff, screen recording and GIF capture.
