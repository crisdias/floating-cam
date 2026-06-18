# FloatingCam

> ⚠️ **Feito com "vibe coding" pelo Claude Opus 4.8.** Este projeto foi escrito
> inteiramente por IA (modelo Opus 4.8 da Anthropic), de forma conversacional, a
> partir de pedidos em linguagem natural. O código foi testado e funciona, mas
> use por sua conta e risco. Contribuições e revisões humanas são bem-vindas. 🤖

Uma caixinha flutuante com a imagem da sua webcam, **sempre no topo**, que você
pode **mover** e **redimensionar** livremente. Ideal para gravar aulas e
tutoriais: você deixa o OBS capturar a tela inteira (o app que está mostrando +
o seu rosto) e posiciona a câmera em um canto, sem cobrir o conteúdo.

## Funciona assim

- A janela é **sem bordas**, fica **sempre por cima** dos outros programas e
  aparece na barra de tarefas.
- **Escolha qual webcam usar** (mostra os nomes reais das câmeras).
- **Espelhar** a imagem, deixar com **cantos arredondados** ou em **formato
  circular** — tudo pelo menu de clique direito.
- **Lembra suas configurações** entre sessões: tamanho, posição, câmera, espelho
  e formato.

## Requisitos

- **Windows 10/11** (64 bits).
- Uma webcam. 🙂
- Para **rodar** a versão publicada (`dist/`): nada — o executável é
  autossuficiente.
- Para **compilar**: [.NET SDK 10](https://dotnet.microsoft.com/download) ou superior.

## Como usar

1. Abra o `FloatingCam.exe`.
2. **Clique com o botão direito** sobre a imagem para abrir o menu:
   - **Webcam** → escolha a câmera.
   - **Espelhar** → efeito espelho.
   - **Cantos arredondados** / **Formato circular** → estilo da caixa.
   - **Fechar** → encerra o app.
3. **Mover:** clique e arraste sobre a imagem.
4. **Redimensionar:** arraste as bordas/cantos. A alça de redimensionar (canto
   inferior direito) só aparece quando a janela está em **foco** — clique no app
   para mostrá-la; ela some ao clicar em outro lugar.

> O app roda em **instância única**: abrir de novo enquanto já está aberto não
> cria uma segunda janela.

## Fluxo com o OBS

- No OBS, use uma fonte de **Captura de Tela (Display Capture)** — ela pega tudo,
  inclusive a caixinha flutuante por cima do que você está mostrando.
- Posicione a câmera num canto livre e redimensione como preferir.

## Compilar a partir do código

```powershell
# Rodar em modo de desenvolvimento
dotnet run --project FloatingCam -c Release

# Gerar um .exe autossuficiente (não exige .NET instalado para rodar)
dotnet publish FloatingCam -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -o dist
```

## Detalhes técnicos

- **Linguagem/stack:** C# / .NET 10 (WPF).
- **Captura de vídeo:** [OpenCvSharp4](https://github.com/shimat/opencvsharp)
  (backend DirectShow).
- **Enumeração de câmeras:** interop COM com o DirectShow, para listar os nomes
  reais dos dispositivos.
- As configurações ficam em `%AppData%\FloatingCam\settings.json`.
- Um log de diagnóstico é gravado em `%Temp%\floatingcam.log` (útil para depurar).

## Licença

MIT — sinta-se livre para usar, modificar e distribuir.
