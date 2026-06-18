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

# FloatingCam — específico do projeto

App **Windows-only**: caixa flutuante com a webcam (always-on-top, sem bordas),
movível/redimensionável, para gravar aulas no OBS via captura de tela.

## Stack
- **C# / .NET 10 (WPF)**, `net10.0-windows`. Projeto em `FloatingCam/`.
- Vídeo via **OpenCvSharp4** (backend **DirectShow**). Conversão de frame com
  `OpenCvSharp4.WpfExtensions`.

## Comandos
O `dotnet` pode não estar no PATH deste ambiente; use o caminho completo:
`"C:\Program Files\dotnet\dotnet.exe"`.

```powershell
# Build / run
dotnet build FloatingCam -c Release
dotnet run --project FloatingCam -c Release

# Publicar .exe ÚNICO (libs nativas embutidas) — é o que o CI e o instalador usam
dotnet publish FloatingCam -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

## Como verificar (não há testes automatizados)
- App de GUI: rode o `.exe` e confira o **log** em `%Temp%\floatingcam.log`
  (enumeração de câmeras, resolução/fourcc, FPS medido, exceções).
- Configurações persistidas em `%AppData%\FloatingCam\settings.json`
  (tamanho, posição, câmera, espelho, formato, zoom, centro do enquadramento).
- **Instância única** (mutex `FloatingCam.SingleInstance`): pare o app antes de
  rodar outra instância nos testes, senão a segunda encerra sozinha e/ou o arquivo
  fica travado ao republicar. `Get-Process FloatingCam | Stop-Process -Force`.

## Armadilhas já resolvidas (não regredir)
- **Enumeração de câmeras** (`CameraEnumerator.cs`): interop COM do DirectShow. O
  método `IEnumMoniker.Next` PRECISA do atributo `[Out]` + `ArraySubType=Interface`,
  senão a lista volta vazia e o app abre preto.
- **Enquadramento** (zoom + reposição): há UMA fonte de verdade,
  `MainWindow.CropRect(zoom, centerX, centerY)` em coords normalizadas da câmera.
  A janela aplica via `ImageBrush.Viewbox`; o seletor (`FramingWindow`) desenha a
  moldura com o MESMO `CropRect`. Não reintroduzir `UniformToFill` + transform
  (causava divergência seletor↔janela e distorção).
- **Recorte recalcula quando o 1º frame chega** (em `RenderFrame`): a resolução só
  é conhecida aí; sem recalcular, a imagem distorce (achatamento).
- **Resolução adaptativa**: tenta MJPG 720p; se a câmera não aceitar MJPG (fica em
  formato cru e satura o USB), cai para 640x360 para manter ~30fps.

## Distribuição
- CI em `.github/workflows/release.yml`: criar tag `vX.Y.Z` e dar push gera a
  Release com o `FloatingCam.exe` único. Não cria release em push normal de `master`.
- Instalado localmente em `%LocalAppData%\Programs\FloatingCam\` com atalho no
  Menu Iniciar.
- O `.exe` não é assinado → SmartScreen mostra "Editor desconhecido" na 1ª execução.
