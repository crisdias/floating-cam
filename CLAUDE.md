# CLAUDE.md

Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.

---

# FloatingCam — project-specific

**Windows-only** app: a floating, borderless, always-on-top webcam box that can be
moved/resized, for recording lessons in OBS via screen capture.

## Stack
- **C# / .NET 10 (WPF)**, `net10.0-windows`. Project lives in `FloatingCam/`.
- Video via **OpenCvSharp4** (**DirectShow** backend). Frame conversion via
  `OpenCvSharp4.WpfExtensions`.

## Commands
`dotnet` may not be on PATH in this environment; use the full path:
`"C:\Program Files\dotnet\dotnet.exe"`.

```powershell
# Build / run
dotnet build FloatingCam -c Release
dotnet run --project FloatingCam -c Release

# Publish a SINGLE .exe (native libs bundled) — what the CI and installer use
dotnet publish FloatingCam -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

## How to verify (no automated tests)
- It's a GUI app: run the `.exe` and check the **log** at `%Temp%\floatingcam.log`
  (camera enumeration, resolution/fourcc, measured FPS, exceptions).
- Settings persist in `%AppData%\FloatingCam\settings.json`
  (size, position, camera, mirror, shape, zoom, framing center).
- **Single instance** (mutex `FloatingCam.SingleInstance`): stop the app before
  launching another instance in tests, otherwise the second one exits by itself
  and/or the file is locked when republishing.
  `Get-Process FloatingCam | Stop-Process -Force`.

## Pitfalls already fixed (do not regress)
- **Camera enumeration** (`CameraEnumerator.cs`): DirectShow COM interop. The
  `IEnumMoniker.Next` method REQUIRES the `[Out]` attribute + `ArraySubType=Interface`,
  otherwise the list comes back empty and the app opens black.
- **Framing** (zoom + reposition): there is ONE source of truth,
  `MainWindow.CropRect(zoom, centerX, centerY)` in normalized camera coordinates.
  The window applies it via `ImageBrush.Viewbox`; the selector (`FramingWindow`)
  draws the guide rect from the SAME `CropRect`. Do not reintroduce `UniformToFill`
  + transform (it caused selector↔window mismatch and distortion).
- **Crop is recomputed when the 1st frame arrives** (in `RenderFrame`): the
  resolution is only known then; without recomputing, the image distorts (squish).
- **Adaptive resolution**: requests MJPG 720p; if the camera rejects MJPG (stays in
  raw format and saturates USB), it falls back to 640x360 to keep ~30fps.

## Distribution
- CI in `.github/workflows/release.yml`: pushing a `vX.Y.Z` tag builds the Release
  with the single `FloatingCam.exe`. A normal `master` push does NOT create a release.
- Installed locally under `%LocalAppData%\Programs\FloatingCam\` with a Start Menu
  shortcut.
- The `.exe` is unsigned → SmartScreen shows "Unknown publisher" on first run.
