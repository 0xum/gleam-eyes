using System.Collections.Generic;
using System.Linq;

namespace FlashCap;

public static class CaptureDevicesExtension
{
	public static IEnumerable<CaptureDeviceDescriptor> EnumerateDescriptors(this CaptureDevices captureDevices)
	{
		return captureDevices.InternalEnumerateDescriptors();
	}

	public static CaptureDeviceDescriptor[] GetDescriptors(this CaptureDevices captureDevices)
	{
		return captureDevices.InternalEnumerateDescriptors().ToArray();
	}
}
