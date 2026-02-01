using System;
using System.Collections.Generic;
using Gleam.Engine.Frames;
using Gleam.Engine.Overlays;

namespace Gleam.Engine.Modules;

public sealed class AnimatedShapesOverlayModule : IOverlayModule
{
    private static readonly ColorRgba[] Palette =
    [
        new ColorRgba(0, 200, 255, 210),
        new ColorRgba(255, 120, 50, 200),
        new ColorRgba(120, 255, 120, 200),
        new ColorRgba(255, 80, 180, 200),
        new ColorRgba(250, 230, 80, 200)
    ];

    public string Name => "AnimatedShapes";

    public bool IsEnabled { get; set; } = false;

    public void BuildOverlays(in RawFrame frame, OverlayScene scene)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return;
        }

        var width = frame.Width;
        var height = frame.Height;
        var centerX = width / 2f;
        var centerY = height / 2f;
        var time = (float)(frame.TimestampNs / 1_000_000_000.0);
        var maxRadius = MathF.Min(width, height) * 0.35f;

        var lineStroke = new OverlayStroke(1f, new ColorRgba(255, 255, 255, 110));
        const int lineCount = 24;
        for (var i = 0; i < lineCount; i++)
        {
            var phase = (i / (float)lineCount + time * 0.12f) % 1f;
            var x = phase * width;
            scene.Add(new OverlayLine(new OverlayPoint(x, 0), new OverlayPoint(x, height), lineStroke));

            var yPhase = (i / (float)lineCount + time * 0.17f) % 1f;
            var y = yPhase * height;
            scene.Add(new OverlayLine(new OverlayPoint(0, y), new OverlayPoint(width, y), lineStroke));
        }

        const int circleCount = 30;
        for (var i = 0; i < circleCount; i++)
        {
            var angle = time * 0.9f + i * 0.45f;
            var orbit = maxRadius * (0.25f + 0.75f * (i / (float)circleCount));
            var radius = 6f + 10f * (0.6f + 0.4f * MathF.Sin(time * 1.7f + i));
            var cx = centerX + MathF.Cos(angle) * orbit;
            var cy = centerY + MathF.Sin(angle * 1.3f) * orbit;
            var color = Palette[i % Palette.Length];
            var stroke = new OverlayStroke(2f, color);
            var fill = new OverlayFill(new ColorRgba(color.R, color.G, color.B, 50));
            scene.Add(new OverlayCircle(new OverlayPoint(cx, cy), radius, stroke, fill));
        }

        const int rectangleCount = 16;
        for (var i = 0; i < rectangleCount; i++)
        {
            var size = 18f + i * 6f;
            var drift = maxRadius * 0.15f * MathF.Sin(time * 0.8f + i);
            var angle = time * 0.45f + i * 0.6f;
            var offsetX = MathF.Cos(angle) * drift;
            var offsetY = MathF.Sin(angle * 1.2f) * drift;
            var topLeft = new OverlayPoint(centerX - size / 2f + offsetX, centerY - size / 2f + offsetY);
            var color = Palette[(i + 2) % Palette.Length];
            var stroke = new OverlayStroke(1.5f, color);
            var fill = new OverlayFill(new ColorRgba(color.R, color.G, color.B, 35));
            scene.Add(new OverlayRectangle(topLeft, size, size, stroke, fill));
        }

        const int wavePoints = 42;
        var points = new List<OverlayPoint>(wavePoints);
        for (var i = 0; i < wavePoints; i++)
        {
            var x = i / (float)(wavePoints - 1) * width;
            var y = centerY
                    + MathF.Sin(time * 2.2f + i * 0.35f) * height * 0.14f
                    + MathF.Sin(time * 0.7f + i * 0.12f) * 12f;
            points.Add(new OverlayPoint(x, y));
        }

        scene.Add(new OverlayPolyline(points, new OverlayStroke(2f, new ColorRgba(0, 210, 255, 180))));
    }
}