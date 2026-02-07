using System.Runtime.InteropServices;
using Gleam.Gestures.Models;
using Mediapipe.Net.Framework.Packets;
using Mediapipe.Net.Framework.Port;
using Mediapipe.Net.Framework.Protobuf;

namespace Gleam.Gestures.Processing;

internal sealed class NormalizedLandmarkListVectorPacket : Packet<List<NormalizedLandmarkList>>
{
    public NormalizedLandmarkListVectorPacket()
        : base(true)
    {
    }

    public override List<NormalizedLandmarkList> Get()
    {
        NativeMethods.AssertOk(NativeMethods.mp_Packet__GetNormalizedLandmarkListVector(MpPtr, out var vector));

        try
        {
            var result = new List<NormalizedLandmarkList>(Math.Max(vector.Size, 0));
            var itemSize = Marshal.SizeOf<SerializedProtoNative>();

            for (var i = 0; i < vector.Size; i++)
            {
                var itemPtr = IntPtr.Add(vector.Data, i * itemSize);
                var item = Marshal.PtrToStructure<SerializedProtoNative>(itemPtr);
                if (item.Length <= 0 || item.StrPtr == IntPtr.Zero)
                {
                    continue;
                }

                var bytes = new byte[item.Length];
                Marshal.Copy(item.StrPtr, bytes, 0, item.Length);
                result.Add(NormalizedLandmarkList.Parser.ParseFrom(bytes));
            }

            return result;
        }
        finally
        {
            NativeMethods.mp_api_SerializedProtoArray__delete(vector.Data, vector.Size);
        }
    }

    public override StatusOr<List<NormalizedLandmarkList>> Consume()
    {
        throw new NotSupportedException();
    }

    public override Status ValidateAsType()
    {
        return ValidateAsProtoMessageLite();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SerializedProtoNative
    {
        public IntPtr StrPtr;
        public int Length;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SerializedProtoVectorNative
    {
        public IntPtr Data;
        public int Size;
    }

    private static class NativeMethods
    {
        [DllImport("mediapipe_c")]
        public static extern int mp_Packet__GetNormalizedLandmarkListVector(
            IntPtr packet,
            out SerializedProtoVectorNative serializedProtoVector);

        [DllImport("mediapipe_c")]
        public static extern void mp_api_SerializedProtoArray__delete(IntPtr data, int size);

        public static void AssertOk(int returnCode)
        {
            if (returnCode != 0)
            {
                throw new InvalidOperationException($"mediapipe_c call failed with code {returnCode}");
            }
        }
    }
}
