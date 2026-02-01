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

Próximos passos sugeridos:

- Implementar conversores para `BGR24`, `RGB24`, `NV12`, `YUY2`.
- Usar `WriteableBitmap` para reduzir alocações.
- Plugar um `IFrameProcessor` real na pipeline para reconhecimento de gestos.