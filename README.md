# Gleam

Preview de webcam com Avalonia + pipeline desacoplado para futuras etapas de reconhecimento.

## Como rodar

```bash
dotnet build Gleam.sln
dotnet run --project src/Gleam.UI
```

> Se o FlashCap ainda não estiver configurado, ao clicar **Start** a UI exibirá uma mensagem informando que o binding não está habilitado.

## Habilitar FlashCap

1. Confirme o **package id**/namespace correto do FlashCap (no momento está como `FlashCap` no NuGet).
2. Ajuste o adaptador em `src/Gleam.Engine/Capture/FlashCapCameraCapture.cs` preenchendo os TODOs com a API real.
3. Ative o símbolo de compilação `FLASHCAP` (ex: no `.csproj`):

```xml
<PropertyGroup>
  <DefineConstants>$(DefineConstants);FLASHCAP</DefineConstants>
</PropertyGroup>
```

## Formatos suportados

Atualmente o preview suporta apenas `BGRA32`. Caso receba outro formato, o sistema exibirá um erro com instruções.

## Plugins de processamento

O Engine agora expõe um pipeline modular via `IFrameProcessingPlugin`, permitindo adicionar
projetos externos que recebem os frames, processam dados e, opcionalmente, desenham overlays
sem modificar a UI ou a Engine existente.

### Como criar um plugin

Crie uma classe que implemente `IFrameProcessingPlugin` (exemplo em `SampleGesturePlugin`).

```csharp
public sealed class HandGesturePlugin : IFrameProcessingPlugin
{
    public string Name => "HandGesture";
    public bool IsEnabled { get; set; } = true;

    public FrameProcessResult? Process(in RawFrame frame)
    {
        // Seu processamento aqui.
        return new FrameProcessResult("Gesture: Pinch", 0.93f);
    }

    public void BuildOverlays(in RawFrame frame, OverlayScene scene)
    {
        // Desenhe overlays opcionais.
    }
}
```

### Como registrar plugins

Na UI, registre os plugins no `FrameProcessingRegistry`:

```csharp
_processingRegistry.Register(new HandGesturePlugin());
```

Para reutilizar módulos de overlay existentes, use o adaptador:

```csharp
_processingRegistry.Register(new OverlayModulePluginAdapter(_gridModule));
```

Próximos passos sugeridos:

- Implementar conversores para `BGR24`, `RGB24`, `NV12`, `YUY2`.
- Usar `WriteableBitmap` para reduzir alocações.
- Conectar plugins reais de reconhecimento (ex: gestos de mão) no `FrameProcessingRegistry`.