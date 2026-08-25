using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Doorstop;

internal sealed class D3D11CaptureSnapshot
{
    public bool HookInstalled { get; init; }
    public bool PresentDeviceCaptured { get; init; }
    public bool DeviceCaptured { get; init; }
    public bool ContextCaptured { get; init; }
    public bool SwapChainCaptured { get; init; }
    public string? Error { get; init; }
}

internal static class D3D11DeviceCapture
{
    private static readonly object InstallLock = new();
    private static readonly object CaptureLock = new();
    private static readonly ManualResetEventSlim DeviceReady = new(initialState: false);
    private static readonly ManualResetEventSlim PresentDeviceReady = new(initialState: false);
    private static readonly CreateDeviceDelegate CreateDeviceReplacement = OnCreateDevice;
    private static readonly CreateDeviceAndSwapChainDelegate CreateDeviceAndSwapChainReplacement =
        OnCreateDeviceAndSwapChain;
    private static readonly FactoryCreateSwapChainDelegate FactoryCreateSwapChainReplacement =
        OnFactoryCreateSwapChain;
    private static readonly FactoryCreateSwapChainForHwndDelegate FactoryCreateSwapChainForHwndReplacement =
        OnFactoryCreateSwapChainForHwnd;
    private static readonly FactoryCreateSwapChainForCoreWindowDelegate
        FactoryCreateSwapChainForCoreWindowReplacement = OnFactoryCreateSwapChainForCoreWindow;
    private static readonly FactoryCreateSwapChainForCompositionDelegate
        FactoryCreateSwapChainForCompositionReplacement = OnFactoryCreateSwapChainForComposition;
    private static readonly PresentDelegate PresentReplacement = OnPresent;
    private static CreateDeviceDelegate? _createDeviceOriginal;
    private static CreateDeviceAndSwapChainDelegate? _createDeviceAndSwapChainOriginal;
    private static FactoryCreateSwapChainDelegate? _factoryCreateSwapChainOriginal;
    private static FactoryCreateSwapChainForHwndDelegate? _factoryCreateSwapChainForHwndOriginal;
    private static FactoryCreateSwapChainForCoreWindowDelegate?
        _factoryCreateSwapChainForCoreWindowOriginal;
    private static FactoryCreateSwapChainForCompositionDelegate?
        _factoryCreateSwapChainForCompositionOriginal;
    private static PresentDelegate? _presentOriginal;
    private static IntPtr _device;
    private static IntPtr _context;
    private static IntPtr _swapChain;
    private static IntPtr _presentDevice;
    private static IntPtr _presentContext;
    private static bool _hookInstalled;
    private static bool _presentHookInstalled;
    private static bool _installAttempted;
    private static int _livePresentFrameCount;
    private static long _presentSerial;
    private static bool _firstPresentSnapshotSaved;
    private static bool _secondPresentSnapshotSaved;
    private static D3D11VerticalBlitter? _m6UiBlitter;
    private static IntPtr _m6UiTextureA;
    private static IntPtr _m6UiTextureB;
    private static int _m6UiTextureIndex;
    private static bool _m6UiReadyLogged;
    private static long _nextM6UiFailureMilliseconds;
    private static string? _error;
    private static IntPtr _d3d11Library;
    private static IntPtr _dobbyLibrary;
    private static IntPtr _dxgiLibrary;
    private static readonly Guid Id3D11Device = new("DB6F6DDB-AC77-4E88-8253-819DF9BBF140");
    private static readonly Guid IdxgiFactory2 = new("50C83A1C-E072-4C48-87B0-3630FA36A6D0");

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
                _d3d11Library = NativeLibrary.Load("d3d11.dll");
                string gameRoot = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)
                    ?? Directory.GetCurrentDirectory();
                _dobbyLibrary = NativeLibrary.Load(Path.Combine(gameRoot, "BepInEx", "core", "dobby.dll"));
                DobbyHookDelegate hook = Marshal.GetDelegateForFunctionPointer<DobbyHookDelegate>(
                    NativeLibrary.GetExport(_dobbyLibrary, "DobbyHook"));

                bool createDeviceHooked = TryHook(
                    hook,
                    NativeLibrary.GetExport(_d3d11Library, "D3D11CreateDevice"),
                    Marshal.GetFunctionPointerForDelegate(CreateDeviceReplacement),
                    out IntPtr createDeviceOriginal);
                if (createDeviceHooked)
                {
                    _createDeviceOriginal = Marshal.GetDelegateForFunctionPointer<CreateDeviceDelegate>(
                        createDeviceOriginal);
                }

                bool createSwapChainHooked = TryHook(
                    hook,
                    NativeLibrary.GetExport(_d3d11Library, "D3D11CreateDeviceAndSwapChain"),
                    Marshal.GetFunctionPointerForDelegate(CreateDeviceAndSwapChainReplacement),
                    out IntPtr createSwapChainOriginal);
                if (createSwapChainHooked)
                {
                    _createDeviceAndSwapChainOriginal =
                        Marshal.GetDelegateForFunctionPointer<CreateDeviceAndSwapChainDelegate>(
                            createSwapChainOriginal);
                }

                bool factorySwapChainHooked = InstallFactorySwapChainHooks(hook);
                _hookInstalled = createDeviceHooked || createSwapChainHooked || factorySwapChainHooked;
                if (!_hookInstalled)
                {
                    _error = "Dobby could not hook D3D11 device creation or DXGI swapchain creation.";
                }
            }
            catch (Exception exception)
            {
                _error = $"{exception.GetType().FullName}: {exception.Message}";
            }
        }
    }

    public static bool WaitForDevice(int timeoutMilliseconds)
    {
        return PresentDeviceReady.Wait(timeoutMilliseconds);
    }

    public static D3D11CaptureSnapshot Snapshot() => new()
    {
        HookInstalled = _hookInstalled,
        PresentDeviceCaptured = PresentDevice != IntPtr.Zero,
        DeviceCaptured = Device != IntPtr.Zero,
        ContextCaptured = Context != IntPtr.Zero,
        SwapChainCaptured = Volatile.Read(ref _swapChain) != IntPtr.Zero,
        Error = _error
    };

    internal static IntPtr Device
    {
        get
        {
            IntPtr present = Volatile.Read(ref _presentDevice);
            return present != IntPtr.Zero ? present : Volatile.Read(ref _device);
        }
    }

    internal static IntPtr PresentDevice => Volatile.Read(ref _presentDevice);

    internal static IntPtr Context
    {
        get
        {
            IntPtr present = Volatile.Read(ref _presentContext);
            return present != IntPtr.Zero ? present : Volatile.Read(ref _context);
        }
    }

    internal static IntPtr PresentContext => Volatile.Read(ref _presentContext);

    internal static IntPtr PresentSwapChain =>
        PresentDeviceReady.IsSet ? Volatile.Read(ref _swapChain) : IntPtr.Zero;

    internal static long PresentSerial => Interlocked.Read(ref _presentSerial);

    private static bool InstallFactorySwapChainHooks(DobbyHookDelegate hook)
    {
        _dxgiLibrary = NativeLibrary.Load("dxgi.dll");
        CreateDxgiFactory2Delegate createFactory = Marshal.GetDelegateForFunctionPointer<CreateDxgiFactory2Delegate>(
            NativeLibrary.GetExport(_dxgiLibrary, "CreateDXGIFactory2"));
        Guid interfaceId = IdxgiFactory2;
        int result = createFactory(0, ref interfaceId, out IntPtr factory);
        if (result < 0 || factory == IntPtr.Zero)
        {
            _error = $"CreateDXGIFactory2 failed while installing swapchain hooks: HRESULT=0x{result:x8}.";
            return false;
        }

        try
        {
            IntPtr vtable = Marshal.ReadIntPtr(factory);
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

            return legacyHooked || hwndHooked || coreWindowHooked || compositionHooked;
        }
        finally
        {
            _ = Marshal.Release(factory);
        }
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

    private static int OnCreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        IntPtr deviceOutput,
        IntPtr featureLevelOutput,
        IntPtr immediateContextOutput)
    {
        CreateDeviceDelegate? original = _createDeviceOriginal;
        if (original is null)
        {
            return unchecked((int)0x80004005);
        }

        int result = original(
            adapter,
            driverType,
            software,
            flags,
            featureLevels,
            featureLevelCount,
            sdkVersion,
            deviceOutput,
            featureLevelOutput,
            immediateContextOutput);
        CaptureOutputs(result, IntPtr.Zero, deviceOutput, immediateContextOutput);
        return result;
    }

    private static int OnCreateDeviceAndSwapChain(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        IntPtr swapChainDescription,
        IntPtr swapChainOutput,
        IntPtr deviceOutput,
        IntPtr featureLevelOutput,
        IntPtr immediateContextOutput)
    {
        CreateDeviceAndSwapChainDelegate? original = _createDeviceAndSwapChainOriginal;
        if (original is null)
        {
            return unchecked((int)0x80004005);
        }

        int result = original(
            adapter,
            driverType,
            software,
            flags,
            featureLevels,
            featureLevelCount,
            sdkVersion,
            swapChainDescription,
            swapChainOutput,
            deviceOutput,
            featureLevelOutput,
            immediateContextOutput);
        CaptureOutputs(result, swapChainOutput, deviceOutput, immediateContextOutput);
        return result;
    }

    private static void CaptureOutputs(
        int result,
        IntPtr swapChainOutput,
        IntPtr deviceOutput,
        IntPtr immediateContextOutput)
    {
        if (result < 0)
        {
            return;
        }

        try
        {
            IntPtr device = IntPtr.Zero;
            IntPtr swapChain = IntPtr.Zero;
            if (deviceOutput != IntPtr.Zero)
            {
                device = Marshal.ReadIntPtr(deviceOutput);
                Volatile.Write(ref _device, device);
            }

            if (immediateContextOutput != IntPtr.Zero)
            {
                Volatile.Write(ref _context, Marshal.ReadIntPtr(immediateContextOutput));
            }

            if (swapChainOutput != IntPtr.Zero)
            {
                swapChain = Marshal.ReadIntPtr(swapChainOutput);
                Volatile.Write(ref _swapChain, swapChain);
            }

            if (Volatile.Read(ref _device) != IntPtr.Zero)
            {
                DeviceReady.Set();
            }

            if (device != IntPtr.Zero && swapChain != IntPtr.Zero)
            {
                CapturePresentDevice(result, device, swapChainOutput);
            }
        }
        catch (Exception exception)
        {
            _error = $"{exception.GetType().FullName}: {exception.Message}";
        }
    }

    private static int OnFactoryCreateSwapChain(
        IntPtr factory,
        IntPtr device,
        IntPtr description,
        IntPtr swapChainOutput)
    {
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

    private static int OnFactoryCreateSwapChainForComposition(
        IntPtr factory,
        IntPtr device,
        IntPtr description,
        IntPtr restrictToOutput,
        IntPtr swapChainOutput)
    {
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

    private static int OnFactoryCreateSwapChainForCoreWindow(
        IntPtr factory,
        IntPtr device,
        IntPtr window,
        IntPtr description,
        IntPtr restrictToOutput,
        IntPtr swapChainOutput)
    {
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

    private static void CapturePresentDevice(int result, IntPtr unknownDevice, IntPtr swapChainOutput)
    {
        if (result < 0 || unknownDevice == IntPtr.Zero)
        {
            return;
        }

        Guid interfaceId = Id3D11Device;
        int queryResult = Marshal.QueryInterface(
            unknownDevice,
            ref interfaceId,
            out IntPtr d3D11Device);
        if (queryResult < 0 || d3D11Device == IntPtr.Zero)
        {
            return;
        }

        try
        {
            IntPtr vtable = Marshal.ReadIntPtr(d3D11Device);
            GetImmediateContextDelegate getImmediateContext =
                Marshal.GetDelegateForFunctionPointer<GetImmediateContextDelegate>(
                    Marshal.ReadIntPtr(vtable, 40 * IntPtr.Size));
            getImmediateContext(d3D11Device, out IntPtr immediateContext);
            if (immediateContext == IntPtr.Zero)
            {
                return;
            }

            lock (CaptureLock)
            {
                IntPtr oldDevice = _presentDevice;
                IntPtr oldContext = _presentContext;
                Volatile.Write(ref _presentDevice, d3D11Device);
                Volatile.Write(ref _presentContext, immediateContext);
                d3D11Device = IntPtr.Zero;

                if (swapChainOutput != IntPtr.Zero)
                {
                    IntPtr swapChain = Marshal.ReadIntPtr(swapChainOutput);
                    Volatile.Write(ref _swapChain, swapChain);
                    TryInstallPresentHook(swapChain);
                }

                if (oldContext != IntPtr.Zero)
                {
                    _ = Marshal.Release(oldContext);
                }

                if (oldDevice != IntPtr.Zero)
                {
                    _ = Marshal.Release(oldDevice);
                }

                PresentDeviceReady.Set();
                DeviceReady.Set();
            }
        }
        catch (Exception exception)
        {
            _error = $"{exception.GetType().FullName}: {exception.Message}";
        }
        finally
        {
            if (d3D11Device != IntPtr.Zero)
            {
                _ = Marshal.Release(d3D11Device);
            }
        }
    }

    private static void TryInstallPresentHook(IntPtr swapChain)
    {
        if (_presentHookInstalled || swapChain == IntPtr.Zero || _dobbyLibrary == IntPtr.Zero)
        {
            return;
        }

        IntPtr vtable = Marshal.ReadIntPtr(swapChain);
        IntPtr present = Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size);
        DobbyHookDelegate hook = Marshal.GetDelegateForFunctionPointer<DobbyHookDelegate>(
            NativeLibrary.GetExport(_dobbyLibrary, "DobbyHook"));
        if (!TryHook(
                hook,
                present,
                Marshal.GetFunctionPointerForDelegate(PresentReplacement),
                out IntPtr original))
        {
            return;
        }

        _presentOriginal = Marshal.GetDelegateForFunctionPointer<PresentDelegate>(original);
        _presentHookInstalled = true;
    }

    private static int OnPresent(IntPtr swapChain, uint syncInterval, uint flags)
    {
        PresentDelegate? original = _presentOriginal;
        if (original is null)
        {
            return unchecked((int)0x80004005);
        }

        try
        {
            if (swapChain == Volatile.Read(ref _swapChain))
            {
                _ = Interlocked.Increment(ref _presentSerial);
                StereoPerformanceTelemetry.RecordPresent();
                // The unified panel path uses the final game backbuffer directly.
                // Do not spend a second full-resolution pass producing the retired keyed UI layer.
                TrySaveSynchronizedLiveSnapshot(swapChain);
            }
        }
        catch (Exception exception)
        {
            _error = $"Present snapshot: {exception.GetType().FullName}: {exception.Message}";
        }

        return original(swapChain, syncInterval, flags);
    }

    private static void TryCaptureSynchronizedM6Ui(IntPtr swapChain)
    {
        using D3D11TextureLease? worldTexture =
            UnityRenderSourceRegistry.AcquireLiveWorldTexture(1_500);
        if (worldTexture is null ||
            !worldTexture.SourceName.StartsWith(
                "M6_NONLIVE|",
                StringComparison.Ordinal))
        {
            return;
        }

        IntPtr device = Volatile.Read(ref _presentDevice);
        IntPtr context = Volatile.Read(ref _presentContext);
        if (device == IntPtr.Zero || context == IntPtr.Zero)
        {
            UnityRenderSourceRegistry.ClearLiveUiTexture();
            return;
        }

        IntPtr backBuffer = IntPtr.Zero;
        try
        {
            backBuffer = D3D11Interop.GetSwapChainBackBuffer(swapChain);
            D3D11Texture2DDescription backBufferDescription =
                D3D11Interop.GetTextureDescription(backBuffer);
            D3D11Texture2DDescription worldDescription =
                D3D11Interop.GetTextureDescription(worldTexture.Texture);
            if (backBufferDescription.SampleCount != 1 ||
                worldDescription.SampleCount != 1)
            {
                throw new InvalidOperationException(
                    "M6 synchronized UI requires single-sample backbuffer and world textures.");
            }

            EnsureM6UiTextures(device, backBufferDescription);
            IntPtr destination = (_m6UiTextureIndex++ & 1) == 0
                ? _m6UiTextureA
                : _m6UiTextureB;
            _m6UiBlitter ??= new D3D11VerticalBlitter(device);
            _m6UiBlitter.BlitUiDifference(
                context,
                destination,
                backBuffer,
                worldTexture.Texture,
                backBufferDescription.Width,
                backBufferDescription.Height,
                backBufferDescription.Format,
                worldDescription.Format);
            UnityRenderSourceRegistry.UpdateLiveUiTexture(
                destination,
                "M6_SYNC_UI|backbuffer-world-difference");
            if (!_m6UiReadyLogged)
            {
                RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Event = "m6-synchronized-ui-ready",
                    BootstrapVersion = RuntimeProbe.BootstrapVersion,
                    ProcessId = Environment.ProcessId,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    UiCaptureSubmitted = true,
                    UiCaptureWidth = checked((int)backBufferDescription.Width),
                    UiCaptureHeight = checked((int)backBufferDescription.Height),
                    UiCaptureTextureDescription =
                        D3D11Interop.DescribeTexture(destination),
                    Reason = "Backbuffer and approved world RT were differenced on the game Present boundary before OpenXR consumption."
                });
                _m6UiReadyLogged = true;
            }
        }
        catch (Exception exception)
        {
            UnityRenderSourceRegistry.ClearLiveUiTexture();
            long now = Environment.TickCount64;
            if (now >= Interlocked.Read(ref _nextM6UiFailureMilliseconds))
            {
                RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Event = "m6-synchronized-ui-failure",
                    BootstrapVersion = RuntimeProbe.BootstrapVersion,
                    ProcessId = Environment.ProcessId,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    UiCaptureSubmitted = false,
                    ErrorType = exception.GetType().FullName,
                    Error = exception.Message,
                    Reason = "M6 stereo remains blocked and the final backbuffer SafePanel remains active."
                });
                Interlocked.Exchange(
                    ref _nextM6UiFailureMilliseconds,
                    now + 10_000);
            }
        }
        finally
        {
            if (backBuffer != IntPtr.Zero)
            {
                D3D11Interop.Release(backBuffer);
            }
        }
    }

    private static void EnsureM6UiTextures(
        IntPtr device,
        D3D11Texture2DDescription backBufferDescription)
    {
        if (_m6UiTextureA != IntPtr.Zero &&
            _m6UiTextureB != IntPtr.Zero)
        {
            D3D11Texture2DDescription existing =
                D3D11Interop.GetTextureDescription(_m6UiTextureA);
            if (existing.Width == backBufferDescription.Width &&
                existing.Height == backBufferDescription.Height &&
                existing.Format == backBufferDescription.Format)
            {
                return;
            }
        }

        UnityRenderSourceRegistry.ClearLiveUiTexture();
        _m6UiBlitter?.ResetViews();
        if (_m6UiTextureA != IntPtr.Zero)
        {
            D3D11Interop.Release(_m6UiTextureA);
            _m6UiTextureA = IntPtr.Zero;
        }
        if (_m6UiTextureB != IntPtr.Zero)
        {
            D3D11Interop.Release(_m6UiTextureB);
            _m6UiTextureB = IntPtr.Zero;
        }

        _m6UiTextureA = D3D11Interop.CreateShaderReadableRenderTarget(
            device,
            backBufferDescription);
        try
        {
            _m6UiTextureB = D3D11Interop.CreateShaderReadableRenderTarget(
                device,
                backBufferDescription);
        }
        catch
        {
            D3D11Interop.Release(_m6UiTextureA);
            _m6UiTextureA = IntPtr.Zero;
            throw;
        }
        _m6UiTextureIndex = 0;
    }

    private static void TrySaveSynchronizedLiveSnapshot(IntPtr swapChain)
    {
        if (_secondPresentSnapshotSaved)
        {
            return;
        }

        using D3D11TextureLease? worldTexture =
            UnityRenderSourceRegistry.AcquireLiveWorldTexture(1_500);
        if (worldTexture is null)
        {
            _livePresentFrameCount = 0;
            return;
        }

        D3D11Texture2DDescription worldDescription =
            D3D11Interop.GetTextureDescription(worldTexture.Texture);
        if (worldDescription.Width != 3840 || worldDescription.Height != 1634)
        {
            _livePresentFrameCount = 0;
            return;
        }

        _livePresentFrameCount++;
        bool saveFirst = !_firstPresentSnapshotSaved && _livePresentFrameCount >= 30;
        bool saveSecond = _firstPresentSnapshotSaved &&
            !_secondPresentSnapshotSaved &&
            _livePresentFrameCount >= 600;
        if (!saveFirst && !saveSecond)
        {
            return;
        }

        IntPtr device = Volatile.Read(ref _presentDevice);
        IntPtr context = Volatile.Read(ref _presentContext);
        if (device == IntPtr.Zero || context == IntPtr.Zero)
        {
            return;
        }

        IntPtr backBuffer = D3D11Interop.GetSwapChainBackBuffer(swapChain);
        try
        {
            D3D11Texture2DDescription backBufferDescription =
                D3D11Interop.GetTextureDescription(backBuffer);
            if (backBufferDescription.Width != 1920 || backBufferDescription.Height != 1080)
            {
                return;
            }

            string? executable = Process.GetCurrentProcess().MainModule?.FileName;
            string gameRoot = executable is null
                ? Directory.GetCurrentDirectory()
                : Path.GetDirectoryName(executable) ?? Directory.GetCurrentDirectory();
            string logDirectory = Path.Combine(gameRoot, "vrmod", "logs");
            string label = saveFirst ? "sample1" : "sample2";
            D3D11Interop.SaveTextureBmp(
                device,
                context,
                backBuffer,
                Path.Combine(logDirectory, $"v0.53-present-{label}-backbuffer.bmp"));
            D3D11Interop.SaveTextureBmp(
                device,
                context,
                worldTexture.Texture,
                Path.Combine(logDirectory, $"v0.53-present-{label}-world.bmp"));

            if (saveFirst)
            {
                _firstPresentSnapshotSaved = true;
            }
            else
            {
                _secondPresentSnapshotSaved = true;
            }
        }
        finally
        {
            D3D11Interop.Release(backBuffer);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDeviceDelegate(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        IntPtr deviceOutput,
        IntPtr featureLevelOutput,
        IntPtr immediateContextOutput);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDeviceAndSwapChainDelegate(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        IntPtr swapChainDescription,
        IntPtr swapChainOutput,
        IntPtr deviceOutput,
        IntPtr featureLevelOutput,
        IntPtr immediateContextOutput);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDxgiFactory2Delegate(uint flags, ref Guid interfaceId, out IntPtr factory);

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
    private delegate void GetImmediateContextDelegate(IntPtr device, out IntPtr immediateContext);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PresentDelegate(IntPtr swapChain, uint syncInterval, uint flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DobbyHookDelegate(IntPtr target, IntPtr replacement, out IntPtr original);
}
