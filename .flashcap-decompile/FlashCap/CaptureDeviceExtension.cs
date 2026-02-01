using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace FlashCap;

public static class CaptureDeviceExtension
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Task StartAsync(this CaptureDevice captureDevice, CancellationToken ct = default(CancellationToken))
	{
		return captureDevice.InternalStartAsync(ct);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Task StopAsync(this CaptureDevice captureDevice, CancellationToken ct = default(CancellationToken))
	{
		return captureDevice.InternalStopAsync(ct);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Task<bool> ShowPropertyPageAsync(this CaptureDevice captureDevice, nint parentWindow, CancellationToken ct = default(CancellationToken))
	{
		return captureDevice.InternalShowPropertyPageAsync((IntPtr)parentWindow, ct);
	}
}
