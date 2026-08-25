using System.Runtime.InteropServices;

namespace Doorstop;

[StructLayout(LayoutKind.Sequential)]
internal struct Color4
{
    public float R;
    public float G;
    public float B;
    public float A;
}

internal struct D3D11Texture2DDescription
{
    public D3D11Texture2DDescription()
    {
    }

    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public int Format;
    public uint SampleCount;
    public uint SampleQuality;
    public uint Usage = 0;
    public uint BindFlags = 0;
    public uint CpuAccessFlags = 0;
    public uint MiscFlags = 0;
}
