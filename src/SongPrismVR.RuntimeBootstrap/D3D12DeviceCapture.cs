using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Doorstop;

internal sealed class D3D12CaptureSnapshot
{
    public bool HookInstalled { get; init; }
    public bool DeviceCaptured { get; init; }
    public bool CommandQueueCaptured { get; init; }
    public bool SwapChainCaptured { get; init; }
    public int CreateFactoryHookCallCount { get; init; }
    public bool SwapChainVtableHooksInstalled { get; init; }
    public int FactorySwapChainCallCount { get; init; }
    public int CapturePresentDeviceQueryFailureCount { get; init; }
    public int SwapChainOutputCount { get; init; }
    public int? CommandQueueType { get; init; }
    public long PresentationGeneration { get; init; }
    public bool PresentHookInstalled { get; init; }
    public int PresentHookCount { get; init; }
    public int Present1HookCount { get; init; }
    public string? LastFactoryInterfaceId { get; init; }
    public string? Error { get; init; }
}

internal sealed class D3D12PresentationBindingLease : IDisposable
{
    private IntPtr _device;
    private IntPtr _commandQueue;
    private IntPtr _swapChain;

    internal D3D12PresentationBindingLease(
        IntPtr device,
        IntPtr commandQueue,
        IntPtr swapChain,
        long generation)
    {
        _device = device;
        _commandQueue = commandQueue;
        _swapChain = swapChain;
        Generation = generation;
    }

    internal IntPtr Device => _device;
    internal IntPtr CommandQueue => _commandQueue;
    internal IntPtr SwapChain => _swapChain;
    internal long Generation { get; }

    public void Dispose()
    {
        Release(ref _swapChain);
        Release(ref _commandQueue);
        Release(ref _device);
    }

    private static void Release(ref IntPtr value)
    {
        IntPtr owned = Interlocked.Exchange(ref value, IntPtr.Zero);
        if (owned != IntPtr.Zero)
        {
            _ = Marshal.Release(owned);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct D3D12CommandQueueDescription
{
    public int Type;
    public int Priority;
    public int Flags;
    public uint NodeMask;
}

internal static class D3D12DeviceCapture
{
    private static readonly object InstallLock = new();
    private static readonly object PresentationLock = new();
    private static readonly ManualResetEventSlim DeviceReady = new(false);
    private static readonly ManualResetEventSlim PresentReady = new(false);
    private static readonly CreateDeviceDelegate CreateDeviceReplacement = OnCreateDevice;
    private static readonly CreateDxgiFactory2Delegate CreateDxgiFactory2Replacement = OnCreateDxgiFactory2;
    private static readonly CreateDxgiFactory1Delegate CreateDxgiFactory1Replacement = OnCreateDxgiFactory1;
    private static readonly CreateDxgiFactoryDelegate CreateDxgiFactoryReplacement = OnCreateDxgiFactory;
    private static readonly CreateCommandQueueDelegate CreateCommandQueueReplacement = OnCreateCommandQueue;
    private static readonly FactoryCreateSwapChainDelegate FactoryCreateSwapChainReplacement =
        OnFactoryCreateSwapChain;
    private static readonly FactoryCreateSwapChainForHwndDelegate FactoryCreateSwapChainForHwndReplacement =
        OnFactoryCreateSwapChainForHwnd;
    private static readonly FactoryCreateSwapChainForCoreWindowDelegate
        FactoryCreateSwapChainForCoreWindowReplacement = OnFactoryCreateSwapChainForCoreWindow;
    private static readonly FactoryCreateSwapChainForCompositionDelegate
        FactoryCreateSwapChainForCompositionReplacement = OnFactoryCreateSwapChainForComposition;
    private static readonly PresentDelegate PresentReplacement = OnPresent;
    private static readonly Present1Delegate Present1Replacement = OnPresent1;

    private static CreateDeviceDelegate? _createDeviceOriginal;
    private static CreateDxgiFactory2Delegate? _createDxgiFactory2Original;
    private static CreateDxgiFactory1Delegate? _createDxgiFactory1Original;
    private static CreateDxgiFactoryDelegate? _createDxgiFactoryOriginal;
    private static FactoryCreateSwapChainDelegate? _factoryCreateSwapChainOriginal;
    private static FactoryCreateSwapChainForHwndDelegate? _factoryCreateSwapChainForHwndOriginal;
    private static FactoryCreateSwapChainForCoreWindowDelegate?
        _factoryCreateSwapChainForCoreWindowOriginal;
    private static FactoryCreateSwapChainForCompositionDelegate?
        _factoryCreateSwapChainForCompositionOriginal;
    private static readonly Dictionary<IntPtr, PresentDelegate> PresentOriginals = new();
    private static readonly Dictionary<IntPtr, Present1Delegate> Present1Originals = new();
    private static PresentDelegate? _presentOriginalFallback;
    private static Present1Delegate? _present1OriginalFallback;
    private static CreateCommandQueueDelegate? _createCommandQueueOriginal;

    private static IntPtr _d3d12Library;
    private static IntPtr _dxgiLibrary;
    private static IntPtr _dobbyLibrary;
    private static IntPtr _adapter;
    private static IntPtr _device;
    private static IntPtr _observedCommandQueue;
    private static IntPtr _commandQueue;
    private static IntPtr _swapChain;
    private static long _presentationGeneration;
    private static bool _installAttempted;
    private static bool _createDeviceHooked;
    private static bool _createCommandQueueHooked;
    private static int _createFactoryHookCallCount;
    private static bool _swapChainVtableHooksInstalled;
    private static int _factorySwapChainCallCount;
    private static int _capturePresentDeviceQueryFailureCount;
    private static int _swapChainOutputCount;
    private static int? _commandQueueType;
    private static Guid _capturedQueueInterfaceId = Guid.Empty;
    private static int? _createDeviceRequestedFeatureLevel;
    private static int? _createDeviceResult;
    private static bool _createDeviceFellBack;
    private static string? _lastFactoryInterfaceId;
    private static long _presentSerial;
    private static string? _error;

    internal static long PresentationGeneration =>
        Volatile.Read(ref _presentationGeneration);

    private static readonly Guid Id3D12Device = new("189819f1-1db6-4b57-be54-1821339b85f7");
    private static readonly Guid IidCommandQueue = new("0ec870a6-5d7e-4c22-8cfc-5baae07616ed");
    private static readonly Guid IidUnknown = new("00000000-0000-0000-C000-000000000046");
    private static readonly Guid IdxgiFactory2 = new("50C83A1C-E072-4C48-87B0-3630FA36A6D0");
    private static readonly Guid IdxgiFactory = new("7b7166ec-21c7-44ae-b21a-c9ae321ae369");
    private static readonly Guid IdxgiFactory1 = new("770aae78-f26f-4dba-a829-253c83d1b387");

    public static void Install()
    {
        lock (InstallLock)
        {
            if (_installAttempted)
            {
                return;
            }

            _installAttempted = true;
            try
            {
                _d3d12Library = NativeLibrary.Load("d3d12.dll");
                string gameRoot = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)
                    ?? Directory.GetCurrentDirectory();
                _dobbyLibrary = NativeLibrary.Load(Path.Combine(gameRoot, "BepInEx", "core", "dobby.dll"));
                DobbyHookDelegate hook = Marshal.GetDelegateForFunctionPointer<DobbyHookDelegate>(
                    NativeLibrary.GetExport(_dobbyLibrary, "DobbyHook"));

                _createDeviceHooked = TryHook(
                    hook,
                    NativeLibrary.GetExport(_d3d12Library, "D3D12CreateDevice"),
                    Marshal.GetFunctionPointerForDelegate(CreateDeviceReplacement),
                    out IntPtr createDeviceOriginal);
                if (_createDeviceHooked)
                {
                    _createDeviceOriginal = Marshal.GetDelegateForFunctionPointer<CreateDeviceDelegate>(
                        createDeviceOriginal);
                }

                bool factoryHooked = InstallFactoryCreationHooks(hook);
                if (!_createDeviceHooked && !factoryHooked)
                {
                    _error = "Dobby could not hook D3D12 device creation or DXGI swapchain creation.";
                }
            }
            catch (Exception exception)
            {
                _error = exception.ToString();
            }
        }
    }

    public static D3D12CaptureSnapshot Snapshot() => new()
    {
        HookInstalled = _createDeviceHooked || PresentReady.IsSet,
        DeviceCaptured = Volatile.Read(ref _device) != IntPtr.Zero,
        CommandQueueCaptured = Volatile.Read(ref _commandQueue) != IntPtr.Zero,
        SwapChainCaptured = Volatile.Read(ref _swapChain) != IntPtr.Zero,
        CreateFactoryHookCallCount = Volatile.Read(ref _createFactoryHookCallCount),
        SwapChainVtableHooksInstalled = _swapChainVtableHooksInstalled,
        FactorySwapChainCallCount = Volatile.Read(ref _factorySwapChainCallCount),
        CapturePresentDeviceQueryFailureCount = Volatile.Read(ref _capturePresentDeviceQueryFailureCount),
        SwapChainOutputCount = Volatile.Read(ref _swapChainOutputCount),
        CommandQueueType = _commandQueueType,
        PresentationGeneration = Interlocked.Read(ref _presentationGeneration),
        PresentHookInstalled = GetPresentOriginalCount() > 0 || GetPresent1OriginalCount() > 0,
        PresentHookCount = GetPresentOriginalCount(),
        Present1HookCount = GetPresent1OriginalCount(),
        LastFactoryInterfaceId = _lastFactoryInterfaceId,
        Error = _error
    };

    public static bool WaitForPresent(int timeoutMilliseconds) => PresentReady.Wait(timeoutMilliseconds);

    internal static IntPtr Device
    {
        get
        {
            lock (PresentationLock)
            {
                return _device;
            }
        }
    }

    internal static IntPtr Adapter => Volatile.Read(ref _adapter);

    internal static IntPtr CommandQueue
    {
        get
        {
            lock (PresentationLock)
            {
                return _commandQueue;
            }
        }
    }

    internal static int? CommandQueueType => _commandQueueType;

    internal static string? CapturedQueueInterfaceId =>
        _capturedQueueInterfaceId == Guid.Empty ? null : _capturedQueueInterfaceId.ToString();

    internal static IntPtr CreateDirectCommandQueue(IntPtr device, out int result)
    {
        result = unchecked((int)0x80004005);
        CreateCommandQueueDelegate? original = _createCommandQueueOriginal;
        if (device == IntPtr.Zero || original is null)
        {
            return IntPtr.Zero;
        }

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
            Guid interfaceId = _capturedQueueInterfaceId == Guid.Empty
                ? IidCommandQueue
                : _capturedQueueInterfaceId;
            result = original(device, descriptionPointer, ref interfaceId, out IntPtr queue);
            return result >= 0 ? queue : IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(descriptionPointer);
        }
    }

    internal static IntPtr CreateFreshDeviceForOpenXr(
        IntPtr adapter,
        int minimumFeatureLevel,
        out int result)
    {
        result = unchecked((int)0x80004005);
        CreateDeviceDelegate? original = _createDeviceOriginal;
        if (adapter == IntPtr.Zero || original is null)
        {
            return IntPtr.Zero;
        }

        Guid interfaceId = Id3D12Device;
        result = original(adapter, minimumFeatureLevel, ref interfaceId, out IntPtr device);
        return result >= 0 ? device : IntPtr.Zero;
    }

    internal static int? CreateDeviceRequestedFeatureLevel => _createDeviceRequestedFeatureLevel;

    internal static int? CreateDeviceResult => _createDeviceResult;

    internal static bool CreateDeviceFellBack => _createDeviceFellBack;

    internal static IntPtr SwapChain
    {
        get
        {
            lock (PresentationLock)
            {
                return _swapChain;
            }
        }
    }

    internal static bool TryAcquirePresentationBinding(
        out D3D12PresentationBindingLease binding)
    {
        lock (PresentationLock)
        {
            if (_presentationGeneration <= 0 ||
                _device == IntPtr.Zero ||
                _commandQueue == IntPtr.Zero ||
                _swapChain == IntPtr.Zero)
            {
                binding = null!;
                return false;
            }

            _ = Marshal.AddRef(_device);
            try
            {
                _ = Marshal.AddRef(_commandQueue);
                try
                {
                    _ = Marshal.AddRef(_swapChain);
                    binding = new D3D12PresentationBindingLease(
                        _device,
                        _commandQueue,
                        _swapChain,
                        _presentationGeneration);
                    return true;
                }
                catch
                {
                    _ = Marshal.Release(_commandQueue);
                    throw;
                }
            }
            catch
            {
                _ = Marshal.Release(_device);
                throw;
            }
        }
    }

    internal static bool IsPresentationGenerationCurrent(long generation)
    {
        lock (PresentationLock)
        {
            return generation > 0 && generation == _presentationGeneration;
        }
    }

    internal static long PresentSerial => Interlocked.Read(ref _presentSerial);

    internal static void RefreshPresentSerial()
    {
        if (!TryAcquirePresentationBinding(out D3D12PresentationBindingLease binding))
        {
            return;
        }

        using (binding)
        {
            if (D3D12Interop.TryGetLastPresentCount(binding.SwapChain, out uint lastPresentCount) &&
                lastPresentCount > 0)
            {
                Interlocked.Exchange(ref _presentSerial, lastPresentCount);
                PresentReady.Set();
            }
        }
    }

    private static bool InstallFactorySwapChainHooks(DobbyHookDelegate hook)
    {
        _dxgiLibrary = NativeLibrary.Load("dxgi.dll");
        CreateDxgiFactory2Delegate createFactory = Marshal.GetDelegateForFunctionPointer<CreateDxgiFactory2Delegate>(
            NativeLibrary.GetExport(_dxgiLibrary, "CreateDXGIFactory2"));
        Guid interfaceId = IdxgiFactory2;
        int result = createFactory(0, ref interfaceId, out IntPtr factory);
        if (result < 0 || factory == IntPtr.Zero)
        {
            _error = $"CreateDXGIFactory2 failed while installing D3D12 swapchain hooks: HRESULT=0x{result:x8}.";
            return false;
        }

        bool installed = InstallSwapChainVtableHooks(hook, factory, IdxgiFactory2);
        _ = Marshal.Release(factory);
        return installed;
    }

    private static bool InstallFactoryCreationHooks(DobbyHookDelegate hook)
    {
        _dxgiLibrary = NativeLibrary.Load("dxgi.dll");
        bool hookedAny = false;
        if (TryHook(
                hook,
                NativeLibrary.GetExport(_dxgiLibrary, "CreateDXGIFactory2"),
                Marshal.GetFunctionPointerForDelegate(CreateDxgiFactory2Replacement),
                out IntPtr createFactory2Original))
        {
            _createDxgiFactory2Original =
                Marshal.GetDelegateForFunctionPointer<CreateDxgiFactory2Delegate>(createFactory2Original);
            hookedAny = true;
        }
        if (TryHook(
                hook,
                NativeLibrary.GetExport(_dxgiLibrary, "CreateDXGIFactory1"),
                Marshal.GetFunctionPointerForDelegate(CreateDxgiFactory1Replacement),
                out IntPtr createFactory1Original))
        {
            _createDxgiFactory1Original =
                Marshal.GetDelegateForFunctionPointer<CreateDxgiFactory1Delegate>(createFactory1Original);
            hookedAny = true;
        }
        if (TryHook(
                hook,
                NativeLibrary.GetExport(_dxgiLibrary, "CreateDXGIFactory"),
                Marshal.GetFunctionPointerForDelegate(CreateDxgiFactoryReplacement),
                out IntPtr createFactoryOriginal))
        {
            _createDxgiFactoryOriginal =
                Marshal.GetDelegateForFunctionPointer<CreateDxgiFactoryDelegate>(createFactoryOriginal);
            hookedAny = true;
        }
        return hookedAny;
    }

    private static bool InstallSwapChainVtableHooks(
        DobbyHookDelegate hook,
        IntPtr factory,
        Guid interfaceId)
    {
        IntPtr vtable = Marshal.ReadIntPtr(factory);
        if (interfaceId == IdxgiFactory || interfaceId == IdxgiFactory1)
        {
            bool baseLegacyHooked = TryHook(
                hook,
                Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size),
                Marshal.GetFunctionPointerForDelegate(FactoryCreateSwapChainReplacement),
                out IntPtr baseLegacyOriginal);
            if (baseLegacyHooked)
            {
                _factoryCreateSwapChainOriginal =
                    Marshal.GetDelegateForFunctionPointer<FactoryCreateSwapChainDelegate>(baseLegacyOriginal);
                _swapChainVtableHooksInstalled = true;
            }
            return baseLegacyHooked;
        }
        bool legacyHooked = TryHook(
            hook,
            Marshal.ReadIntPtr(vtable, 10 * IntPtr.Size),
            Marshal.GetFunctionPointerForDelegate(FactoryCreateSwapChainReplacement),
            out IntPtr legacyOriginal);
        if (legacyHooked)
        {
            _factoryCreateSwapChainOriginal =
                Marshal.GetDelegateForFunctionPointer<FactoryCreateSwapChainDelegate>(legacyOriginal);
        }

        bool hwndHooked = TryHook(
            hook,
            Marshal.ReadIntPtr(vtable, 15 * IntPtr.Size),
            Marshal.GetFunctionPointerForDelegate(FactoryCreateSwapChainForHwndReplacement),
            out IntPtr hwndOriginal);
        if (hwndHooked)
        {
            _factoryCreateSwapChainForHwndOriginal =
                Marshal.GetDelegateForFunctionPointer<FactoryCreateSwapChainForHwndDelegate>(hwndOriginal);
        }

        bool coreWindowHooked = TryHook(
            hook,
            Marshal.ReadIntPtr(vtable, 16 * IntPtr.Size),
            Marshal.GetFunctionPointerForDelegate(FactoryCreateSwapChainForCoreWindowReplacement),
            out IntPtr coreWindowOriginal);
        if (coreWindowHooked)
        {
            _factoryCreateSwapChainForCoreWindowOriginal =
                Marshal.GetDelegateForFunctionPointer<FactoryCreateSwapChainForCoreWindowDelegate>(
                    coreWindowOriginal);
        }

        bool compositionHooked = TryHook(
            hook,
            Marshal.ReadIntPtr(vtable, 24 * IntPtr.Size),
            Marshal.GetFunctionPointerForDelegate(FactoryCreateSwapChainForCompositionReplacement),
            out IntPtr compositionOriginal);
        if (compositionHooked)
        {
            _factoryCreateSwapChainForCompositionOriginal =
                Marshal.GetDelegateForFunctionPointer<FactoryCreateSwapChainForCompositionDelegate>(
                    compositionOriginal);
        }

        bool installed = legacyHooked || hwndHooked || coreWindowHooked || compositionHooked;
        if (installed)
        {
            _swapChainVtableHooksInstalled = true;
        }
        return installed;
    }

    private static int OnCreateDxgiFactory2(
        uint flags,
        ref Guid interfaceId,
        out IntPtr factory)
    {
        CreateDxgiFactory2Delegate? original = _createDxgiFactory2Original;
        if (original is null)
        {
            factory = IntPtr.Zero;
            return unchecked((int)0x80004005);
        }

        int result = original(flags, ref interfaceId, out factory);
        if (result >= 0 && factory != IntPtr.Zero && _dobbyLibrary != IntPtr.Zero)
        {
            _ = Interlocked.Increment(ref _createFactoryHookCallCount);
            Volatile.Write(ref _lastFactoryInterfaceId, interfaceId.ToString());
            DobbyHookDelegate hook = Marshal.GetDelegateForFunctionPointer<DobbyHookDelegate>(
                NativeLibrary.GetExport(_dobbyLibrary, "DobbyHook"));
            InstallSwapChainVtableHooks(hook, factory, interfaceId);
        }
        return result;
    }

    private static int OnCreateDxgiFactory1(
        ref Guid interfaceId,
        out IntPtr factory)
    {
        CreateDxgiFactory1Delegate? original = _createDxgiFactory1Original;
        if (original is null)
        {
            factory = IntPtr.Zero;
            return unchecked((int)0x80004005);
        }

        int result = original(ref interfaceId, out factory);
        if (result >= 0 && factory != IntPtr.Zero && _dobbyLibrary != IntPtr.Zero)
        {
            _ = Interlocked.Increment(ref _createFactoryHookCallCount);
            Volatile.Write(ref _lastFactoryInterfaceId, interfaceId.ToString());
            DobbyHookDelegate hook = Marshal.GetDelegateForFunctionPointer<DobbyHookDelegate>(
                NativeLibrary.GetExport(_dobbyLibrary, "DobbyHook"));
            InstallSwapChainVtableHooks(hook, factory, interfaceId);
        }
        return result;
    }

    private static int OnCreateDxgiFactory(
        ref Guid interfaceId,
        out IntPtr factory)
    {
        CreateDxgiFactoryDelegate? original = _createDxgiFactoryOriginal;
        if (original is null)
        {
            factory = IntPtr.Zero;
            return unchecked((int)0x80004005);
        }

        int result = original(ref interfaceId, out factory);
        if (result >= 0 && factory != IntPtr.Zero && _dobbyLibrary != IntPtr.Zero)
        {
            _ = Interlocked.Increment(ref _createFactoryHookCallCount);
            Volatile.Write(ref _lastFactoryInterfaceId, interfaceId.ToString());
            DobbyHookDelegate hook = Marshal.GetDelegateForFunctionPointer<DobbyHookDelegate>(
                NativeLibrary.GetExport(_dobbyLibrary, "DobbyHook"));
            InstallSwapChainVtableHooks(hook, factory, interfaceId);
        }
        return result;
    }

    private static int OnCreateDevice(
        IntPtr adapter,
        int minimumFeatureLevel,
        ref Guid interfaceId,
        out IntPtr device)
    {
        CreateDeviceDelegate? original = _createDeviceOriginal;
        if (original is null)
        {
            device = IntPtr.Zero;
            return unchecked((int)0x80004005);
        }

        const int minimumFeatureLevel120 = 0xC000;
        int requestedFeatureLevel = minimumFeatureLevel < minimumFeatureLevel120
            ? minimumFeatureLevel120
            : minimumFeatureLevel;
        int result = original(adapter, requestedFeatureLevel, ref interfaceId, out device);
        _createDeviceRequestedFeatureLevel = requestedFeatureLevel;
        _createDeviceResult = result;
        _createDeviceFellBack = false;
        if (result < 0 && requestedFeatureLevel != minimumFeatureLevel)
        {
            device = IntPtr.Zero;
            result = original(adapter, minimumFeatureLevel, ref interfaceId, out device);
            _createDeviceResult = result;
            _createDeviceFellBack = result >= 0;
        }
        if (result >= 0 && device != IntPtr.Zero)
        {
            lock (PresentationLock)
            {
                if (_presentationGeneration == 0)
                {
                    Volatile.Write(ref _adapter, adapter);
                    Volatile.Write(ref _device, device);
                }
            }
            DeviceReady.Set();
            TryInstallCreateCommandQueueHook(device);
        }
        return result;
    }

    private static void TryInstallCreateCommandQueueHook(IntPtr device)
    {
        if (_createCommandQueueHooked || device == IntPtr.Zero || _dobbyLibrary == IntPtr.Zero)
        {
            return;
        }

        IntPtr vtable = Marshal.ReadIntPtr(device);
        IntPtr createCommandQueue = Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size);
        DobbyHookDelegate hook = Marshal.GetDelegateForFunctionPointer<DobbyHookDelegate>(
            NativeLibrary.GetExport(_dobbyLibrary, "DobbyHook"));
        if (!TryHook(
                hook,
                createCommandQueue,
                Marshal.GetFunctionPointerForDelegate(CreateCommandQueueReplacement),
                out IntPtr original))
        {
            return;
        }

        _createCommandQueueOriginal = Marshal.GetDelegateForFunctionPointer<CreateCommandQueueDelegate>(original);
        _createCommandQueueHooked = true;
    }

    private static int OnCreateCommandQueue(
        IntPtr device,
        IntPtr description,
        ref Guid interfaceId,
        out IntPtr commandQueue)
    {
        CreateCommandQueueDelegate? original = _createCommandQueueOriginal;
        if (original is null)
        {
            commandQueue = IntPtr.Zero;
            return unchecked((int)0x80004005);
        }

        int result = original(device, description, ref interfaceId, out commandQueue);
        if (result >= 0 && commandQueue != IntPtr.Zero)
        {
            _capturedQueueInterfaceId = interfaceId;
            int queueType = -1;
            if (description != IntPtr.Zero)
            {
                D3D12CommandQueueDescription queueDescription =
                    Marshal.PtrToStructure<D3D12CommandQueueDescription>(description);
                queueType = queueDescription.Type;
            }

            IntPtr capturedDevice = Volatile.Read(ref _device);
            IntPtr existingQueue = Volatile.Read(ref _observedCommandQueue);
            if (device == capturedDevice && (queueType == 0 || existingQueue == IntPtr.Zero))
            {
                Volatile.Write(ref _observedCommandQueue, commandQueue);
            }
        }
        return result;
    }

    private static int OnFactoryCreateSwapChain(
        IntPtr factory,
        IntPtr device,
        IntPtr description,
        IntPtr swapChainOutput)
    {
        _ = Interlocked.Increment(ref _factorySwapChainCallCount);
        FactoryCreateSwapChainDelegate? original = _factoryCreateSwapChainOriginal;
        if (original is null)
        {
            return unchecked((int)0x80004005);
        }

        int result = original(factory, device, description, swapChainOutput);
        CapturePresentDevice(result, device, swapChainOutput);
        return result;
    }

    private static int OnFactoryCreateSwapChainForHwnd(
        IntPtr factory,
        IntPtr device,
        IntPtr window,
        IntPtr description,
        IntPtr fullscreenDescription,
        IntPtr restrictToOutput,
        IntPtr swapChainOutput)
    {
        _ = Interlocked.Increment(ref _factorySwapChainCallCount);
        FactoryCreateSwapChainForHwndDelegate? original = _factoryCreateSwapChainForHwndOriginal;
        if (original is null)
        {
            return unchecked((int)0x80004005);
        }

        int result = original(
            factory,
            device,
            window,
            description,
            fullscreenDescription,
            restrictToOutput,
            swapChainOutput);
        CapturePresentDevice(result, device, swapChainOutput);
        return result;
    }

    private static int OnFactoryCreateSwapChainForCoreWindow(
        IntPtr factory,
        IntPtr device,
        IntPtr window,
        IntPtr description,
        IntPtr restrictToOutput,
        IntPtr swapChainOutput)
    {
        _ = Interlocked.Increment(ref _factorySwapChainCallCount);
        FactoryCreateSwapChainForCoreWindowDelegate? original =
            _factoryCreateSwapChainForCoreWindowOriginal;
        if (original is null)
        {
            return unchecked((int)0x80004005);
        }

        int result = original(factory, device, window, description, restrictToOutput, swapChainOutput);
        CapturePresentDevice(result, device, swapChainOutput);
        return result;
    }

    private static int OnFactoryCreateSwapChainForComposition(
        IntPtr factory,
        IntPtr device,
        IntPtr description,
        IntPtr restrictToOutput,
        IntPtr swapChainOutput)
    {
        _ = Interlocked.Increment(ref _factorySwapChainCallCount);
        FactoryCreateSwapChainForCompositionDelegate? original =
            _factoryCreateSwapChainForCompositionOriginal;
        if (original is null)
        {
            return unchecked((int)0x80004005);
        }

        int result = original(factory, device, description, restrictToOutput, swapChainOutput);
        CapturePresentDevice(result, device, swapChainOutput);
        return result;
    }

    private static void CapturePresentDevice(int result, IntPtr unknownDevice, IntPtr swapChainOutput)
    {
        if (result < 0 || unknownDevice == IntPtr.Zero || swapChainOutput == IntPtr.Zero)
        {
            return;
        }

        IntPtr commandQueue = IntPtr.Zero;
        IntPtr queueDevice = IntPtr.Zero;
        IntPtr swapChain = IntPtr.Zero;
        try
        {
            IntPtr createdSwapChain = Marshal.ReadIntPtr(swapChainOutput);
            if (createdSwapChain == IntPtr.Zero)
            {
                return;
            }

            Guid queueInterfaceId = IidCommandQueue;
            int queueResult = Marshal.QueryInterface(unknownDevice, ref queueInterfaceId, out commandQueue);
            if (queueResult < 0 || commandQueue == IntPtr.Zero)
            {
                _ = Interlocked.Increment(ref _capturePresentDeviceQueryFailureCount);
                return;
            }

            IntPtr queueVtable = Marshal.ReadIntPtr(commandQueue);
            GetDeviceDelegate getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceDelegate>(
                Marshal.ReadIntPtr(queueVtable, 7 * IntPtr.Size));
            Guid deviceInterfaceId = Id3D12Device;
            int deviceResult = getDevice(commandQueue, ref deviceInterfaceId, out queueDevice);
            if (deviceResult < 0 || queueDevice == IntPtr.Zero)
            {
                _ = Interlocked.Increment(ref _capturePresentDeviceQueryFailureCount);
                return;
            }

            IntPtr retiredSwapChain = IntPtr.Zero;
            IntPtr hookSwapChain = IntPtr.Zero;
            lock (PresentationLock)
            {
                if (_presentationGeneration > 0 &&
                    (!HaveSameComIdentity(_device, queueDevice) ||
                     !HaveSameComIdentity(_commandQueue, commandQueue)))
                {
                    _ = Interlocked.Increment(ref _capturePresentDeviceQueryFailureCount);
                    _error = "A D3D12 swapchain was created with a different presentation device/queue; " +
                        "the existing authoritative binding was preserved.";
                    return;
                }

                if (_presentationGeneration > 0 && _swapChain == createdSwapChain)
                {
                    return;
                }

                _ = Marshal.AddRef(createdSwapChain);
                swapChain = createdSwapChain;
                if (_presentationGeneration == 0)
                {
                    _device = queueDevice;
                    _commandQueue = commandQueue;
                    _commandQueueType = 0;
                    _capturedQueueInterfaceId = IidCommandQueue;
                    queueDevice = IntPtr.Zero;
                    commandQueue = IntPtr.Zero;
                }
                else
                {
                    retiredSwapChain = _swapChain;
                }
                _swapChain = swapChain;
                _ = Interlocked.Increment(ref _presentationGeneration);
                swapChain = IntPtr.Zero;
                _ = Marshal.AddRef(createdSwapChain);
                hookSwapChain = createdSwapChain;
            }

            ReleaseOwned(retiredSwapChain);
            _ = Interlocked.Increment(ref _swapChainOutputCount);
            try
            {
                TryInstallPresentHook(hookSwapChain);
            }
            finally
            {
                ReleaseOwned(hookSwapChain);
            }
            PresentReady.Set();
        }
        catch (Exception exception)
        {
            _error = exception.ToString();
        }
        finally
        {
            ReleaseOwned(swapChain);
            ReleaseOwned(queueDevice);
            ReleaseOwned(commandQueue);
        }
    }

    private static void ReleaseOwned(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            _ = Marshal.Release(value);
        }
    }

    private static bool HaveSameComIdentity(IntPtr left, IntPtr right)
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
            int leftResult = Marshal.QueryInterface(left, ref interfaceId, out leftIdentity);
            interfaceId = IidUnknown;
            int rightResult = Marshal.QueryInterface(right, ref interfaceId, out rightIdentity);
            return leftResult >= 0 &&
                rightResult >= 0 &&
                leftIdentity != IntPtr.Zero &&
                leftIdentity == rightIdentity;
        }
        finally
        {
            ReleaseOwned(rightIdentity);
            ReleaseOwned(leftIdentity);
        }
    }

    private static int GetPresentOriginalCount()
    {
        lock (PresentOriginals)
        {
            return PresentOriginals.Count;
        }
    }

    private static int GetPresent1OriginalCount()
    {
        lock (Present1Originals)
        {
            return Present1Originals.Count;
        }
    }

    private static void TryInstallPresentHook(IntPtr swapChain)
    {
        if (swapChain == IntPtr.Zero || _dobbyLibrary == IntPtr.Zero)
        {
            return;
        }

        IntPtr vtable = Marshal.ReadIntPtr(swapChain);
        IntPtr present = Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size);
        IntPtr present1 = Marshal.ReadIntPtr(vtable, 18 * IntPtr.Size);
        DobbyHookDelegate hook = Marshal.GetDelegateForFunctionPointer<DobbyHookDelegate>(
            NativeLibrary.GetExport(_dobbyLibrary, "DobbyHook"));
        lock (PresentOriginals)
        {
            if (!PresentOriginals.ContainsKey(present) &&
                TryHook(
                    hook,
                    present,
                    Marshal.GetFunctionPointerForDelegate(PresentReplacement),
                    out IntPtr presentOriginal))
            {
                PresentDelegate originalDelegate =
                    Marshal.GetDelegateForFunctionPointer<PresentDelegate>(presentOriginal);
                PresentOriginals[present] = originalDelegate;
                _presentOriginalFallback ??= originalDelegate;
            }
        }
        lock (Present1Originals)
        {
            if (present1 != IntPtr.Zero &&
                !Present1Originals.ContainsKey(present1) &&
                TryHook(
                    hook,
                    present1,
                    Marshal.GetFunctionPointerForDelegate(Present1Replacement),
                    out IntPtr present1Original))
            {
                Present1Delegate originalDelegate =
                    Marshal.GetDelegateForFunctionPointer<Present1Delegate>(present1Original);
                Present1Originals[present1] = originalDelegate;
                _present1OriginalFallback ??= originalDelegate;
            }
        }
    }

    private static int OnPresent(IntPtr swapChain, uint syncInterval, uint flags)
    {
        _ = Interlocked.Increment(ref _presentSerial);
        StereoPerformanceTelemetry.RecordPresent();
        PresentDelegate? original;
        lock (PresentOriginals)
        {
            IntPtr vtable = Marshal.ReadIntPtr(swapChain);
            IntPtr present = Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size);
            if (!PresentOriginals.TryGetValue(present, out original))
            {
                original = _presentOriginalFallback;
            }
        }

        if (swapChain == SwapChain)
        {
            PresentReady.Set();
        }
        return original!(swapChain, syncInterval, flags);
    }

    private static int OnPresent1(
        IntPtr swapChain,
        uint syncInterval,
        uint flags,
        IntPtr presentParameters)
    {
        _ = Interlocked.Increment(ref _presentSerial);
        StereoPerformanceTelemetry.RecordPresent();
        Present1Delegate? original;
        lock (Present1Originals)
        {
            IntPtr vtable = Marshal.ReadIntPtr(swapChain);
            IntPtr present1 = Marshal.ReadIntPtr(vtable, 18 * IntPtr.Size);
            if (!Present1Originals.TryGetValue(present1, out original))
            {
                original = _present1OriginalFallback;
            }
        }

        if (swapChain == SwapChain)
        {
            PresentReady.Set();
        }
        return original!(swapChain, syncInterval, flags, presentParameters);
    }

    private static bool TryHook(
        DobbyHookDelegate hook,
        IntPtr target,
        IntPtr replacement,
        out IntPtr original)
    {
        int result = hook(target, replacement, out original);
        return result == 0 && original != IntPtr.Zero;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDeviceDelegate(
        IntPtr adapter,
        int minimumFeatureLevel,
        ref Guid interfaceId,
        out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateCommandQueueDelegate(
        IntPtr device,
        IntPtr description,
        ref Guid interfaceId,
        out IntPtr commandQueue);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetDeviceDelegate(
        IntPtr deviceChild,
        ref Guid interfaceId,
        out IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDxgiFactory2Delegate(uint flags, ref Guid interfaceId, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDxgiFactory1Delegate(ref Guid interfaceId, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDxgiFactoryDelegate(ref Guid interfaceId, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int FactoryCreateSwapChainDelegate(
        IntPtr factory,
        IntPtr device,
        IntPtr description,
        IntPtr swapChainOutput);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int FactoryCreateSwapChainForHwndDelegate(
        IntPtr factory,
        IntPtr device,
        IntPtr window,
        IntPtr description,
        IntPtr fullscreenDescription,
        IntPtr restrictToOutput,
        IntPtr swapChainOutput);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int FactoryCreateSwapChainForCoreWindowDelegate(
        IntPtr factory,
        IntPtr device,
        IntPtr window,
        IntPtr description,
        IntPtr restrictToOutput,
        IntPtr swapChainOutput);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int FactoryCreateSwapChainForCompositionDelegate(
        IntPtr factory,
        IntPtr device,
        IntPtr description,
        IntPtr restrictToOutput,
        IntPtr swapChainOutput);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PresentDelegate(IntPtr swapChain, uint syncInterval, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int Present1Delegate(
        IntPtr swapChain,
        uint syncInterval,
        uint flags,
        IntPtr presentParameters);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DobbyHookDelegate(IntPtr target, IntPtr replacement, out IntPtr original);
}
