# Windows setup and updates

Build on Windows x64 with the .NET 8 targeting packs, an SDK capable of publishing .NET 8, Inno Setup 6 and Visual Studio's redistributable x64 VC143 CRT. Run `pwsh -File scripts/setup.ps1`. The self-contained Setup and SHA-256 sidecar appear under `artifacts/release`.

The packaging script downloads Gyan's pinned FFmpeg 8.0.1 full shared ZIP on the first build and checks its SHA-256 before extracting it. For an offline build, put the same `ffmpeg-8.0.1-full_build-shared.zip` under `artifacts/installer-deps` alongside the WebView2 bootstrapper. Setup bundles ffmpeg, ffprobe, their shared DLLs and license. The package uses solid LZMA2 compression. [Upstream FFmpeg release and checksums](https://github.com/GyanD/codexffmpeg/releases/expanded_assets/8.0.1)

The bundled Chinese installer translation is from Inno Setup tag is-6_7_3, Files/Languages/Unofficial/ChineseSimplified.isl. A Microsoft-signed WebView2 bootstrapper only runs when the Evergreen runtime is absent. In that case setup needs a network connection. Setup uses the current user, with no elevation for FrameNote.

The project Version in FrameNote.csproj drives the About dialog, release tag, setup version and filename. Keep user-facing Chinese release notes in docs/releases/vX.Y.Z.md. To publish, stage a draft GitHub release in Meidios/FrameNote with tag vX.Y.Z, the matching file as the release body, the Setup EXE and its SHA-256 sidecar. Confirm that the asset digest reported by GitHub matches the local SHA-256. Only then publish as the latest stable release. Verify the public download and discovery by an older installed version. Packaging does not publish anything. The updater uses GitHub's HTTPS API asset digest; the sidecar is for independent manual verification.

The installation registers .framenote as its own document type, including ProgID, shell open command, document icon and per-user application capabilities. To change a file association, choose **文件 → 设置 → 打开默认应用设置** and confirm the preferred application in Windows. Common image types are registered as optional Open with candidates; no image UserChoice or default extension value is changed. Uninstall restores the prior extension default and FrameNote application commands (including the Explorer-generated portable FrameNote handler) if they are still ours. User projects and preferences live outside the installation directory and are preserved.

Run the offline updater checks with `dotnet run --project tests/UpdateCheck -c Release`. On a disposable Windows user profile without FrameNote installed, run `python tests/setup_acceptance.py <previous-setup> <new-setup>` (Playwright, pywinauto, pywin32, psutil). The old setup for the 0.2.0 acceptance run was a local fixture containing the actual 0.1.0 development executable, not a public release.


