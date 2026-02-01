using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace FlashCap;

public static class ObservableCaptureDeviceExtension
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Task StartAsync(this ObservableCaptureDevice observableCaptureDevice, CancellationToken ct = default(CancellationToken))
	{
		return observableCaptureDevice.InternalStartAsync(ct);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Task StopAsync(this ObservableCaptureDevice observableCaptureDevice, CancellationToken ct = default(CancellationToken))
	{
		return observableCaptureDevice.InternalStopAsync(ct);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static IDisposable Subscribe(this ObservableCaptureDevice observableCaptureDevice, IObserver<PixelBufferScope> observer)
	{
		return observableCaptureDevice.InternalSubscribe(observer);
	}
}
