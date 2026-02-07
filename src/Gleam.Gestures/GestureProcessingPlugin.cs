using Gleam.Engine.Processing;

namespace Gleam.Gestures;

[PluginMetadata("GestureProcessing", "Plugin básico para detecção de gestos e exibição de logs", "1.0.0")]
public class GestureProcessingPlugin : IFrameProcessingPlugin
{
    private int _frameCount;

    public bool IsEnabled { get; set; } = true;

    public void OnStartCapture(PluginStartContext context)
    {
        PluginLogger.Log("GestureProcessingPlugin: Captura iniciada.");
        _frameCount = 0;
    }

    public void OnUpdateCapture(PluginFrameContext context)
    {
        _frameCount++;

        // Exibe log a cada 100 frames para evitar sobrecarregar o log
        if (_frameCount % 100 == 0)
        {
            PluginLogger.Log($"GestureProcessingPlugin: Processando frame {_frameCount}. Resolução: {context.Frame.Width}x{context.Frame.Height}");
        }
    }

    public void OnEndCapture(PluginEndContext context)
    {
        PluginLogger.Log($"GestureProcessingPlugin: Captura finalizada. Total de frames processados: {_frameCount}");
    }
}