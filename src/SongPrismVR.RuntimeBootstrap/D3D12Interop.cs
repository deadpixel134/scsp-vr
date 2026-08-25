using System.Runtime.InteropServices;

namespace Doorstop;

internal struct D3D12ResourceDescription
{
    public D3D12ResourceDescription() { }

    public int Dimension = 0;
    public ulong Alignment = 0;
    public ulong Width = 0;
    public uint Height = 0;
    public ushort DepthOrArraySize = 0;
    public ushort MipLevels = 0;
    public int Format = 0;
    public uint SampleCount = 0;
    public uint SampleQuality = 0;
    public int Layout = 0;
    public int Flags = 0;
}

internal struct DxgiRational
{
    public DxgiRational() { }

    public uint Numerator = 0;
    public uint Denominator = 0;
}

internal struct DxgiSampleDescription
{
    public DxgiSampleDescription() { }

    public uint Count = 0;
    public uint Quality = 0;
}

internal struct DxgiModeDescription
{
    public DxgiModeDescription() { }

    public uint Width = 0;
    public uint Height = 0;
    public DxgiRational RefreshRate = new();
    public int Format = 0;
    public int ScanlineOrdering = 0;
    public int Scaling = 0;
}

internal struct DxgiSwapChainDescription
{
    public DxgiSwapChainDescription() { }

    public DxgiModeDescription BufferDescription = new();
    public DxgiSampleDescription SampleDescription = new();
    public uint BufferUsage = 0;
    public uint BufferCount = 0;
    public IntPtr OutputWindow = IntPtr.Zero;
    public int Windowed = 0;
    public int SwapEffect = 0;
    public int Flags = 0;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct DxgiAdapterDescription
{
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
    public string Description;
    public uint VendorId;
    public uint DeviceId;
    public uint SubSysId;
    public uint Revision;
    public UIntPtr DedicatedVideoMemory;
    public UIntPtr DedicatedSystemMemory;
    public UIntPtr SharedSystemMemory;
    public DxgiLuid AdapterLuid;
}

internal struct DxgiLuid
{
    public uint LowPart = 0;
    public int HighPart = 0;

    public DxgiLuid()
    {
    }
}

internal static class D3D12Interop
{
    private const int ResourceBarrierVtableIndex = 26;
    private const int SwapChainGetBufferVtableIndex = 9;
    private const int SwapChain3GetCurrentBackBufferIndexVtableIndex = 36;
    private const int D3D12ResourceBarrierTypeTransition = 0;
    private const uint D3D12ResourceBarrierAllSubresources = 0xFFFFFFFF;
    private const int D3D12ResourceStateCommon = 0;
    private const int D3D12ResourceStateRenderTarget = 0x4;
    private const int D3D12ResourceStateCopyDest = 0x400;
    private const int D3D12ResourceStateCopySource = 0x800;
    private static readonly Guid Id3D12Resource = new("696442be-a72e-4059-bc79-5b5c98040fad");
    private static readonly Guid Id3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");
    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid Id3D12CommandAllocator = new("6102dee4-af59-4b09-b999-b44d73f09b24");
    private static readonly Guid Id3D12GraphicsCommandList = new("5b160d0f-ac1b-4185-8ba8-b3ae42a5a455");
    private static readonly Guid Id3D12Fence = new("0a753dcf-c4d8-4b91-adf6-be5a60d95a76");
    private static readonly Guid IidCommandQueue = new("0ec870a6-5d7e-4c22-8cfc-5baae07616ed");
    private static readonly Guid IidDxgiSwapChain3 = new("94d99bdb-f1f8-4ab0-b236-7da0170edab1");

    public static IntPtr GetSwapChainBackBuffer(IntPtr swapChain)
    {
        if (swapChain == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        Guid swapChain3InterfaceId = IidDxgiSwapChain3;
        int queryResult = Marshal.QueryInterface(
            swapChain,
            ref swapChain3InterfaceId,
            out IntPtr swapChain3);
        if (queryResult < 0 || swapChain3 == IntPtr.Zero)
        {
            Release(swapChain3);
            throw new InvalidOperationException(
                $"IDXGISwapChain does not expose IDXGISwapChain3: HRESULT=0x{queryResult:x8}.");
        }

        try
        {
            IntPtr vtable = Marshal.ReadIntPtr(swapChain3);
            if (vtable == IntPtr.Zero)
            {
                throw new InvalidOperationException("IDXGISwapChain3 vtable pointer is null.");
            }

            GetCurrentBackBufferIndexDelegate getCurrentBackBufferIndex =
                Marshal.GetDelegateForFunctionPointer<GetCurrentBackBufferIndexDelegate>(
                    Marshal.ReadIntPtr(
                        vtable,
                        SwapChain3GetCurrentBackBufferIndexVtableIndex * IntPtr.Size));
            uint currentBackBufferIndex = getCurrentBackBufferIndex(swapChain3);

            GetBufferDelegate getBuffer = Marshal.GetDelegateForFunctionPointer<GetBufferDelegate>(
                Marshal.ReadIntPtr(vtable, SwapChainGetBufferVtableIndex * IntPtr.Size));
            Guid resourceInterfaceId = Id3D12Resource;
            int result = getBuffer(
                swapChain3,
                currentBackBufferIndex,
                ref resourceInterfaceId,
                out IntPtr resource);
            if (result < 0)
            {
                throw new InvalidOperationException(
                    $"IDXGISwapChain::GetBuffer({currentBackBufferIndex}) failed: HRESULT=0x{result:x8}.");
            }
            if (resource == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"IDXGISwapChain::GetBuffer({currentBackBufferIndex}) returned a null ID3D12Resource.");
            }
            return resource;
        }
        finally
        {
            Release(swapChain3);
        }
    }

    public static DxgiSwapChainDescription GetSwapChainDescription(IntPtr swapChain)
    {
        if (swapChain == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(swapChain));
        }

        IntPtr vtable = Marshal.ReadIntPtr(swapChain);
        if (vtable == IntPtr.Zero)
        {
            throw new InvalidOperationException("IDXGISwapChain vtable pointer is null.");
        }
        IntPtr getDescMethod = Marshal.ReadIntPtr(vtable, 12 * IntPtr.Size);
        if (getDescMethod == IntPtr.Zero)
        {
            throw new InvalidOperationException("IDXGISwapChain::GetDesc vtable slot is null.");
        }
        GetDescDelegate getDesc = Marshal.GetDelegateForFunctionPointer<GetDescDelegate>(
            getDescMethod);
        int result = getDesc(swapChain, out DxgiSwapChainDescription description);
        if (result < 0)
        {
            throw new InvalidOperationException($"IDXGISwapChain::GetDesc failed: HRESULT=0x{result:x8}.");
        }
        return description;
    }

    public static bool TryGetLastPresentCount(IntPtr swapChain, out uint lastPresentCount)
    {
        lastPresentCount = 0;
        if (swapChain == IntPtr.Zero)
        {
            return false;
        }

        IntPtr vtable = Marshal.ReadIntPtr(swapChain);
        if (vtable == IntPtr.Zero)
        {
            return false;
        }

        IntPtr method = Marshal.ReadIntPtr(vtable, 17 * IntPtr.Size);
        if (method == IntPtr.Zero)
        {
            return false;
        }

        GetLastPresentCountDelegate getLastPresentCount =
            Marshal.GetDelegateForFunctionPointer<GetLastPresentCountDelegate>(method);
        int result = getLastPresentCount(swapChain, out lastPresentCount);
        return result >= 0;
    }

    public static D3D12ResourceDescription GetResourceDescription(IntPtr resource)
    {
        if (resource == IntPtr.Zero)
        {
            throw new ArgumentNullException(nameof(resource));
        }

        IntPtr vtable = Marshal.ReadIntPtr(resource);
        GetResourceDescDelegate getDesc = Marshal.GetDelegateForFunctionPointer<GetResourceDescDelegate>(
            Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size));
        getDesc(resource, out D3D12ResourceDescription description);
        return description;
    }

    public static string DescribeResource(IntPtr resource)
    {
        if (resource == IntPtr.Zero)
        {
            return "null";
        }

        D3D12ResourceDescription description = GetResourceDescription(resource);
        return $"dimension={description.Dimension};width={description.Width};height={description.Height};" +
            $"format={description.Format};samples={description.SampleCount};depthOrArray={description.DepthOrArraySize};" +
            $"mips={description.MipLevels};layout={description.Layout};flags={description.Flags}";
    }

    public static void Release(IntPtr unknown)
    {
        if (unknown != IntPtr.Zero)
        {
            _ = Marshal.Release(unknown);
        }
    }

    public static string DescribeTexture(IntPtr resource) => DescribeResource(resource);

    public static D3D11Texture2DDescription GetTextureDescription(IntPtr resource)
    {
        D3D12ResourceDescription description = GetResourceDescription(resource);
        return new D3D11Texture2DDescription
        {
            Width = checked((uint)description.Width),
            Height = description.Height,
            MipLevels = description.MipLevels,
            ArraySize = description.DepthOrArraySize,
            Format = description.Format,
            SampleCount = description.SampleCount,
            SampleQuality = description.SampleQuality
        };
    }

    public static IntPtr CreateRenderTargetView(IntPtr device, IntPtr resource, long format) =>
        resource;

    public static void ClearRenderTargetView(
        IntPtr commandQueue,
        IntPtr renderTargetView,
        Color4 color)
    {
        // D3D12 render targets are cleared by the Unity clone cameras themselves.
    }

    public static void Flush(IntPtr commandQueue)
    {
        // D3D12 command work is submitted by the game and synchronized with fences when needed.
    }

    public static IntPtr CreateEventQuery(IntPtr device) => IntPtr.Zero;

    public static bool HasVisiblePixels(
        IntPtr device,
        IntPtr commandQueue,
        IntPtr resource) =>
        resource != IntPtr.Zero;

    public static void SaveTextureBmp(
        IntPtr device,
        IntPtr commandQueue,
        IntPtr resource,
        string path)
    {
        // Pixel readback diagnostics are not required for the M1 vertical slice.
    }

    public static bool WaitForGpu(
        IntPtr commandQueue,
        IntPtr eventQuery,
        int timeoutMilliseconds)
    {
        if (commandQueue == IntPtr.Zero)
        {
            return false;
        }

        IntPtr device = GetCommandQueueDevice(commandQueue);
        if (device == IntPtr.Zero)
        {
            return false;
        }

        IntPtr deviceVtable = Marshal.ReadIntPtr(device);
        CreateFenceDelegate createFence =
            Marshal.GetDelegateForFunctionPointer<CreateFenceDelegate>(
                Marshal.ReadIntPtr(deviceVtable, 36 * IntPtr.Size));
        Guid fenceIid = Id3D12Fence;
        int fenceResult = createFence(device, 0, 0, ref fenceIid, out IntPtr fence);
        if (fenceResult < 0 || fence == IntPtr.Zero)
        {
            return false;
        }

        IntPtr queueVtable = Marshal.ReadIntPtr(commandQueue);
        SignalQueueDelegate signal =
            Marshal.GetDelegateForFunctionPointer<SignalQueueDelegate>(
                Marshal.ReadIntPtr(queueVtable, 14 * IntPtr.Size));
        IntPtr fenceVtable = Marshal.ReadIntPtr(fence);
        SetEventOnCompletionDelegate setEvent =
            Marshal.GetDelegateForFunctionPointer<SetEventOnCompletionDelegate>(
                Marshal.ReadIntPtr(fenceVtable, 9 * IntPtr.Size));
        IntPtr completionEvent = IntPtr.Zero;
        bool completed = false;
        try
        {
            if (signal(commandQueue, fence, 1) < 0)
            {
                return false;
            }
            completionEvent = CreateEvent(IntPtr.Zero, false, false, IntPtr.Zero);
            if (completionEvent != IntPtr.Zero && setEvent(fence, 1, completionEvent) >= 0)
            {
                completed = WaitForSingleObject(
                    completionEvent,
                    checked((uint)timeoutMilliseconds)) == 0;
            }

            return completed;
        }
        finally
        {
            if (completionEvent != IntPtr.Zero)
            {
                _ = CloseHandle(completionEvent);
            }
            Release(fence);
        }
    }

    public static string GetAdapterLuidText(IntPtr adapter)
    {
        if (adapter == IntPtr.Zero)
        {
            return "null";
        }

        IntPtr vtable = Marshal.ReadIntPtr(adapter);
        GetAdapterDescDelegate getDesc =
            Marshal.GetDelegateForFunctionPointer<GetAdapterDescDelegate>(
                Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size));
        int descriptionSize = Marshal.SizeOf<DxgiAdapterDescription>();
        IntPtr descriptionPointer = Marshal.AllocHGlobal(descriptionSize);
        try
        {
            int result = getDesc(adapter, descriptionPointer);
            if (result < 0)
            {
                throw new InvalidOperationException(
                    $"IDXGIAdapter::GetDesc failed: HRESULT=0x{result:x8}.");
            }
            DxgiAdapterDescription description =
                Marshal.PtrToStructure<DxgiAdapterDescription>(descriptionPointer);
            return $"0x{description.AdapterLuid.HighPart:x8}:{description.AdapterLuid.LowPart:x8}";
        }
        finally
        {
            Marshal.FreeHGlobal(descriptionPointer);
        }
    }

    public static string GetAdapterDescriptionText(IntPtr adapter)
    {
        if (adapter == IntPtr.Zero)
        {
            return "null";
        }

        IntPtr vtable = Marshal.ReadIntPtr(adapter);
        GetAdapterDescDelegate getDesc =
            Marshal.GetDelegateForFunctionPointer<GetAdapterDescDelegate>(
                Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size));
        int descriptionSize = Marshal.SizeOf<DxgiAdapterDescription>();
        IntPtr descriptionPointer = Marshal.AllocHGlobal(descriptionSize);
        try
        {
            int result = getDesc(adapter, descriptionPointer);
            if (result < 0)
            {
                throw new InvalidOperationException(
                    $"IDXGIAdapter::GetDesc failed: HRESULT=0x{result:x8}.");
            }
            DxgiAdapterDescription description =
                Marshal.PtrToStructure<DxgiAdapterDescription>(descriptionPointer);
            return $"name={description.Description};vendor=0x{description.VendorId:x4};device=0x{description.DeviceId:x4};luid=0x{description.AdapterLuid.HighPart:x8}:{description.AdapterLuid.LowPart:x8}";
        }
        finally
        {
            Marshal.FreeHGlobal(descriptionPointer);
        }
    }

    public static int GetMaxSupportedFeatureLevel(IntPtr device)
    {
        if (device == IntPtr.Zero)
        {
            return 0;
        }

        IntPtr vtable = Marshal.ReadIntPtr(device);
        CheckFeatureSupportDelegate checkFeatureSupport =
            Marshal.GetDelegateForFunctionPointer<CheckFeatureSupportDelegate>(
                Marshal.ReadIntPtr(vtable, 13 * IntPtr.Size));
        int[] levels = { 0xC000, 0xB000 };
        IntPtr requestedLevels = Marshal.AllocHGlobal(levels.Length * sizeof(int));
        try
        {
            for (int index = 0; index < levels.Length; index++)
            {
                Marshal.WriteInt32(
                    requestedLevels,
                    index * sizeof(int),
                    levels[index]);
            }
            D3D12FeatureDataFeatureLevels data = new()
            {
                NumFeatureLevels = (uint)levels.Length,
                RequestedLevels = requestedLevels,
                MaxSupportedFeatureLevel = 0
            };
            int result = checkFeatureSupport(
                device,
                2, // D3D12_FEATURE_FEATURE_LEVELS
                ref data,
                (uint)Marshal.SizeOf<D3D12FeatureDataFeatureLevels>());
            return result < 0 ? result : data.MaxSupportedFeatureLevel;
        }
        finally
        {
            Marshal.FreeHGlobal(requestedLevels);
        }
    }

    public static IntPtr GetCommandQueueDevice(IntPtr commandQueue)
    {
        if (commandQueue == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr vtable = Marshal.ReadIntPtr(commandQueue);
        GetDeviceDelegate getDevice =
            Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(
                Marshal.ReadIntPtr(vtable, 7 * IntPtr.Size));
        Guid deviceIid = Id3D12Device;
        int result = getDevice(commandQueue, ref deviceIid, out IntPtr device);
        return result < 0 ? IntPtr.Zero : device;
    }

    public static bool TryGetResourceDeviceIdentity(
        IntPtr resource,
        IntPtr expectedDevice,
        out long resourceDevicePointer,
        out int resourceQueryResult,
        out int getDeviceResult,
        out bool sameIdentity)
    {
        resourceDevicePointer = 0;
        resourceQueryResult = unchecked((int)0x80004003);
        getDeviceResult = unchecked((int)0x80004003);
        sameIdentity = false;
        if (resource == IntPtr.Zero || expectedDevice == IntPtr.Zero)
        {
            return false;
        }

        IntPtr d3d12Resource = IntPtr.Zero;
        IntPtr resourceDevice = IntPtr.Zero;
        try
        {
            Guid resourceIid = Id3D12Resource;
            resourceQueryResult = Marshal.QueryInterface(
                resource,
                ref resourceIid,
                out d3d12Resource);
            if (resourceQueryResult < 0 || d3d12Resource == IntPtr.Zero)
            {
                return false;
            }

            IntPtr vtable = Marshal.ReadIntPtr(d3d12Resource);
            GetDeviceDelegate getDevice =
                Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(
                    Marshal.ReadIntPtr(vtable, 7 * IntPtr.Size));
            Guid deviceIid = Id3D12Device;
            getDeviceResult = getDevice(
                d3d12Resource,
                ref deviceIid,
                out resourceDevice);
            if (getDeviceResult < 0 || resourceDevice == IntPtr.Zero)
            {
                return false;
            }

            resourceDevicePointer = resourceDevice.ToInt64();
            sameIdentity = HaveSameComIdentity(resourceDevice, expectedDevice);
            return true;
        }
        finally
        {
            Release(resourceDevice);
            Release(d3d12Resource);
        }
    }

    internal static bool HaveSameComIdentity(IntPtr left, IntPtr right)
    {
        if (left == right)
        {
            return true;
        }
        if (left == IntPtr.Zero || right == IntPtr.Zero)
        {
            return false;
        }

        IntPtr leftIdentity = IntPtr.Zero;
        IntPtr rightIdentity = IntPtr.Zero;
        try
        {
            Guid interfaceId = IidUnknown;
            int leftResult = Marshal.QueryInterface(
                left,
                ref interfaceId,
                out leftIdentity);
            interfaceId = IidUnknown;
            int rightResult = Marshal.QueryInterface(
                right,
                ref interfaceId,
                out rightIdentity);
            return leftResult >= 0 &&
                rightResult >= 0 &&
                leftIdentity != IntPtr.Zero &&
                leftIdentity == rightIdentity;
        }
        finally
        {
            Release(rightIdentity);
            Release(leftIdentity);
        }
    }

    public static IntPtr CreateDirectCommandQueue(IntPtr device, out int result)
    {
        result = unchecked((int)0x80004005);
        if (device == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        IntPtr vtable = Marshal.ReadIntPtr(device);
        if (vtable == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        CreateCommandQueueDelegate createCommandQueue =
            Marshal.GetDelegateForFunctionPointer<CreateCommandQueueDelegate>(
                Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size));
        D3D12CommandQueueDescription description = new()
        {
            Type = 0,
            Priority = 0,
            Flags = 0,
            NodeMask = 0
        };
        IntPtr descriptionPointer = Marshal.AllocHGlobal(
            Marshal.SizeOf<D3D12CommandQueueDescription>());
        try
        {
            Marshal.StructureToPtr(description, descriptionPointer, fDeleteOld: false);
            Guid interfaceId = IidCommandQueue;
            result = createCommandQueue(device, descriptionPointer, ref interfaceId, out IntPtr queue);
            return result >= 0 ? queue : IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(descriptionPointer);
        }
    }

    public static int GetDeviceRemovedReason(IntPtr device)
    {
        if (device == IntPtr.Zero)
        {
            return 0;
        }

        IntPtr vtable = Marshal.ReadIntPtr(device);
        GetDeviceRemovedReasonDelegate getDeviceRemovedReason =
            Marshal.GetDelegateForFunctionPointer<GetDeviceRemovedReasonDelegate>(
                Marshal.ReadIntPtr(vtable, 37 * IntPtr.Size));
        return getDeviceRemovedReason(device);
    }

    public static void CopyResource(
        IntPtr device,
        IntPtr commandQueue,
        IntPtr source,
        IntPtr destination)
    {
        if (device == IntPtr.Zero || commandQueue == IntPtr.Zero ||
            source == IntPtr.Zero || destination == IntPtr.Zero)
        {
            throw new ArgumentException("D3D12 copy requires a device, queue, source, and destination.");
        }

        IntPtr allocator = IntPtr.Zero;
        IntPtr commandList = IntPtr.Zero;
        IntPtr fence = IntPtr.Zero;
        IntPtr completionEvent = IntPtr.Zero;
        try
        {
            IntPtr deviceVtable = Marshal.ReadIntPtr(device);
            CreateCommandAllocatorDelegate createAllocator =
                Marshal.GetDelegateForFunctionPointer<CreateCommandAllocatorDelegate>(
                    Marshal.ReadIntPtr(deviceVtable, 9 * IntPtr.Size));
            Guid allocatorIid = Id3D12CommandAllocator;
            int allocatorResult = createAllocator(
                device,
                0,
                ref allocatorIid,
                out allocator);
            if (allocatorResult < 0 || allocator == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"ID3D12Device::CreateCommandAllocator failed: HRESULT=0x{allocatorResult:x8}.");
            }

            CreateCommandListDelegate createCommandList =
                Marshal.GetDelegateForFunctionPointer<CreateCommandListDelegate>(
                    Marshal.ReadIntPtr(deviceVtable, 12 * IntPtr.Size));
            Guid commandListIid = Id3D12GraphicsCommandList;
            int commandListResult = createCommandList(
                device,
                0,
                0,
                allocator,
                IntPtr.Zero,
                ref commandListIid,
                out commandList);
            if (commandListResult < 0 || commandList == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"ID3D12Device::CreateCommandList failed: HRESULT=0x{commandListResult:x8}.");
            }

            IntPtr commandListVtable = Marshal.ReadIntPtr(commandList);
            CopyResourceDelegate copyResource =
                Marshal.GetDelegateForFunctionPointer<CopyResourceDelegate>(
                    Marshal.ReadIntPtr(commandListVtable, 17 * IntPtr.Size));
            ResourceBarrierDelegate resourceBarrier =
                Marshal.GetDelegateForFunctionPointer<ResourceBarrierDelegate>(
                    Marshal.ReadIntPtr(commandListVtable, ResourceBarrierVtableIndex * IntPtr.Size));

            IntPtr barriersPointer = Marshal.AllocHGlobal(
                checked(2 * Marshal.SizeOf<D3D12ResourceBarrier>()));
            try
            {
                WriteTransitionBarrier(
                    barriersPointer,
                    0,
                    source,
                    D3D12ResourceStateCommon,
                    D3D12ResourceStateCopySource);
                WriteTransitionBarrier(
                    barriersPointer,
                    1,
                    destination,
                    D3D12ResourceStateRenderTarget,
                    D3D12ResourceStateCopyDest);
                resourceBarrier(commandList, 2, barriersPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(barriersPointer);
            }

            copyResource(commandList, destination, source);

            barriersPointer = Marshal.AllocHGlobal(
                checked(2 * Marshal.SizeOf<D3D12ResourceBarrier>()));
            try
            {
                WriteTransitionBarrier(
                    barriersPointer,
                    0,
                    source,
                    D3D12ResourceStateCopySource,
                    D3D12ResourceStateCommon);
                WriteTransitionBarrier(
                    barriersPointer,
                    1,
                    destination,
                    D3D12ResourceStateCopyDest,
                    D3D12ResourceStateRenderTarget);
                resourceBarrier(commandList, 2, barriersPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(barriersPointer);
            }

            CloseCommandListDelegate close =
                Marshal.GetDelegateForFunctionPointer<CloseCommandListDelegate>(
                    Marshal.ReadIntPtr(commandListVtable, 9 * IntPtr.Size));
            int closeResult = close(commandList);
            if (closeResult < 0)
            {
                throw new InvalidOperationException(
                    $"ID3D12GraphicsCommandList::Close failed: HRESULT=0x{closeResult:x8}.");
            }

            IntPtr queueVtable = Marshal.ReadIntPtr(commandQueue);
            ExecuteCommandListsDelegate execute =
                Marshal.GetDelegateForFunctionPointer<ExecuteCommandListsDelegate>(
                    Marshal.ReadIntPtr(queueVtable, 10 * IntPtr.Size));
            IntPtr commandListsPointer = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                Marshal.WriteIntPtr(commandListsPointer, commandList);
                execute(commandQueue, 1, commandListsPointer);
            }
            finally
            {
                Marshal.FreeHGlobal(commandListsPointer);
            }

            CreateFenceDelegate createFence =
                Marshal.GetDelegateForFunctionPointer<CreateFenceDelegate>(
                    Marshal.ReadIntPtr(deviceVtable, 36 * IntPtr.Size));
            Guid fenceIid = Id3D12Fence;
            int fenceResult = createFence(device, 0, 0, ref fenceIid, out fence);
            if (fenceResult < 0 || fence == IntPtr.Zero)
            {
                throw new InvalidOperationException(
                    $"ID3D12Device::CreateFence failed: HRESULT=0x{fenceResult:x8}.");
            }

            IntPtr fenceVtable = Marshal.ReadIntPtr(fence);
            SignalQueueDelegate signal =
                Marshal.GetDelegateForFunctionPointer<SignalQueueDelegate>(
                    Marshal.ReadIntPtr(queueVtable, 14 * IntPtr.Size));
            signal(commandQueue, fence, 1);

            completionEvent = CreateEvent(IntPtr.Zero, false, false, IntPtr.Zero);
            if (completionEvent == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateEvent failed for D3D12 fence wait.");
            }

            SetEventOnCompletionDelegate setEvent =
                Marshal.GetDelegateForFunctionPointer<SetEventOnCompletionDelegate>(
                    Marshal.ReadIntPtr(fenceVtable, 9 * IntPtr.Size));
            int setEventResult = setEvent(fence, 1, completionEvent);
            if (setEventResult < 0)
            {
                throw new InvalidOperationException(
                    $"ID3D12Fence::SetEventOnCompletion failed: HRESULT=0x{setEventResult:x8}.");
            }

            uint waitResult = WaitForSingleObject(completionEvent, 2_000);
            if (waitResult != 0)
            {
                throw new InvalidOperationException($"D3D12 fence wait failed: 0x{waitResult:x8}.");
            }
        }
        finally
        {
            if (completionEvent != IntPtr.Zero)
            {
                _ = CloseHandle(completionEvent);
            }
            Release(commandList);
            Release(allocator);
            Release(fence);
        }
    }

    private static void WriteTransitionBarrier(
        IntPtr barriersPointer,
        int index,
        IntPtr resource,
        int stateBefore,
        int stateAfter)
    {
        D3D12ResourceBarrier barrier = new()
        {
            Type = D3D12ResourceBarrierTypeTransition,
            Flags = 0,
            Resource = resource,
            Subresource = D3D12ResourceBarrierAllSubresources,
            StateBefore = stateBefore,
            StateAfter = stateAfter
        };
        Marshal.StructureToPtr(
            barrier,
            IntPtr.Add(barriersPointer, index * Marshal.SizeOf<D3D12ResourceBarrier>()),
            fDeleteOld: false);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetBufferDelegate(
        IntPtr swapChain,
        uint buffer,
        ref Guid interfaceId,
        out IntPtr surface);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint GetCurrentBackBufferIndexDelegate(IntPtr swapChain);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDescDelegate(IntPtr swapChain, out DxgiSwapChainDescription description);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetLastPresentCountDelegate(
        IntPtr swapChain,
        out uint lastPresentCount);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetAdapterDescDelegate(IntPtr adapter, IntPtr description);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CheckFeatureSupportDelegate(
        IntPtr device,
        int feature,
        ref D3D12FeatureDataFeatureLevels featureSupportData,
        uint featureSupportDataSize);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDeviceRemovedReasonDelegate(IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDeviceDelegate(
        IntPtr deviceChild,
        ref Guid interfaceId,
        out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateCommandQueueDelegate(
        IntPtr device,
        IntPtr description,
        ref Guid interfaceId,
        out IntPtr commandQueue);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void GetResourceDescDelegate(IntPtr resource, out D3D12ResourceDescription description);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateCommandAllocatorDelegate(
        IntPtr device,
        int type,
        ref Guid interfaceId,
        out IntPtr commandAllocator);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateCommandListDelegate(
        IntPtr device,
        uint nodeMask,
        int type,
        IntPtr commandAllocator,
        IntPtr initialState,
        ref Guid interfaceId,
        out IntPtr commandList);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void CopyResourceDelegate(
        IntPtr commandList,
        IntPtr destination,
        IntPtr source);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ResourceBarrierDelegate(
        IntPtr commandList,
        uint barrierCount,
        IntPtr barriers);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CloseCommandListDelegate(IntPtr commandList);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void ExecuteCommandListsDelegate(
        IntPtr commandQueue,
        uint count,
        IntPtr commandLists);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateFenceDelegate(
        IntPtr device,
        ulong initialValue,
        int flags,
        ref Guid interfaceId,
        out IntPtr fence);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SignalQueueDelegate(IntPtr commandQueue, IntPtr fence, ulong value);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetEventOnCompletionDelegate(
        IntPtr fence,
        ulong value,
        IntPtr completionEvent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(
        IntPtr eventAttributes,
        bool manualReset,
        bool initialState,
        IntPtr name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint timeoutMilliseconds);

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D12ResourceBarrier
    {
        public int Type;
        public int Flags;
        public IntPtr Resource;
        public uint Subresource;
        public int StateBefore;
        public int StateAfter;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D12FeatureDataFeatureLevels
{
    public uint NumFeatureLevels;
    public IntPtr RequestedLevels;
    public int MaxSupportedFeatureLevel;
}
