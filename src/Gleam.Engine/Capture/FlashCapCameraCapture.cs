using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlashCap;
using Gleam.Engine.Frames;

namespace Gleam.Engine.Capture;

public sealed class FlashCapCameraCapture : ICameraCapture
{
    private readonly object _sync = new();
    private CaptureDevices? _devices;
    private CaptureDeviceDescriptor? _descriptor;
    private VideoCharacteristics? _characteristics;
    private CaptureDevice? _device;
    private CancellationTokenSource? _cts;
    private Task? _startTask;

    public event EventHandler<RawFrame>? FrameArrived;

    public void Start()
    {
        lock (_sync)
        {
            if (_startTask is { IsCompleted: false })
            {
                return;
            }

            _cts = new CancellationTokenSource();
            _startTask = Task.Run(() => StartAsync(_cts.Token));
        }
    }

    public void Stop()
    {
        CaptureDevice? device;
        CancellationTokenSource? cts;
        Task? startTask;
        CaptureDevices? devices;

        lock (_sync)
        {
            device = _device;
            cts = _cts;
            startTask = _startTask;
            devices = _devices;
            _device = null;
            _cts = null;
            _startTask = null;
            _descriptor = null;
            _characteristics = null;
            _devices = null;
        }

        cts?.Cancel();
        if (startTask != null)
        {
            try
            {
                startTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
            }
        }
        if (device != null)
        {
            try
            {
                device.StopAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }

            try
            {
                device.DisposeAsync().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
        }

        if (devices is IDisposable disposableDevices)
        {
            disposableDevices.Dispose();
        }

        cts?.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnFrameArrived(RawFrame frame)
    {
        FrameArrived?.Invoke(this, frame);
    }

    private async Task StartAsync(CancellationToken ct)
    {
        _devices = new CaptureDevices();
        _descriptor = _devices.EnumerateDescriptors().FirstOrDefault(d => d.Characteristics.Length > 0)
            ?? throw new InvalidOperationException("No capture devices found.");

        _characteristics = SelectPreferredCharacteristics(_descriptor)
            ?? throw new InvalidOperationException("No valid video characteristics found.");

        _device = await _descriptor.OpenAsync(
            _characteristics,
            OnPixelBufferArrivedAsync,
            ct).ConfigureAwait(false);

        await _device.StartAsync(ct).ConfigureAwait(false);
    }

    private static VideoCharacteristics? SelectPreferredCharacteristics(CaptureDeviceDescriptor descriptor)
    {
        var preferredFormats = descriptor.Characteristics
            .Where(c => c.PixelFormat == PixelFormats.ARGB32
                        || c.PixelFormat == PixelFormats.RGB32
                        || c.PixelFormat == PixelFormats.RGB24)
            .ToList();

        var target = descriptor.Characteristics
            .FirstOrDefault(c => c.Width == 640 && c.Height == 480);

        if (target != null)
        {
            return target;
        }

        return preferredFormats.FirstOrDefault()
               ?? descriptor.Characteristics.FirstOrDefault();
    }

    private Task OnPixelBufferArrivedAsync(PixelBufferScope bufferScope)
    {
        var characteristics = _characteristics;
        if (characteristics == null)
        {
            bufferScope.ReleaseNow();
            return Task.CompletedTask;
        }

        var imageSegment = bufferScope.Buffer.ReferImage();
        var data = new byte[imageSegment.Count];
        if (imageSegment.Count > 0)
        {
            Buffer.BlockCopy(imageSegment.Array!, imageSegment.Offset, data, 0, imageSegment.Count);
        }

        var timestampNs = bufferScope.Buffer.Timestamp.Ticks * 100;
        var rawFrame = new RawFrame(
            characteristics.Width,
            characteristics.Height,
            characteristics.PixelFormat.ToString(),
            data,
            timestampNs);

        bufferScope.ReleaseNow();

        OnFrameArrived(rawFrame);
        return Task.CompletedTask;
    }
}