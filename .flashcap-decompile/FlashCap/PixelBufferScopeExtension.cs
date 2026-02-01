using System.Runtime.CompilerServices;

namespace FlashCap;

public static class PixelBufferScopeExtension
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void ReleaseNow(this PixelBufferScope pixelBufferScope)
	{
		pixelBufferScope.InternalReleaseNow();
	}
}
