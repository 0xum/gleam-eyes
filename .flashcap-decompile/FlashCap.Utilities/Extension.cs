using System;
using System.IO;

namespace FlashCap.Utilities;

public static class Extension
{
	public static Stream AsStream(this ArraySegment<byte> segment)
	{
		if (segment.Array == null)
		{
			return new MemoryStream(ArrayEx.Empty<byte>());
		}
		return new MemoryStream(segment.Array, segment.Offset, segment.Count);
	}

	public static Stream AsStream(this byte[]? data)
	{
		if (data == null)
		{
			return new MemoryStream(ArrayEx.Empty<byte>());
		}
		return new MemoryStream(data);
	}
}
