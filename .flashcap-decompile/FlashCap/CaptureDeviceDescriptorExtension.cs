using System.Threading;
using System.Threading.Tasks;
using FlashCap.FrameProcessors;

namespace FlashCap;

public static class CaptureDeviceDescriptorExtension
{
	public static Task<CaptureDevice> OpenWithFrameProcessorAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, TranscodeFormats transcodeFormat, FrameProcessor frameProcessor, CancellationToken ct = default(CancellationToken))
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		return descriptor.InternalOpenWithFrameProcessorAsync(characteristics, transcodeFormat, frameProcessor, ct);
	}

	public static Task<CaptureDevice> OpenAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, PixelBufferArrivedDelegate pixelBufferArrived, CancellationToken ct = default(CancellationToken))
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		return descriptor.OpenWithFrameProcessorAsync(characteristics, (TranscodeFormats)0, (FrameProcessor)new DelegatedQueuingProcessor(pixelBufferArrived, 1, descriptor.defaultBufferPool), ct);
	}

	public static Task<CaptureDevice> OpenAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, TranscodeFormats transcodeFormat, PixelBufferArrivedDelegate pixelBufferArrived, CancellationToken ct = default(CancellationToken))
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		return descriptor.OpenWithFrameProcessorAsync(characteristics, transcodeFormat, (FrameProcessor)new DelegatedQueuingProcessor(pixelBufferArrived, 1, descriptor.defaultBufferPool), ct);
	}

	public static Task<CaptureDevice> OpenAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, TranscodeFormats transcodeFormat, bool isScattering, int maxQueuingFrames, PixelBufferArrivedDelegate pixelBufferArrived, CancellationToken ct = default(CancellationToken))
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		return descriptor.OpenWithFrameProcessorAsync(characteristics, transcodeFormat, (FrameProcessor)(isScattering ? new DelegatedScatteringProcessor(pixelBufferArrived, maxQueuingFrames, descriptor.defaultBufferPool) : new DelegatedQueuingProcessor(pixelBufferArrived, maxQueuingFrames, descriptor.defaultBufferPool)), ct);
	}

	public static Task<CaptureDevice> OpenAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, PixelBufferArrivedTaskDelegate pixelBufferArrived, CancellationToken ct = default(CancellationToken))
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0016: Expected O, but got Unknown
		return descriptor.OpenWithFrameProcessorAsync(characteristics, (TranscodeFormats)0, (FrameProcessor)new DelegatedQueuingTaskProcessor(pixelBufferArrived, 1, descriptor.defaultBufferPool), ct);
	}

	public static Task<CaptureDevice> OpenAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, TranscodeFormats transcodeFormat, PixelBufferArrivedTaskDelegate pixelBufferArrived, CancellationToken ct = default(CancellationToken))
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Expected O, but got Unknown
		return descriptor.OpenWithFrameProcessorAsync(characteristics, transcodeFormat, (FrameProcessor)new DelegatedQueuingTaskProcessor(pixelBufferArrived, 1, descriptor.defaultBufferPool), ct);
	}

	public static Task<CaptureDevice> OpenAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, TranscodeFormats transcodeFormat, bool isScattering, int maxQueuingFrames, PixelBufferArrivedTaskDelegate pixelBufferArrived, CancellationToken ct = default(CancellationToken))
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_0010: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Expected O, but got Unknown
		return descriptor.OpenWithFrameProcessorAsync(characteristics, transcodeFormat, (FrameProcessor)(isScattering ? new DelegatedScatteringTaskProcessor(pixelBufferArrived, maxQueuingFrames, descriptor.defaultBufferPool) : new DelegatedQueuingTaskProcessor(pixelBufferArrived, maxQueuingFrames, descriptor.defaultBufferPool)), ct);
	}

	public static async Task<ObservableCaptureDevice> AsObservableAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, CancellationToken ct = default(CancellationToken))
	{
		ObserverProxy observerProxy = new ObserverProxy();
		return new ObservableCaptureDevice(await descriptor.OpenWithFrameProcessorAsync(characteristics, (TranscodeFormats)0, (FrameProcessor)new DelegatedQueuingProcessor(new PixelBufferArrivedDelegate(observerProxy.OnPixelBufferArrived), 1, descriptor.defaultBufferPool), ct).ConfigureAwait(continueOnCapturedContext: false), observerProxy);
	}

	public static async Task<ObservableCaptureDevice> AsObservableAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, TranscodeFormats transcodeFormat, CancellationToken ct = default(CancellationToken))
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		ObserverProxy observerProxy = new ObserverProxy();
		return new ObservableCaptureDevice(await descriptor.OpenWithFrameProcessorAsync(characteristics, transcodeFormat, (FrameProcessor)new DelegatedQueuingProcessor(new PixelBufferArrivedDelegate(observerProxy.OnPixelBufferArrived), 1, descriptor.defaultBufferPool), ct).ConfigureAwait(continueOnCapturedContext: false), observerProxy);
	}

	public static async Task<ObservableCaptureDevice> AsObservableAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, TranscodeFormats transcodeFormat, bool isScattering, int maxQueuingFrames, CancellationToken ct = default(CancellationToken))
	{
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		ObserverProxy observerProxy = new ObserverProxy();
		return new ObservableCaptureDevice(await descriptor.OpenWithFrameProcessorAsync(characteristics, transcodeFormat, (FrameProcessor)(isScattering ? new DelegatedScatteringProcessor(new PixelBufferArrivedDelegate(observerProxy.OnPixelBufferArrived), maxQueuingFrames, descriptor.defaultBufferPool) : new DelegatedQueuingProcessor(new PixelBufferArrivedDelegate(observerProxy.OnPixelBufferArrived), maxQueuingFrames, descriptor.defaultBufferPool)), ct).ConfigureAwait(continueOnCapturedContext: false), observerProxy);
	}

	public static Task<byte[]> TakeOneShotAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, CancellationToken ct = default(CancellationToken))
	{
		return descriptor.InternalTakeOneShotAsync(characteristics, (TranscodeFormats)0, ct);
	}

	public static Task<byte[]> TakeOneShotAsync(this CaptureDeviceDescriptor descriptor, VideoCharacteristics characteristics, TranscodeFormats transcodeFormat, CancellationToken ct = default(CancellationToken))
	{
		//IL_0002: Unknown result type (might be due to invalid IL or missing references)
		return descriptor.InternalTakeOneShotAsync(characteristics, transcodeFormat, ct);
	}
}
