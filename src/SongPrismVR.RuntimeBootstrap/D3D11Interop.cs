using System.Diagnostics;
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

[StructLayout(LayoutKind.Explicit, Size = 20)]
internal struct D3D11RenderTargetViewDescription
{
    [FieldOffset(0)]
    public int Format;

    [FieldOffset(4)]
    public int ViewDimension;

    [FieldOffset(8)]
    public uint MipSlice;

    [FieldOffset(12)]
    public uint FirstArraySlice;

    [FieldOffset(16)]
    public uint ArraySize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11Texture2DDescription
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public int Format;
    public uint SampleCount;
    public uint SampleQuality;
    public uint Usage;
    public uint BindFlags;
    public uint CpuAccessFlags;
    public uint MiscFlags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11MappedSubresource
{
    public IntPtr Data;
    public uint RowPitch;
    public uint DepthPitch;
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D11QueryDescription
{
    public int Query;
    public uint MiscFlags;
}

internal static class D3D11Interop
{
    private const int SwapChainGetBufferVtableIndex = 9;
    private const int CreateRenderTargetViewVtableIndex = 9;
    private const int CreateTexture2DVtableIndex = 5;
    private const int CreateQueryVtableIndex = 24;
    private const int Texture2DGetDescriptionVtableIndex = 10;
    private const int MapVtableIndex = 14;
    private const int UnmapVtableIndex = 15;
    private const int CopyResourceVtableIndex = 47;
    private const int UpdateSubresourceVtableIndex = 48;
    private const int EndVtableIndex = 28;
    private const int GetDataVtableIndex = 29;
    private const int ClearRenderTargetViewVtableIndex = 50;
    private const int FlushVtableIndex = 111;
    private const int ReleaseVtableIndex = 2;
    private const int SetMultithreadProtectedVtableIndex = 5;

    private static readonly Guid Id3D11Multithread =
        new("9B7E4E00-342C-4106-A19F-4F2704F689F0");
    private static readonly Guid Id3D11Texture2D =
        new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    public static bool EnableMultithreadProtection(IntPtr immediateContext)
    {
        if (immediateContext == IntPtr.Zero)
        {
            return false;
        }

        Guid interfaceId = Id3D11Multithread;
        int result = Marshal.QueryInterface(immediateContext, ref interfaceId, out IntPtr multithread);
        if (result < 0 || multithread == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            SetMultithreadProtectedDelegate setProtected = GetMethod<SetMultithreadProtectedDelegate>(
                multithread,
                SetMultithreadProtectedVtableIndex);
            _ = setProtected(multithread, 1);
            return true;
        }
        finally
        {
            Release(multithread);
        }
    }

    public static IntPtr CreateRenderTargetView(IntPtr device, IntPtr texture, long swapchainFormat)
    {
        if (device == IntPtr.Zero || texture == IntPtr.Zero)
        {
            throw new ArgumentException("The D3D11 device and texture must both be non-null.");
        }

        CreateRenderTargetViewDelegate create = GetMethod<CreateRenderTargetViewDelegate>(
            device,
            CreateRenderTargetViewVtableIndex);
        D3D11RenderTargetViewDescription description = new()
        {
            Format = checked((int)swapchainFormat),
            ViewDimension = 4,
            MipSlice = 0
        };
        int result = create(device, texture, ref description, out IntPtr renderTargetView);
        if (result < 0 || renderTargetView == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"ID3D11Device::CreateRenderTargetView failed: HRESULT=0x{result:x8}.");
        }

        return renderTargetView;
    }

    public static void ClearRenderTargetView(IntPtr context, IntPtr renderTargetView, Color4 color)
    {
        ClearRenderTargetViewDelegate clear = GetMethod<ClearRenderTargetViewDelegate>(
            context,
            ClearRenderTargetViewVtableIndex);
        clear(context, renderTargetView, ref color);
    }

    public static void Flush(IntPtr context)
    {
        FlushDelegate flush = GetMethod<FlushDelegate>(context, FlushVtableIndex);
        flush(context);
    }

    public static void UpdateTexture(
        IntPtr context,
        IntPtr texture,
        uint subresource,
        byte[] pixels,
        uint width,
        uint height)
    {
        int expectedLength = checked((int)(width * height * 4));
        if (pixels.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Expected {expectedLength} RGBA bytes, got {pixels.Length}.",
                nameof(pixels));
        }

        GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            UpdateSubresourceDelegate update = GetMethod<UpdateSubresourceDelegate>(
                context,
                UpdateSubresourceVtableIndex);
            uint rowPitch = checked(width * 4);
            update(
                context,
                texture,
                subresource,
                IntPtr.Zero,
                pinned.AddrOfPinnedObject(),
                rowPitch,
                checked(rowPitch * height));
        }
        finally
        {
            pinned.Free();
        }
    }

    public static IntPtr CreateEventQuery(IntPtr device)
    {
        CreateQueryDelegate createQuery = GetMethod<CreateQueryDelegate>(device, CreateQueryVtableIndex);
        D3D11QueryDescription description = new()
        {
            Query = 0,
            MiscFlags = 0
        };
        int result = createQuery(device, ref description, out IntPtr query);
        if (result < 0 || query == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"ID3D11Device::CreateQuery(EVENT) failed: HRESULT=0x{result:x8}.");
        }

        return query;
    }

    public static void WaitForGpu(IntPtr context, IntPtr eventQuery, int timeoutMilliseconds)
    {
        WaitForGpuCore(context, eventQuery, timeoutMilliseconds, lowLatencyPolling: false);
    }

    public static void WaitForGpuLowLatency(
        IntPtr context,
        IntPtr eventQuery,
        int timeoutMilliseconds)
    {
        WaitForGpuCore(context, eventQuery, timeoutMilliseconds, lowLatencyPolling: true);
    }

    private static void WaitForGpuCore(
        IntPtr context,
        IntPtr eventQuery,
        int timeoutMilliseconds,
        bool lowLatencyPolling)
    {
        EndDelegate end = GetMethod<EndDelegate>(context, EndVtableIndex);
        GetDataDelegate getData = GetMethod<GetDataDelegate>(context, GetDataVtableIndex);
        end(context, eventQuery);
        Flush(context);

        IntPtr completed = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Stopwatch timeout = Stopwatch.StartNew();
            while (timeout.ElapsedMilliseconds < timeoutMilliseconds)
            {
                Marshal.WriteInt32(completed, 0);
                int result = getData(context, eventQuery, completed, sizeof(int), 0);
                if (result == 0 && Marshal.ReadInt32(completed) != 0)
                {
                    return;
                }

                if (result < 0)
                {
                    throw new InvalidOperationException(
                        $"ID3D11DeviceContext::GetData(EVENT) failed: HRESULT=0x{result:x8}.");
                }

                if (lowLatencyPolling &&
                    timeout.ElapsedTicks < Stopwatch.Frequency / 1_000)
                {
                    Thread.SpinWait(64);
                }
                else
                {
                    Thread.Yield();
                }
            }

            throw new TimeoutException(
                $"Timed out after {timeoutMilliseconds}ms waiting for D3D11 GPU completion.");
        }
        finally
        {
            Marshal.FreeHGlobal(completed);
        }
    }

    public static string DescribeTexture(IntPtr texture)
    {
        D3D11Texture2DDescription description = GetTextureDescription(texture);
        return $"{description.Width}x{description.Height};mips={description.MipLevels};" +
            $"array={description.ArraySize};format={description.Format};samples={description.SampleCount};" +
            $"usage={description.Usage};bind=0x{description.BindFlags:x};" +
            $"cpu=0x{description.CpuAccessFlags:x};misc=0x{description.MiscFlags:x}";
    }

    public static D3D11Texture2DDescription GetTextureDescription(IntPtr texture)
    {
        GetTexture2DDescriptionDelegate getDescription = GetMethod<GetTexture2DDescriptionDelegate>(
            texture,
            Texture2DGetDescriptionVtableIndex);
        getDescription(texture, out D3D11Texture2DDescription description);
        return description;
    }

    public static IntPtr GetSwapChainBackBuffer(IntPtr swapChain)
    {
        if (swapChain == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(swapChain));
        }

        SwapChainGetBufferDelegate getBuffer = GetMethod<SwapChainGetBufferDelegate>(
            swapChain,
            SwapChainGetBufferVtableIndex);
        Guid interfaceId = Id3D11Texture2D;
        int result = getBuffer(swapChain, 0, ref interfaceId, out IntPtr texture);
        if (result < 0 || texture == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"IDXGISwapChain::GetBuffer failed: HRESULT=0x{result:x8}.");
        }

        return texture;
    }

    public static void CopyTexture(IntPtr context, IntPtr destination, IntPtr source)
    {
        D3D11Texture2DDescription destinationDescription = GetTextureDescription(destination);
        D3D11Texture2DDescription sourceDescription = GetTextureDescription(source);
        if (destinationDescription.Width != sourceDescription.Width ||
            destinationDescription.Height != sourceDescription.Height ||
            destinationDescription.MipLevels != sourceDescription.MipLevels ||
            destinationDescription.ArraySize != sourceDescription.ArraySize ||
            destinationDescription.SampleCount != sourceDescription.SampleCount)
        {
            throw new InvalidOperationException(
                "D3D11 texture copy requires matching dimensions, mip levels, array size, and samples: " +
                $"destination={DescribeTexture(destination)};source={DescribeTexture(source)}.");
        }

        CopyResourceDelegate copy = GetMethod<CopyResourceDelegate>(context, CopyResourceVtableIndex);
        copy(context, destination, source);
    }

    public static IntPtr CreateShaderReadableRenderTarget(
        IntPtr device,
        D3D11Texture2DDescription sourceDescription)
    {
        const uint d3D11UsageDefault = 0;
        const uint d3D11BindShaderResource = 0x8;
        const uint d3D11BindRenderTarget = 0x20;
        D3D11Texture2DDescription description = sourceDescription;
        description.MipLevels = 1;
        description.ArraySize = 1;
        description.SampleCount = 1;
        description.SampleQuality = 0;
        description.Usage = d3D11UsageDefault;
        description.BindFlags = d3D11BindShaderResource | d3D11BindRenderTarget;
        description.CpuAccessFlags = 0;
        description.MiscFlags = 0;
        CreateTexture2DDelegate createTexture = GetMethod<CreateTexture2DDelegate>(
            device,
            CreateTexture2DVtableIndex);
        int result = createTexture(
            device,
            ref description,
            IntPtr.Zero,
            out IntPtr texture);
        if (result < 0 || texture == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"ID3D11Device::CreateTexture2D for synchronized UI failed: HRESULT=0x{result:x8}.");
        }

        return texture;
    }

    public static string ReadFirstPixel(IntPtr device, IntPtr context, IntPtr sourceTexture)
    {
        const uint d3D11UsageStaging = 3;
        const uint d3D11CpuAccessRead = 0x20000;
        const int d3D11MapRead = 1;
        D3D11Texture2DDescription stagingDescription = GetTextureDescription(sourceTexture);
        if (stagingDescription.SampleCount != 1)
        {
            throw new NotSupportedException(
                $"Pixel readback requires a single-sample texture, got {stagingDescription.SampleCount}.");
        }

        stagingDescription.Usage = d3D11UsageStaging;
        stagingDescription.BindFlags = 0;
        stagingDescription.CpuAccessFlags = d3D11CpuAccessRead;
        stagingDescription.MiscFlags = 0;
        CreateTexture2DDelegate createTexture = GetMethod<CreateTexture2DDelegate>(
            device,
            CreateTexture2DVtableIndex);
        int createResult = createTexture(
            device,
            ref stagingDescription,
            IntPtr.Zero,
            out IntPtr stagingTexture);
        if (createResult < 0 || stagingTexture == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"ID3D11Device::CreateTexture2D for readback failed: HRESULT=0x{createResult:x8}.");
        }

        try
        {
            CopyResourceDelegate copy = GetMethod<CopyResourceDelegate>(context, CopyResourceVtableIndex);
            copy(context, stagingTexture, sourceTexture);
            Flush(context);

            MapDelegate map = GetMethod<MapDelegate>(context, MapVtableIndex);
            int mapResult = map(
                context,
                stagingTexture,
                0,
                d3D11MapRead,
                0,
                out D3D11MappedSubresource mapped);
            if (mapResult < 0 || mapped.Data == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"ID3D11DeviceContext::Map for readback failed: HRESULT=0x{mapResult:x8}.");
            }

            try
            {
                return $"{Marshal.ReadByte(mapped.Data, 0)}," +
                    $"{Marshal.ReadByte(mapped.Data, 1)}," +
                    $"{Marshal.ReadByte(mapped.Data, 2)}," +
                    $"{Marshal.ReadByte(mapped.Data, 3)}";
            }
            finally
            {
                UnmapDelegate unmap = GetMethod<UnmapDelegate>(context, UnmapVtableIndex);
                unmap(context, stagingTexture, 0);
            }
        }
        finally
        {
            Release(stagingTexture);
        }
    }

    public static bool HasVisiblePixels(
        IntPtr device,
        IntPtr context,
        IntPtr sourceTexture,
        int gridSize = 16,
        int minimumChannelValue = 4)
    {
        const uint d3D11UsageStaging = 3;
        const uint d3D11CpuAccessRead = 0x20000;
        const int d3D11MapRead = 1;
        if (gridSize < 2 || gridSize > 64)
        {
            throw new ArgumentOutOfRangeException(nameof(gridSize));
        }

        D3D11Texture2DDescription stagingDescription = GetTextureDescription(sourceTexture);
        if (stagingDescription.SampleCount != 1 || stagingDescription.ArraySize != 1 ||
            stagingDescription.MipLevels != 1)
        {
            throw new NotSupportedException(
                "Visible-pixel validation requires a single-sample, single-image texture.");
        }

        stagingDescription.Usage = d3D11UsageStaging;
        stagingDescription.BindFlags = 0;
        stagingDescription.CpuAccessFlags = d3D11CpuAccessRead;
        stagingDescription.MiscFlags = 0;
        CreateTexture2DDelegate createTexture = GetMethod<CreateTexture2DDelegate>(
            device,
            CreateTexture2DVtableIndex);
        int createResult = createTexture(
            device,
            ref stagingDescription,
            IntPtr.Zero,
            out IntPtr stagingTexture);
        if (createResult < 0 || stagingTexture == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CreateTexture2D for validation failed: HRESULT=0x{createResult:x8}.");
        }

        try
        {
            CopyResourceDelegate copy = GetMethod<CopyResourceDelegate>(context, CopyResourceVtableIndex);
            copy(context, stagingTexture, sourceTexture);
            Flush(context);

            MapDelegate map = GetMethod<MapDelegate>(context, MapVtableIndex);
            int mapResult = map(
                context,
                stagingTexture,
                0,
                d3D11MapRead,
                0,
                out D3D11MappedSubresource mapped);
            if (mapResult < 0 || mapped.Data == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Map for validation failed: HRESULT=0x{mapResult:x8}.");
            }

            try
            {
                int width = checked((int)stagingDescription.Width);
                int height = checked((int)stagingDescription.Height);
                for (int gridY = 0; gridY < gridSize; gridY++)
                {
                    int y = Math.Min(height - 1, ((2 * gridY + 1) * height) / (2 * gridSize));
                    for (int gridX = 0; gridX < gridSize; gridX++)
                    {
                        int x = Math.Min(width - 1, ((2 * gridX + 1) * width) / (2 * gridSize));
                        int offset = checked((int)(y * mapped.RowPitch) + x * 4);
                        int channel0 = Marshal.ReadByte(mapped.Data, offset);
                        int channel1 = Marshal.ReadByte(mapped.Data, offset + 1);
                        int channel2 = Marshal.ReadByte(mapped.Data, offset + 2);
                        if (channel0 > minimumChannelValue ||
                            channel1 > minimumChannelValue ||
                            channel2 > minimumChannelValue)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            finally
            {
                UnmapDelegate unmap = GetMethod<UnmapDelegate>(context, UnmapVtableIndex);
                unmap(context, stagingTexture, 0);
            }
        }
        finally
        {
            Release(stagingTexture);
        }
    }

    public static void SaveTextureBmp(
        IntPtr device,
        IntPtr context,
        IntPtr sourceTexture,
        string path)
    {
        const uint d3D11UsageStaging = 3;
        const uint d3D11CpuAccessRead = 0x20000;
        const int d3D11MapRead = 1;
        D3D11Texture2DDescription stagingDescription = GetTextureDescription(sourceTexture);
        if (stagingDescription.SampleCount != 1 || stagingDescription.ArraySize != 1 ||
            stagingDescription.MipLevels != 1)
        {
            throw new NotSupportedException(
                "BMP snapshot requires a single-sample, single-image texture.");
        }

        stagingDescription.Usage = d3D11UsageStaging;
        stagingDescription.BindFlags = 0;
        stagingDescription.CpuAccessFlags = d3D11CpuAccessRead;
        stagingDescription.MiscFlags = 0;
        CreateTexture2DDelegate createTexture = GetMethod<CreateTexture2DDelegate>(
            device,
            CreateTexture2DVtableIndex);
        int createResult = createTexture(
            device,
            ref stagingDescription,
            IntPtr.Zero,
            out IntPtr stagingTexture);
        if (createResult < 0 || stagingTexture == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"CreateTexture2D for BMP snapshot failed: HRESULT=0x{createResult:x8}.");
        }

        try
        {
            CopyResourceDelegate copy = GetMethod<CopyResourceDelegate>(context, CopyResourceVtableIndex);
            copy(context, stagingTexture, sourceTexture);
            MapDelegate map = GetMethod<MapDelegate>(context, MapVtableIndex);
            int mapResult = map(
                context,
                stagingTexture,
                0,
                d3D11MapRead,
                0,
                out D3D11MappedSubresource mapped);
            if (mapResult < 0 || mapped.Data == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"Map for BMP snapshot failed: HRESULT=0x{mapResult:x8}.");
            }

            try
            {
                int width = checked((int)stagingDescription.Width);
                int height = checked((int)stagingDescription.Height);
                int pixelBytes = checked(width * height * 4);
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory());
                using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                using BinaryWriter writer = new(stream);
                writer.Write((byte)'B');
                writer.Write((byte)'M');
                writer.Write(checked(54 + pixelBytes));
                writer.Write(0);
                writer.Write(54);
                writer.Write(40);
                writer.Write(width);
                writer.Write(-height);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write(0);
                writer.Write(pixelBytes);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);

                byte[] sourceRow = new byte[checked(width * 4)];
                byte[] bitmapRow = new byte[sourceRow.Length];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(
                        IntPtr.Add(mapped.Data, checked((int)(y * mapped.RowPitch))),
                        sourceRow,
                        0,
                        sourceRow.Length);
                    for (int x = 0; x < width; x++)
                    {
                        int offset = x * 4;
                        bitmapRow[offset] = sourceRow[offset + 2];
                        bitmapRow[offset + 1] = sourceRow[offset + 1];
                        bitmapRow[offset + 2] = sourceRow[offset];
                        bitmapRow[offset + 3] = sourceRow[offset + 3];
                    }

                    writer.Write(bitmapRow);
                }
            }
            finally
            {
                UnmapDelegate unmap = GetMethod<UnmapDelegate>(context, UnmapVtableIndex);
                unmap(context, stagingTexture, 0);
            }
        }
        finally
        {
            Release(stagingTexture);
        }
    }

    public static void Release(IntPtr unknown)
    {
        if (unknown == IntPtr.Zero)
        {
            return;
        }

        ReleaseDelegate release = GetMethod<ReleaseDelegate>(unknown, ReleaseVtableIndex);
        _ = release(unknown);
    }

    private static TDelegate GetMethod<TDelegate>(IntPtr instance, int vtableIndex)
        where TDelegate : Delegate
    {
        if (instance == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(instance));
        }

        IntPtr vtable = Marshal.ReadIntPtr(instance);
        IntPtr method = Marshal.ReadIntPtr(vtable, checked(vtableIndex * IntPtr.Size));
        if (method == IntPtr.Zero)
        {
            throw new MissingMethodException($"COM vtable entry {vtableIndex} is null.");
        }

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(method);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetMultithreadProtectedDelegate(IntPtr instance, int multithreadProtected);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SwapChainGetBufferDelegate(
        IntPtr instance,
        uint bufferIndex,
        ref Guid interfaceId,
        out IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateRenderTargetViewDelegate(
        IntPtr instance,
        IntPtr resource,
        ref D3D11RenderTargetViewDescription description,
        out IntPtr renderTargetView);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateTexture2DDelegate(
        IntPtr instance,
        ref D3D11Texture2DDescription description,
        IntPtr initialData,
        out IntPtr texture);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateQueryDelegate(
        IntPtr instance,
        ref D3D11QueryDescription description,
        out IntPtr query);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GetTexture2DDescriptionDelegate(
        IntPtr instance,
        out D3D11Texture2DDescription description);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int MapDelegate(
        IntPtr instance,
        IntPtr resource,
        uint subresource,
        int mapType,
        uint mapFlags,
        out D3D11MappedSubresource mappedResource);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void UnmapDelegate(IntPtr instance, IntPtr resource, uint subresource);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void CopyResourceDelegate(IntPtr instance, IntPtr destination, IntPtr source);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void UpdateSubresourceDelegate(
        IntPtr instance,
        IntPtr destinationResource,
        uint destinationSubresource,
        IntPtr destinationBox,
        IntPtr sourceData,
        uint sourceRowPitch,
        uint sourceDepthPitch);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void EndDelegate(IntPtr instance, IntPtr asynchronous);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDataDelegate(
        IntPtr instance,
        IntPtr asynchronous,
        IntPtr data,
        uint dataSize,
        uint getDataFlags);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ClearRenderTargetViewDelegate(
        IntPtr instance,
        IntPtr renderTargetView,
        ref Color4 color);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void FlushDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint ReleaseDelegate(IntPtr instance);
}
