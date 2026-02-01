using System;
using System.Runtime.CompilerServices;

namespace FlashCap;

public static class PixelBufferExtension
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte[] ExtractImage(this PixelBuffer pixelBuffer)
	{
		return pixelBuffer.InternalExtractImage((BufferStrategies)1).Array;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static byte[] CopyImage(this PixelBuffer pixelBuffer)
	{
		return pixelBuffer.InternalExtractImage((BufferStrategies)0).Array;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static ArraySegment<byte> ReferImage(this PixelBuffer pixelBuffer)
	{
		return pixelBuffer.InternalExtractImage((BufferStrategies)2);
	}
}
