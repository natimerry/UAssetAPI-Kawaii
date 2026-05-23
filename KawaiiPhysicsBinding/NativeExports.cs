using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using UAssetAPI;

namespace KawaiiPhysicsBinding;

[StructLayout(LayoutKind.Sequential)]
public struct KawaiiPhysicsPortNativeResult
{
    public int VisitedAnimNodes;
    public int PortedAnimNodes;
    public int SkippedExistingChains;
}

public static unsafe class NativeExports
{
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    public static int PortAsset(
        byte* usmapPathUtf8,
        byte* uassetPathUtf8,
        int forceRebuild,
        KawaiiPhysicsPortNativeResult* result,
        byte* errorBuffer,
        int errorBufferLength)
    {
        try
        {
            var uassetPath = PtrToString(uassetPathUtf8);
            if (string.IsNullOrWhiteSpace(uassetPath))
            {
                WriteError(errorBuffer, errorBufferLength, "UAsset path was null or empty");
                return 2;
            }

            var options = new KawaiiPhysicsPortOptions
            {
                ForceRebuildChain0 = forceRebuild != 0
            };

            var portResult = KawaiiPhysicsBridge.PortAsset(
                PtrToString(usmapPathUtf8),
                uassetPath,
                options);

            if (result != null)
            {
                result->VisitedAnimNodes = portResult.VisitedAnimNodes;
                result->PortedAnimNodes = portResult.PortedAnimNodes;
                result->SkippedExistingChains = portResult.SkippedExistingChains;
            }

            return 0;
        }
        catch (Exception ex)
        {
            WriteError(errorBuffer, errorBufferLength, ex.ToString());
            return 1;
        }
    }

    private static string? PtrToString(byte* ptr)
    {
        return ptr == null ? null : Marshal.PtrToStringUTF8((IntPtr)ptr);
    }

    private static void WriteError(byte* errorBuffer, int errorBufferLength, string message)
    {
        if (errorBuffer == null || errorBufferLength <= 0)
        {
            return;
        }

        var output = new Span<byte>(errorBuffer, errorBufferLength);
        output.Clear();

        if (errorBufferLength == 1)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(message);
        var copied = Math.Min(bytes.Length, errorBufferLength - 1);
        bytes.AsSpan(0, copied).CopyTo(output);
        output[copied] = 0;
    }
}
