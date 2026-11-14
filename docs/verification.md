# Development build verification

Tested on Windows x64 with the .NET 8 Desktop Runtime, WebView2, FFmpeg/FFprobe 8.0.1 and an isolated REAPER 6.82 x64 portable instance. The REAPER test starts from a saved project, keeps an existing track and marker, and never edits a working session.

| Check | Observed result |
| --- | --- |
| Displayed frame vs. source timestamps | H.264 test fixtures at 30000/1001 fps (90 frames), 60 fps (180 frames), and variable frame rate (75 frames). The actual desk stepped backward and forward at the start, middle and end and returned to the expected presentation timestamps within 2 µs. |
| Notes | Created Chinese point and range notes with keyboard shortcuts, drew an arrow, edited, undid, sought from the list and enabled a range loop in the actual UI. |
| Persistence | Restarted the application and recovered notes. Moved the original video, opened only the exported package and edited the restored notes. Original and packaged media SHA-256 matched. This tests independence from the original path; it is not a clean-machine installer test. |
| REAPER | The UI-generated VFR package produced one VIDEO item starting at 60 s; marker at 60.466667 s and region at 60.733333–62.966667 s with original Chinese names. Assertions compare against the package within 2 µs. Actual referenced media was inside the test project's media folder. Duplicate insert was rejected. One undo restored the original project contents, and importing again succeeded. |
| Recording workflow | Automated the actual settings and save dialogs with an animated test window. Starting/stopping MP4 capture opened the result for review. The direct capture-to-GIF path produced a looping animated file. See the mixed-DPI region test below. Full-display capture, physical microphone and hotkey contention still need separate coverage. |
| GIF from a range | Exported a two-second section of a 1080p recording: 800 × 450, 24 frames, infinite loop. Separately exported a marked range via the desk and checked the animated frames and loop flag. |

## Mixed-DPI region recording

The settings now offer a separate region picker. The revised release UI test sampled a bright stripe and its grey background through the live picker, checked that both recording buttons were disabled before the first selection, cancelled a re-selection, then replaced it and started recording. On the 100% physical display and the 150% extended virtual display the outline was visible and click-through before and during capture, and gone after stopping. Both 642 × 358 recordings preserved the four reference bands exactly. An additional RGB check on the physical-display recording found no blue outline inside its outer three pixels.

The physical Mi Monitor stayed at 3440 × 1440, 100%, origin (0, 0). Windows extended the desktop to a Parsec virtual monitor at 2560 × 1600, origin (−2560, 432). Windows reported 120 and 144 DPI on that monitor at 125% and 150%. The tests selected one monitor per recording; they do not cover spanning a recording across monitors.

Before the fix, the 125% overlay had a physical client size of 3200 × 2000 instead of 2560 × 1600. Dragging from monitor-local (103, 137) to (745, 495) produced a 514 × 286 MP4. WPF resized the overlay on entering the secondary display; normalizing pointer coordinates against that oversized window shrank and shifted the crop.

| Final release build | Drags tested | Decoded size | Left, top, right, bottom error |
| --- | --- | --- | --- |
| Primary, 100% | Forward; reverse odd | 642 × 358 | 0, 0, 0, 0 px |
| Secondary, 125% | Forward; reverse odd | 642 × 358 | 0, 0, 0, 0 px |
| Secondary, 150% | Forward and reverse, each with even and odd dimensions | 642 × 358 | 0, 0, 0, 0 px |

The test draws a synthetic card, drags the real selection overlay, checks the outline on screen, stops recording and decodes the resulting MP4. Forward drags start at (103, 137); reverse drags start on the last addressable pixel near the bottom right. Odd drags span 643 × 359 physical pixels; the preview snaps the moving edge inward to 642 × 358. On every decoded frame sampled at 0.5 s, white bands occupied x=[12,20), [622,630) and y=[12,20), [338,346), exactly as drawn. The overlay client also matched the monitor bounds in every case.

To repeat on an already configured extended desktop, run `python tests/crop_ui.py artifacts/app/FrameNote.exe artifacts/checks DISPLAY11 reverse-odd`, substituting the active Windows display name. Supported drags: `forward`, `reverse`, `forward-odd`, `reverse-odd`. The test takes control of the pointer and covers the selected monitor with the card; it does not change Windows display settings.

## Original recording measurements (before the clock fix)

Recorded a synthetic moving window at 1920 × 1080 with periodic white flashes. A WASAPI player sent a short 1 kHz pulse every second to a selected virtual cable, which the recorder captured using Windows loopback. The synthesized flashes used a wall clock and sound used the output device clock; the result measures the **combined** playback, capture and timing path. It is not a calibrated estimate of recorder-only latency.

| Profile | Container duration | File size (decimal) | Video frames | Audio/visual events |
| --- | ---: | ---: | ---: | ---: |
| 1080p30, balanced | 120.067 s | 44.20 MB | 3,602 | 120 pulses and 120 flashes |
| 1080p60, high quality | 120.213 s | 101.60 MB | 7,212 | 120 pulses and 120 flashes |

The first vs. last ten events had median audio-minus-flash offsets of 152 vs. 178 ms at 30 fps and 125 vs. 227 ms at 60 fps. Frame rate in the container matched the requested rate. A fixed-rate output can contain repeated frames: these totals do not establish zero dropped source frames or game performance. The default virtual matrix endpoint produced a silent audio stream in a separate trial; the explicitly selected virtual cable produced nonzero decoded audio. Use the endpoint meters and check a short recording. **Frame-accurate live capture A/V sync is not verified**. Do not apply these machine-specific offsets to imported videos or REAPER markers.

## Clock isolation and retest

The test plays the same moving clip (SHA-256 `C9C6E45570B78F02AE7CCED6F1B7DB2265587C4AA3CD6FFFD82F03479EB5D7F4`) and overlays the same one-second flash/tone pattern. A second WASAPI loopback client timestamps the first above-threshold sample in each pulse using the packet QPC timestamp, not its arrival time. The source window logs the QPC of the UI flash instruction. The latter is not a photodiode or a compositor measurement. FFmpeg decodes the MP4, and the analyzer uses actual video frame and AAC block presentation timestamps. It matches 116 consecutive interior events in each run and excludes two seconds at each edge.

Positive means audio later. Values are first ten → last ten medians, ms. The residual is calculated per matched event *before* taking medians; medians of differences need not equal differences of medians.

| 120 s window capture, CABLE Input | Input: endpoint − UI flash | MP4: sound − visible flash | MP4 minus input |
| --- | ---: | ---: | ---: |
| Original path, new instrumented 60 fps control | +48.70 → +48.71 | +68.92 → +85.58 | +20.26 → +36.76 |
| Fixed, 30 fps balanced | −23.58 → −24.15 | −1.35 → −18.02 | +22.21 → −0.18 |
| Fixed, 60 fps high quality | +50.16 → +50.02 | +61.22 → +61.47 | +3.20 → +10.92 |

The original 60 fps artifact had six pulse-spacing increases of roughly 16.7 ms (after seconds 3, 28, 49, 58, 91 and 98, counting pulses from zero). This was a staircase in the *decoded audio itself*, not just a changing visual reference. In the instrumented baseline the endpoint period stayed near one second; this run reproduced only a one-frame change in the video and no cumulative change in the audio pulse period. Do not interpret the original 102 ms end-to-end increase as a repeatable device clock rate. The old artifact lacks independent packet logs at each of the six steps.

The old implementation's [audio collection and padding](https://github.com/sskodje/ScreenRecorderLib/blob/646383234187efd3854e6b1069612b80c3ab9981/ScreenRecorderLibNative/AudioManager.cpp) runs inside the video loop. It can pad a frame when input is considered inactive. The [timeline](https://github.com/sskodje/ScreenRecorderLib/blob/646383234187efd3854e6b1069612b80c3ab9981/ScreenRecorderLibNative/TimelineManager.cpp) advances audio by sample count plus a drift adjustment; the [writer](https://github.com/sskodje/ScreenRecorderLib/blob/646383234187efd3854e6b1069612b80c3ab9981/ScreenRecorderLibNative/OutputManager.cpp) does not place packets using their endpoint timestamps. This allows an inserted frame of silence to shift later real audio. It is consistent with the historical steps; which condition fired in each instance was not logged.

FrameNote now records each endpoint independently using [the WASAPI packet timestamp](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudiocaptureclient-getbuffer) and stores timed PCM in temporary Matroska files. The video recording-start notification anchors audio to the same monotonic clock, without fitting a delay to the fixture. FFmpeg resamples against the actual packet timestamps, handles gaps, mixes the selected endpoints, encodes AAC and stream-copies video. A separate mux regression injected 0.3% clock skew on one endpoint and a 200 ms packet gap on the other: first/last reference errors were −2.7/−3.1 ms and 0.0/0.0 ms respectively. Silent input and unchanged video bitstream also passed.

The fixed MP4 drift was within one frame in both two-minute runs. That is not a promise of subframe absolute sync: endpoint latency, display timing and the video-start notification can affect the intercept. Source audio and the visual timer deliberately run independently; their start phase can differ across sessions. No fixed latency correction was applied. Physical microphone, longer runs, and game-load measurements remain open.

For the clock-isolation report run `python tests/measure_clocks.py <out-dir> 30 60` after the long test below. For the synthetic skew/gap regression run `python tests/check_clock_recovery.py <path-to-CaptureCheck.exe>`. Current sample timing evidence is in [the matched-event table](capture-sync-events.csv).

## Repeat the checks

- Build with `scripts/build.ps1`. Run `tests/make-fixtures.ps1` to generate synthetic clips with visible frame numbers.
- The desktop checks require Python, Playwright, pywinauto, pywin32 and Pillow. Run `python tests/review_ui.py artifacts/app/FrameNote.exe artifacts/checks`, then `python tests/capture_ui.py artifacts/app/FrameNote.exe artifacts/checks`. They launch only the app under test, use a test-specific WebView profile, and leave artifacts in unique directories. The test-only debugging endpoint is enabled by the test process environment.
- For REAPER, place a separate portable test install under `artifacts/reaper-test` with video decoding support. Run `python tests/run_reaper.py artifacts/reaper-test artifacts/checks "artifacts/app/reaper/FrameNote - Import review.lua"`. The action is called by a small test driver inside REAPER; the file chooser itself is not automated.
- For recording, use a new empty output directory and run `dotnet run --project tests/CaptureCheck -- artifacts/checks/60.mp4 <out-dir> --system-audio --virtual-cable --long`. The fixture flashes and plays a test tone. The virtual-cable option chooses an installed CABLE Input device if available; otherwise it uses the default playback device. Inspect the files with `python tests/check_sync.py <out-dir>/capture-30.mp4 <out-dir>/capture-60.mp4`.



## Sidebar capture and direct REAPER sends

The release WebView2 UI recorded two synthetic-window MP4s and a looping GIF through separate header buttons, with no setup dialog or pre-record save dialog. The 100% physical-monitor region regression still decoded a 642 × 358 frame with all four reference bands aligned. The monochrome REAPER glyph was inspected in a rendered desktop screenshot.

Run `python tests/send_reaper_ui.py artifacts/app/FrameNote.exe artifacts/checks artifacts/reaper-test` after the review UI fixtures have generated latest.txt. The test chooses a new script folder via the actual installation dialog, uses a disposable FrameNote data directory and starts an isolated REAPER 6.82 portable project. On the second selected track at 17.250000 s it sent an ordinary video, a temporary recording, and an opened review package. The track count stayed at two. All three media paths remained playable after FrameNote closed; only the latter two used project-owned copies. The package created a Chinese marker at 17.716667 s and a Chinese region from 17.983333 to 20.216667 s. Undo removed only the package item and its notes.
