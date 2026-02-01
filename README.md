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

O Engine agora expõe um pipeline modular via `IFrameProcessingPlugin`, carregado por reflection.
Plugins podem estar no próprio código ou em bibliotecas dentro de `/plugins` na raiz do executável.
Cada plugin recebe callbacks de ciclo de vida para iniciar, atualizar e encerrar o processamento.

### Como criar um plugin

Crie uma classe que implemente `IFrameProcessingPlugin` e marque com `PluginMetadata`.

```csharp
public sealed class HandGesturePlugin : IFrameProcessingPlugin
{
    public bool IsEnabled { get; set; } = true;

    public void OnStartCapture(PluginStartContext context) { }

    public void OnUpdateCapture(PluginFrameContext context)
    {
        // Seu processamento aqui.
        // context.Scene.Add(...); // Desenhar overlays opcionais.
    }

    public void OnEndCapture(PluginEndContext context) { }
}
```

### Carregamento e pasta /plugins

- Plugins internos são descobertos automaticamente no assembly principal.
- DLLs externas são buscadas em `./plugins` (pasta ao lado do executável).
- Tipos válidos precisam ter `PluginMetadataAttribute` e implementar `IFrameProcessingPlugin`.

Próximos passos sugeridos:

- Implementar conversores para `BGR24`, `RGB24`, `NV12`, `YUY2`.
- Usar `WriteableBitmap` para reduzir alocações.
- Conectar plugins reais de reconhecimento (ex: gestos de mão) no `PluginHandler`.