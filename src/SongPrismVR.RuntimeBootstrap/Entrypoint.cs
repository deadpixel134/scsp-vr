using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using SongPrismVR.Core;

namespace Doorstop;

public static class Entrypoint
{
    private static readonly object StartLock = new();
    private static Thread? _worker;

    public static void Start()
    {
        lock (StartLock)
        {
            if (_worker is not null)
            {
                return;
            }

            string logPath = RuntimeProbe.GetLogPath();
            VrSettings settings = VrSettingsRuntime.Initialize(logPath);
            if (!settings.Runtime.Enabled)
            {
                RuntimeProbe.Append(logPath, new ProbeEvent
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Event = "vr-runtime-disabled",
                    BootstrapVersion = RuntimeProbe.BootstrapVersion,
                    ProcessId = Environment.ProcessId,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    Reason = "settings.runtime.enabled=false; no graphics or IL2CPP hooks were installed."
                });
                return;
            }

            if (Environment.GetEnvironmentVariable("SONGPRISM_VR_NO_HOOK_DIAG") == "1")
            {
                RuntimeProbe.RunNoHookDiagnostic();
                return;
            }

            string gameRoot = Path.GetDirectoryName(
                Process.GetCurrentProcess().MainModule?.FileName) ??
                Directory.GetCurrentDirectory();
            UnityD3D12InterfaceProbe.InstallEarly(gameRoot, logPath);
            D3D12DeviceCapture.Install();

            _worker = new Thread(RuntimeProbe.Run)
            {
                IsBackground = true,
                Name = "SongPrismVR Runtime Probe",
                Priority = ThreadPriority.Normal
            };
            _worker.Start();
        }
    }
}

internal static class RuntimeProbe
{
    private static readonly object LogLock = new();
    internal const string BootstrapVersion = "0.1.1";
    private const int ModuleTimeoutMilliseconds = 60_000;
    private const int DomainTimeoutMilliseconds = 60_000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static void Run()
    {
        string logPath = GetLogPath();
        try
        {
            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "bootstrap-start",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Reason =
                    $"workerThreadPriority={Thread.CurrentThread.Priority};" +
                    "openXrGpuWait=spin-up-to-1ms-then-yield"
            });

            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "process-elevation",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Reason = $"elevated={IsProcessElevated()}"
            });

            D3D12CaptureSnapshot startupSnapshot = D3D12DeviceCapture.Snapshot();
            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "d3d12-hook-install-status",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                D3D12HookInstalled = startupSnapshot.HookInstalled,
                D3D12DeviceCaptured = startupSnapshot.DeviceCaptured,
                D3D12CommandQueueCaptured = startupSnapshot.CommandQueueCaptured,
                D3D12SwapChainCaptured = startupSnapshot.SwapChainCaptured,
                D3D12CreateFactoryHookCallCount = startupSnapshot.CreateFactoryHookCallCount,
                D3D12SwapChainVtableHooksInstalled = startupSnapshot.SwapChainVtableHooksInstalled,
                D3D12FactorySwapChainCallCount = startupSnapshot.FactorySwapChainCallCount,
                D3D12CapturePresentDeviceQueryFailureCount = startupSnapshot.CapturePresentDeviceQueryFailureCount,
                D3D12SwapChainOutputCount = startupSnapshot.SwapChainOutputCount,
                D3D12PresentationGeneration = startupSnapshot.PresentationGeneration,
                D3D12PresentHookInstalled = startupSnapshot.PresentHookInstalled,
                D3D12PresentHookCount = startupSnapshot.PresentHookCount,
                D3D12Present1HookCount = startupSnapshot.Present1HookCount,
                D3D12LastFactoryInterfaceId = startupSnapshot.LastFactoryInterfaceId,
                Error = startupSnapshot.Error
            });

            if (D3D12DeviceCapture.WaitForPresent(15_000))
            {
                RunD3D12PanelOnly(logPath);
                return;
            }

            D3D12CaptureSnapshot unavailableSnapshot = D3D12DeviceCapture.Snapshot();
            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "d3d12-device-unavailable",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Reason = "D3D12 present/swapchain was not captured within the M0 panel-only startup window.",
                D3D12HookInstalled = unavailableSnapshot.HookInstalled,
                D3D12DeviceCaptured = unavailableSnapshot.DeviceCaptured,
                D3D12CommandQueueCaptured = unavailableSnapshot.CommandQueueCaptured,
                D3D12SwapChainCaptured = unavailableSnapshot.SwapChainCaptured,
                D3D12CreateFactoryHookCallCount = unavailableSnapshot.CreateFactoryHookCallCount,
                D3D12SwapChainVtableHooksInstalled = unavailableSnapshot.SwapChainVtableHooksInstalled,
                D3D12FactorySwapChainCallCount = unavailableSnapshot.FactorySwapChainCallCount,
                D3D12CapturePresentDeviceQueryFailureCount = unavailableSnapshot.CapturePresentDeviceQueryFailureCount,
                D3D12SwapChainOutputCount = unavailableSnapshot.SwapChainOutputCount,
                D3D12PresentationGeneration = unavailableSnapshot.PresentationGeneration,
                D3D12PresentHookInstalled = unavailableSnapshot.PresentHookInstalled,
                D3D12PresentHookCount = unavailableSnapshot.PresentHookCount,
                D3D12Present1HookCount = unavailableSnapshot.Present1HookCount,
                D3D12LastFactoryInterfaceId = unavailableSnapshot.LastFactoryInterfaceId,
                Error = unavailableSnapshot.Error
            });
        }
        catch (Exception exception)
        {
            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "bootstrap-failure",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ErrorType = exception.GetType().FullName,
                Error = exception.Message
            });
        }
    }

    internal static void RunNoHookDiagnostic()
    {
        string logPath = GetLogPath();
        try
        {
            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "bootstrap-start",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Reason = "no-hook-diagnostic; D3D12/DXGI hooks were intentionally not installed."
            });

            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "process-elevation",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Reason = $"elevated={IsProcessElevated()}"
            });

            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "openxr-loaded-modules",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Reason = DescribeOpenXrModules()
            });

            D3D11OpenXrSessionProbeResult probe = D3D11OpenXrSessionProbe.Run();
            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "d3d11-openxr-session-probe-summary",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                OpenXrSessionCreateResult = probe.SessionCreateResult,
                OpenXrSessionCreated = probe.SessionCreated,
                OpenXrFrameLoopStage = probe.Stage,
                Error = probe.Error,
                Reason = "D3D11 no-hook diagnostic completed without installing D3D12/DXGI hooks."
            });
        }
        catch (Exception exception)
        {
            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "bootstrap-failure",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ErrorType = exception.GetType().FullName,
                Error = exception.Message
            });
        }
    }

    private static void RunD3D12PanelOnly(string logPath)
    {
        D3D12CaptureSnapshot graphics = D3D12DeviceCapture.Snapshot();
        Append(logPath, new ProbeEvent
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = graphics.DeviceCaptured
                ? "d3d12-device-ready"
                : "d3d12-device-unavailable",
            BootstrapVersion = BootstrapVersion,
            ProcessId = Environment.ProcessId,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            D3D12HookInstalled = graphics.HookInstalled,
            D3D12DeviceCaptured = graphics.DeviceCaptured,
            D3D12CommandQueueCaptured = graphics.CommandQueueCaptured,
            D3D12SwapChainCaptured = graphics.SwapChainCaptured,
            D3D12CreateFactoryHookCallCount = graphics.CreateFactoryHookCallCount,
            D3D12SwapChainVtableHooksInstalled = graphics.SwapChainVtableHooksInstalled,
            D3D12FactorySwapChainCallCount = graphics.FactorySwapChainCallCount,
            D3D12SwapChainOutputCount = graphics.SwapChainOutputCount,
            D3D12PresentHookCount = graphics.PresentHookCount,
            D3D12Present1HookCount = graphics.Present1HookCount,
            D3D12LastFactoryInterfaceId = graphics.LastFactoryInterfaceId,
            Error = graphics.Error
        });

        if (!graphics.SwapChainCaptured)
        {
            return;
        }

        if (!D3D12DeviceCapture.TryAcquirePresentationBinding(
                out D3D12PresentationBindingLease presentationBinding))
        {
            return;
        }

        using (presentationBinding)
        {
            IntPtr backBuffer = IntPtr.Zero;
            try
            {
                backBuffer = D3D12Interop.GetSwapChainBackBuffer(presentationBinding.SwapChain);
                if (backBuffer == IntPtr.Zero)
                {
                    throw new InvalidOperationException("IDXGISwapChain::GetBuffer returned a null D3D12 resource.");
                }
                D3D12ResourceDescription backBufferDescription =
                    D3D12Interop.GetResourceDescription(backBuffer);
                string backBufferDescriptionText = D3D12Interop.DescribeResource(backBuffer);
                Append(logPath, new ProbeEvent
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Event = "d3d12-backbuffer-info",
                    BootstrapVersion = BootstrapVersion,
                    ProcessId = Environment.ProcessId,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    D3D12BackBufferWidth = backBufferDescription.Width,
                    D3D12BackBufferHeight = backBufferDescription.Height,
                    D3D12BackBufferFormat = backBufferDescription.Format,
                    D3D12BackBufferSamples = backBufferDescription.SampleCount,
                    D3D12BackBufferDescription = backBufferDescriptionText
                });
            }
            catch (Exception exception)
            {
                Append(logPath, new ProbeEvent
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Event = "d3d12-backbuffer-info-failure",
                    BootstrapVersion = BootstrapVersion,
                    ProcessId = Environment.ProcessId,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    ErrorType = exception.GetType().FullName,
                    Error = exception.ToString(),
                    D3D12LastFactoryInterfaceId = graphics.LastFactoryInterfaceId,
                    D3D12CreateFactoryHookCallCount = graphics.CreateFactoryHookCallCount,
                    D3D12FactorySwapChainCallCount = graphics.FactorySwapChainCallCount,
                    D3D12SwapChainOutputCount = graphics.SwapChainOutputCount,
                    D3D12PresentHookCount = graphics.PresentHookCount,
                    D3D12Present1HookCount = graphics.Present1HookCount
                });
                return;
            }
            finally
            {
                D3D12Interop.Release(backBuffer);
            }
        }

        if (Environment.GetEnvironmentVariable("SONGPRISM_VR_RUN_D3D11_PROBE") == "1")
        {
            D3D11OpenXrSessionProbeResult d3d11Probe = D3D11OpenXrSessionProbe.Run();
            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "d3d11-openxr-session-probe-summary",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                OpenXrSessionCreateResult = d3d11Probe.SessionCreateResult,
                OpenXrSessionCreated = d3d11Probe.SessionCreated,
                OpenXrFrameLoopStage = d3d11Probe.Stage,
                Error = d3d11Probe.Error,
                Reason = "D3D11 binding session probe was explicitly requested before D3D12 session creation."
            });
        }
        else
        {
            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "d3d11-openxr-session-probe-skipped",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                Reason = "D3D11 binding session probe was skipped before D3D12 session creation to avoid a second OpenXR instance in the same process."
            });
        }

        StartMainThreadSampler(logPath);

        D3D12OpenXrPanelResult panelResult = D3D12OpenXrPanelRuntime.Run();
        Append(logPath, new ProbeEvent
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = "d3d12-openxr-panel-result",
            BootstrapVersion = BootstrapVersion,
            ProcessId = Environment.ProcessId,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OpenXrInstanceCreated = panelResult.InstanceCreated,
            OpenXrSessionCreated = panelResult.SessionCreated,
            OpenXrTestPatternLayerFramesSubmitted = panelResult.FramesSubmitted,
            OpenXrFrameLoopStage = panelResult.Stage,
            OpenXrFrameLoopResult = panelResult.FrameLoopResult,
            Error = panelResult.Error,
            Reason = "D3D12 panel-only runtime completed without IL2CPP main-thread sampling."
        });

    }

    private static void StartMainThreadSampler(string logPath)
    {
        Thread samplerThread = new(() => RunMainThreadSampler(logPath))
        {
            IsBackground = true,
            Name = "SongPrismVR Main Thread Sampler",
            Priority = ThreadPriority.Normal
        };
        samplerThread.Start();
    }

    private static void RunMainThreadSampler(string logPath)
    {
        try
        {
            IntPtr gameAssembly = WaitForModule("GameAssembly.dll", ModuleTimeoutMilliseconds);
            Il2CppApi api = Il2CppApi.Load(gameAssembly);
            IntPtr domain = WaitForDomain(api, DomainTimeoutMilliseconds);
            _ = WaitForAssemblies(api, domain, ModuleTimeoutMilliseconds);
            MainThreadSampler sampler = new(api, domain, logPath);
            sampler.Run();
        }
        catch (Exception exception)
        {
            Append(logPath, new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "main-thread-sampler-start-failure",
                BootstrapVersion = BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ErrorType = exception.GetType().FullName,
                Error = exception.ToString()
            });
        }
    }

    private static IntPtr WaitForModule(string moduleName, int timeoutMilliseconds)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            IntPtr module = NativeMethods.GetModuleHandle(moduleName);
            if (module != IntPtr.Zero)
            {
                return module;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"Timed out waiting for {moduleName}.");
    }

    private static bool IsProcessElevated()
    {
        try
        {
            if (!NativeMethods.OpenProcessToken(
                    Process.GetCurrentProcess().Handle,
                    0x0008,
                    out IntPtr token))
            {
                return false;
            }

            try
            {
                if (!NativeMethods.GetTokenInformation(
                        token,
                        20,
                        out int elevation,
                        sizeof(int),
                        out _))
                {
                    return false;
                }

                return elevation != 0;
            }
            finally
            {
                _ = NativeMethods.CloseHandle(token);
            }
        }
        catch
        {
            return false;
        }
    }

    private static string DescribeOpenXrModules()
    {
        try
        {
            IReadOnlyList<string> modules = Process.GetCurrentProcess()
                .Modules
                .Cast<System.Diagnostics.ProcessModule>()
                .Where(module =>
                    module.ModuleName?.Contains("openxr", StringComparison.OrdinalIgnoreCase) == true ||
                    module.ModuleName?.Contains("d3d11", StringComparison.OrdinalIgnoreCase) == true ||
                    module.ModuleName?.Contains("dxgi", StringComparison.OrdinalIgnoreCase) == true ||
                    module.ModuleName?.Contains("d3d12", StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(module => module.ModuleName, StringComparer.OrdinalIgnoreCase)
                .Select(module => $"{module.ModuleName}={module.FileName}")
                .ToArray();
            return string.Join(";", modules);
        }
        catch (Exception exception)
        {
            return $"module-enumeration-failed:{exception.GetType().Name}:{exception.Message}";
        }
    }

    private static IntPtr WaitForDomain(Il2CppApi api, int timeoutMilliseconds)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            IntPtr domain = api.DomainGet();
            if (domain != IntPtr.Zero)
            {
                return domain;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException("Timed out waiting for an initialized IL2CPP domain.");
    }

    private static IReadOnlyList<string> WaitForAssemblies(
        Il2CppApi api,
        IntPtr domain,
        int timeoutMilliseconds)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            IntPtr assemblyPointers = api.DomainGetAssemblies(domain, out UIntPtr rawCount);
            ulong count = rawCount.ToUInt64();
            if (assemblyPointers != IntPtr.Zero && count > 0)
            {
                return ReadAssemblies(api, assemblyPointers, count);
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException("Timed out waiting for IL2CPP assemblies.");
    }

    private static IReadOnlyList<string> ReadAssemblies(
        Il2CppApi api,
        IntPtr assemblyPointers,
        ulong count)
    {
        if (count > 4096)
        {
            throw new InvalidOperationException($"Invalid IL2CPP assembly table: pointer={assemblyPointers}, count={count}.");
        }

        List<string> names = new((int)count);
        for (ulong index = 0; index < count; index++)
        {
            IntPtr assembly = Marshal.ReadIntPtr(assemblyPointers, checked((int)(index * (ulong)IntPtr.Size)));
            if (assembly == IntPtr.Zero)
            {
                continue;
            }

            IntPtr image = api.AssemblyGetImage(assembly);
            IntPtr namePointer = image == IntPtr.Zero ? IntPtr.Zero : api.ImageGetName(image);
            string? name = namePointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(namePointer);
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    internal static string GetLogPath()
    {
        string? executable = Process.GetCurrentProcess().MainModule?.FileName;
        string gameRoot = executable is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(executable) ?? Directory.GetCurrentDirectory();
        string logDirectory = Path.Combine(gameRoot, "vrmod", "logs");
        Directory.CreateDirectory(logDirectory);
        return Path.Combine(logDirectory, "runtime-bootstrap.jsonl");
    }

    internal static void Append(string path, ProbeEvent value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        lock (LogLock)
        {
            try
            {
                File.AppendAllText(path, json + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging must never take down the game loop; drop the entry
                // when another process briefly holds the log file open.
            }
        }
    }
}

internal sealed class ProbeEvent
{
    public DateTimeOffset TimestampUtc { get; set; }

    public string Event { get; set; } = string.Empty;

    public string BootstrapVersion { get; set; } = RuntimeProbe.BootstrapVersion;

    public int ProcessId { get; set; }

    public string Architecture { get; set; } = string.Empty;

    public int? AssemblyCount { get; set; }

    public IReadOnlyList<string>? Assemblies { get; set; }

    public string? ErrorType { get; set; }

    public string? Error { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public int? Orientation { get; set; }

    public int? CameraCount { get; set; }

    public IReadOnlyList<CameraProbeRecord>? Cameras { get; set; }

    public IReadOnlyList<CanvasProbeRecord>? UiCanvases { get; set; }

    public IReadOnlyList<RawImageProbeRecord>? RawImages { get; set; }

    public IReadOnlyList<VideoPlayerProbeRecord>? VideoPlayers { get; set; }

    public IReadOnlyList<MediaSurfaceProbeRecord>? MediaSurfaces { get; set; }

    public IReadOnlyList<string>? MediaSurfaceTypes { get; set; }

    public IReadOnlyList<UiGraphicProbeRecord>? UiGraphics { get; set; }

    public IReadOnlyList<Il2CppTypeProbeRecord>? UiReplayTypes { get; set; }

    public IReadOnlyList<Il2CppTypeProbeRecord>? UrpRequestTypes { get; set; }

    public string? UiCaptureStage { get; set; }

    public bool? UiCaptureSubmitted { get; set; }

    public bool? UiCaptureRawImageSuppressed { get; set; }

    public bool? UiCaptureRawImageRestored { get; set; }

    public bool? UiCaptureDestinationVerified { get; set; }

    public bool? UiCaptureRequestSupported { get; set; }

    public int? UiCaptureWidth { get; set; }

    public int? UiCaptureHeight { get; set; }

    public string? UiCaptureTextureDescription { get; set; }

    public int? UiCaptureFrameCount { get; set; }

    public int? UiCaptureSuppressedGraphicCount { get; set; }

    public string? Scene { get; set; }

    public string? TransitionState { get; set; }

    public string? OrientationKind { get; set; }

    public bool? FreezeFrame { get; set; }

    public bool? BlockPointerInput { get; set; }

    public bool? RequestRebind { get; set; }

    public string? PresentationContext { get; set; }

    public string? PresentationMode { get; set; }

    public string? Reason { get; set; }

    public string? ActiveRuntimeManifest { get; set; }

    public string? ActiveRuntimeName { get; set; }

    public string? OpenXrLoaderPath { get; set; }

    public string? OpenXrLoaderVersion { get; set; }

    public int? OpenXrExtensionCount { get; set; }

    public IReadOnlyList<string>? OpenXrExtensions { get; set; }

    public bool? SupportsD3D11 { get; set; }

    public bool? OpenXrInstanceCreated { get; set; }

    public string? OpenXrRuntimeVersion { get; set; }

    public string? OpenXrRuntimeReportedName { get; set; }

    public int? OpenXrHmdSystemResult { get; set; }

    public bool? OpenXrHmdSystemAvailable { get; set; }

    public string? OpenXrSystemName { get; set; }

    public uint? OpenXrVendorId { get; set; }

    public uint? OpenXrMaxSwapchainWidth { get; set; }

    public uint? OpenXrMaxSwapchainHeight { get; set; }

    public uint? OpenXrMaxLayerCount { get; set; }

    public bool? OpenXrOrientationTracking { get; set; }

    public bool? OpenXrPositionTracking { get; set; }

    public string? OpenXrRequiredAdapterLuid { get; set; }

    public string? OpenXrMinD3DFeatureLevel { get; set; }

    public uint? OpenXrViewCount { get; set; }

    public uint? OpenXrRecommendedViewWidth { get; set; }

    public uint? OpenXrRecommendedViewHeight { get; set; }

    public uint? OpenXrRecommendedSampleCount { get; set; }

    public int? OpenXrSessionCreateResult { get; set; }

    public bool? OpenXrSessionCreated { get; set; }

    public bool? OpenXrSessionReadyObserved { get; set; }

    public int? OpenXrEmptyFramesSubmitted { get; set; }

    public int? OpenXrTestPatternFramesSubmitted { get; set; }

    public int? OpenXrTestPatternLayerFramesSubmitted { get; set; }

    public uint? OpenXrTestPatternWidth { get; set; }

    public uint? OpenXrTestPatternHeight { get; set; }

    public long? OpenXrTestPatternFormat { get; set; }

    public string? OpenXrTestPatternTextureDescription { get; set; }

    public string? OpenXrTestPatternPixelReadback { get; set; }

    public string? OpenXrFrameLoopStage { get; set; }

    public int? OpenXrFrameLoopResult { get; set; }

    public ulong? OpenXrStereoViewStateFlags { get; set; }

    public float? OpenXrStereoIpdMeters { get; set; }

    public IReadOnlyList<OpenXrStereoViewProbeRecord>? OpenXrStereoViews { get; set; }

    public bool? StereoCloneReady { get; set; }

    public int? StereoRenderWidth { get; set; }

    public int? StereoRenderHeight { get; set; }

    public float? StereoRenderResolutionScale { get; set; }

    public string? StereoLeftTextureDescription { get; set; }

    public string? StereoRightTextureDescription { get; set; }

    public bool? StereoRenderSubmitted { get; set; }

    public bool? StereoDiagnosticSaved { get; set; }

    public int? StereoFrameCount { get; set; }

    public float? StereoPublishFramesPerSecond { get; set; }

    public long? StereoPublishIntervalMilliseconds { get; set; }

    public long? StereoPublishPresentDelta { get; set; }

    public StereoPerformanceProbeRecord? StereoPerformance { get; set; }

    public bool? StereoSourceRenderShadows { get; set; }

    public bool? StereoSourcePostProcessing { get; set; }

    public bool? StereoClonePostProcessing { get; set; }

    public string? StereoVisualEffectMode { get; set; }

    public bool? StereoVisualEffectOverrideConfigured { get; set; }

    public bool? StereoVisualEffectOverrideApplied { get; set; }

    public bool? StereoVisualEffectFallback { get; set; }

    public bool? StereoSourceRequiresDepthTexture { get; set; }

    public bool? StereoCloneRequiresDepthTexture { get; set; }

    public int? StereoSourceRequiresDepthOption { get; set; }

    public int? StereoCloneRequiresDepthOption { get; set; }

    public int? StereoRendererIndex { get; set; }

    public int? StereoSourceRenderType { get; set; }

    public int? StereoCloneRenderType { get; set; }

    public int? StereoSourceAntialiasing { get; set; }

    public int? StereoCloneAntialiasing { get; set; }

    public bool? D3D11HookInstalled { get; set; }

    public bool? D3D11PresentDeviceCaptured { get; set; }

    public bool? D3D11DeviceCaptured { get; set; }

    public bool? D3D11ContextCaptured { get; set; }

    public bool? D3D11SwapChainCaptured { get; set; }

    public bool? D3D12HookInstalled { get; set; }

    public bool? D3D12DeviceCaptured { get; set; }

    public bool? D3D12CommandQueueCaptured { get; set; }

    public bool? D3D12SwapChainCaptured { get; set; }

    public int? D3D12CreateFactoryHookCallCount { get; set; }

    public bool? D3D12SwapChainVtableHooksInstalled { get; set; }

    public int? D3D12FactorySwapChainCallCount { get; set; }

    public int? D3D12CapturePresentDeviceQueryFailureCount { get; set; }

    public int? D3D12SwapChainOutputCount { get; set; }

    public long? D3D12PresentationGeneration { get; set; }

    public bool? D3D12PresentHookInstalled { get; set; }

    public int? D3D12PresentHookCount { get; set; }

    public int? D3D12Present1HookCount { get; set; }

    public string? D3D12LastFactoryInterfaceId { get; set; }

    public uint? D3D12SwapChainWidth { get; set; }

    public uint? D3D12SwapChainHeight { get; set; }

    public ulong? D3D12BackBufferWidth { get; set; }

    public ulong? D3D12BackBufferHeight { get; set; }

    public int? D3D12BackBufferFormat { get; set; }

    public uint? D3D12BackBufferSamples { get; set; }

    public string? D3D12BackBufferDescription { get; set; }
}

internal sealed class OpenXrStereoViewProbeRecord
{
    public int EyeIndex { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float OrientationX { get; set; }
    public float OrientationY { get; set; }
    public float OrientationZ { get; set; }
    public float OrientationW { get; set; }
    public float FovLeftDegrees { get; set; }
    public float FovRightDegrees { get; set; }
    public float FovUpDegrees { get; set; }
    public float FovDownDegrees { get; set; }
}

internal sealed class CameraProbeRecord
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool ActiveInHierarchy { get; set; }
    public float Depth { get; set; }
    public int CullingMask { get; set; }
    public int ClearFlags { get; set; }
    public int CameraType { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public float FieldOfView { get; set; }
    public float NearClipPlane { get; set; }
    public float FarClipPlane { get; set; }
    public bool TargetTexturePresent { get; set; }
    public string? TargetTextureName { get; set; }
    public int? TargetTextureWidth { get; set; }
    public int? TargetTextureHeight { get; set; }
    public int? TargetTextureGraphicsFormat { get; set; }
    public int? TargetTextureDimension { get; set; }
    public int? TargetTextureAntiAliasing { get; set; }
    public bool HasUniversalAdditionalCameraData { get; set; }
    public int? UrpRendererIndex { get; set; }
    public int? UrpRenderType { get; set; }
    public bool? UrpRenderPostProcessing { get; set; }
    public bool? UrpRequiresDepthTexture { get; set; }
    public bool? UrpRequiresColorTexture { get; set; }
    public int? UrpCameraStackCount { get; set; }
    public IReadOnlyList<string>? UrpCameraStackNames { get; set; }
}

internal sealed class CanvasProbeRecord
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool ActiveInHierarchy { get; set; }
    public int RenderMode { get; set; }
    public int SortingOrder { get; set; }
    public bool OverrideSorting { get; set; }
    public string? WorldCameraName { get; set; }
}

internal sealed class RawImageProbeRecord
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool ActiveInHierarchy { get; set; }
    public bool RaycastTarget { get; set; }
    public string? TextureName { get; set; }
    public int? TextureWidth { get; set; }
    public int? TextureHeight { get; set; }
}

internal sealed class VideoPlayerProbeRecord
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool ActiveInHierarchy { get; set; }
    public bool? IsPlaying { get; set; }
    public bool? IsPrepared { get; set; }
    public int? Source { get; set; }
    public string? ClipName { get; set; }
    public int? RenderMode { get; set; }
    public int? AspectRatio { get; set; }
    public string? TargetCameraName { get; set; }
    public string? TargetTextureName { get; set; }
    public int? TargetTextureWidth { get; set; }
    public int? TargetTextureHeight { get; set; }
    public string? OutputTextureName { get; set; }
    public int? OutputTextureWidth { get; set; }
    public int? OutputTextureHeight { get; set; }
}

internal sealed class MediaSurfaceProbeRecord
{
    public string AssemblyName { get; set; } = string.Empty;
    public string TypeNamespace { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool? Enabled { get; set; }
    public bool ActiveInHierarchy { get; set; }
    public bool? IsPlaying { get; set; }
    public bool? IsPrepared { get; set; }
    public string? TextureName { get; set; }
    public int? TextureWidth { get; set; }
    public int? TextureHeight { get; set; }
}

internal sealed class UiGraphicProbeRecord
{
    public string TypeNamespace { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public bool ActiveInHierarchy { get; set; }
    public bool RaycastTarget { get; set; }
    public bool? Culled { get; set; }
    public int? AbsoluteDepth { get; set; }
    public int? RelativeDepth { get; set; }
    public int? MaterialCount { get; set; }
    public string? MaterialName { get; set; }
    public string? TextureName { get; set; }
    public int? TextureWidth { get; set; }
    public int? TextureHeight { get; set; }
}

internal sealed class Il2CppTypeProbeRecord
{
    public string AssemblyName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string? ParentClassName { get; set; }
    public IReadOnlyList<Il2CppMethodProbeRecord> Methods { get; set; } =
        Array.Empty<Il2CppMethodProbeRecord>();
    public IReadOnlyList<Il2CppFieldProbeRecord> Fields { get; set; } =
        Array.Empty<Il2CppFieldProbeRecord>();
}

internal sealed class Il2CppMethodProbeRecord
{
    public string Name { get; set; } = string.Empty;
    public uint ParameterCount { get; set; }
    public string ReturnType { get; set; } = string.Empty;
    public IReadOnlyList<string> ParameterTypes { get; set; } = Array.Empty<string>();
    public uint Flags { get; set; }
    public bool IsStatic { get; set; }
    public string NativeMethodPointer { get; set; } = string.Empty;
    public string VirtualMethodPointer { get; set; } = string.Empty;
    public string InvokerPointer { get; set; } = string.Empty;
    public string NativeMethodModule { get; set; } = string.Empty;
    public string VirtualMethodModule { get; set; } = string.Empty;
    public string InvokerModule { get; set; } = string.Empty;
}

internal sealed class Il2CppFieldProbeRecord
{
    public string Name { get; set; } = string.Empty;
    public int Offset { get; set; }
}

internal sealed class Il2CppApi
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr DomainGetDelegate();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ThreadAttachDelegate(IntPtr domain);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr DomainGetAssembliesDelegate(IntPtr domain, out UIntPtr size);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr AssemblyGetImageDelegate(IntPtr assembly);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ImageGetNameDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate UIntPtr ImageGetClassCountDelegate(IntPtr image);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ImageGetClassDelegate(IntPtr image, UIntPtr index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ClassFromNameDelegate(IntPtr image, IntPtr namespaze, IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ClassGetMethodFromNameDelegate(IntPtr klass, IntPtr name, int argumentCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ClassGetMethodsDelegate(IntPtr klass, ref IntPtr iterator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ClassGetNameDelegate(IntPtr klass);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ClassGetNamespaceDelegate(IntPtr klass);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ClassGetParentDelegate(IntPtr klass);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ClassGetFieldsDelegate(IntPtr klass, ref IntPtr iterator);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr FieldGetNameDelegate(IntPtr field);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int FieldGetOffsetDelegate(IntPtr field);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FieldSetValueDelegate(IntPtr instance, IntPtr field, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FieldGetValueDelegate(IntPtr instance, IntPtr field, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void GcWriteBarrierSetFieldDelegate(
        IntPtr instance,
        IntPtr targetAddress,
        IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr MethodGetNameDelegate(IntPtr method);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint MethodGetParamCountDelegate(IntPtr method);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr MethodGetParamDelegate(IntPtr method, uint index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr MethodGetReturnTypeDelegate(IntPtr method);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint MethodGetFlagsDelegate(IntPtr method, out uint implementationFlags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr TypeGetNameDelegate(IntPtr type);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FreeDelegate(IntPtr memory);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FormatExceptionDelegate(IntPtr exception, IntPtr message, int messageSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void FormatStackTraceDelegate(IntPtr exception, IntPtr message, int messageSize);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ObjectNewDelegate(IntPtr klass);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ValueBoxDelegate(IntPtr klass, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate uint GcHandleNewDelegate(
        IntPtr instance,
        [MarshalAs(UnmanagedType.I1)] bool pinned);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void GcHandleFreeDelegate(uint handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ClassGetTypeDelegate(IntPtr klass);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr TypeGetObjectDelegate(IntPtr type);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr RuntimeInvokeDelegate(
        IntPtr method,
        IntPtr instance,
        IntPtr arguments,
        out IntPtr exception);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr RuntimeInvokeConvertArgsDelegate(
        IntPtr method,
        IntPtr instance,
        IntPtr arguments,
        int argumentCount,
        out IntPtr exception);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ObjectUnboxDelegate(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int StringLengthDelegate(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr StringCharsDelegate(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr StringNewDelegate(IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr ResolveIcallDelegate(IntPtr name);

    private Il2CppApi(
        DomainGetDelegate domainGet,
        ThreadAttachDelegate threadAttach,
        DomainGetAssembliesDelegate domainGetAssemblies,
        AssemblyGetImageDelegate assemblyGetImage,
        ImageGetNameDelegate imageGetName,
        ImageGetClassCountDelegate imageGetClassCount,
        ImageGetClassDelegate imageGetClass,
        ClassFromNameDelegate classFromName,
        ClassGetMethodFromNameDelegate classGetMethodFromName,
        ClassGetMethodsDelegate classGetMethods,
        ClassGetNameDelegate classGetName,
        ClassGetNamespaceDelegate classGetNamespace,
        ClassGetParentDelegate classGetParent,
        ClassGetFieldsDelegate classGetFields,
        FieldGetNameDelegate fieldGetName,
        FieldGetOffsetDelegate fieldGetOffset,
        FieldSetValueDelegate fieldSetValue,
        FieldGetValueDelegate fieldGetValue,
        GcWriteBarrierSetFieldDelegate gcWriteBarrierSetField,
        MethodGetNameDelegate methodGetName,
        MethodGetParamCountDelegate methodGetParamCount,
        MethodGetParamDelegate methodGetParam,
        MethodGetReturnTypeDelegate methodGetReturnType,
        MethodGetFlagsDelegate methodGetFlags,
        TypeGetNameDelegate typeGetName,
        FreeDelegate free,
        FormatExceptionDelegate formatException,
        FormatStackTraceDelegate formatStackTrace,
        ObjectNewDelegate objectNew,
        ValueBoxDelegate valueBox,
        GcHandleNewDelegate gcHandleNew,
        GcHandleFreeDelegate gcHandleFree,
        ClassGetTypeDelegate? classGetType,
        TypeGetObjectDelegate? typeGetObject,
        RuntimeInvokeDelegate runtimeInvoke,
        RuntimeInvokeConvertArgsDelegate? runtimeInvokeConvertArgs,
        ObjectUnboxDelegate objectUnbox,
        StringLengthDelegate stringLength,
        StringCharsDelegate stringChars,
        StringNewDelegate stringNew,
        ResolveIcallDelegate resolveIcall)
    {
        DomainGet = domainGet;
        ThreadAttach = threadAttach;
        DomainGetAssemblies = domainGetAssemblies;
        AssemblyGetImage = assemblyGetImage;
        ImageGetName = imageGetName;
        ImageGetClassCount = imageGetClassCount;
        ImageGetClass = imageGetClass;
        ClassFromName = classFromName;
        ClassGetMethodFromName = classGetMethodFromName;
        ClassGetMethods = classGetMethods;
        ClassGetName = classGetName;
        ClassGetNamespace = classGetNamespace;
        ClassGetParent = classGetParent;
        ClassGetFields = classGetFields;
        FieldGetName = fieldGetName;
        FieldGetOffset = fieldGetOffset;
        FieldSetValue = fieldSetValue;
        FieldGetValue = fieldGetValue;
        GcWriteBarrierSetField = gcWriteBarrierSetField;
        MethodGetName = methodGetName;
        MethodGetParamCount = methodGetParamCount;
        MethodGetParam = methodGetParam;
        MethodGetReturnType = methodGetReturnType;
        MethodGetFlags = methodGetFlags;
        TypeGetName = typeGetName;
        Free = free;
        FormatException = formatException;
        FormatStackTrace = formatStackTrace;
        ObjectNew = objectNew;
        ValueBox = valueBox;
        GcHandleNew = gcHandleNew;
        GcHandleFree = gcHandleFree;
        ClassGetType = classGetType;
        TypeGetObject = typeGetObject;
        RuntimeInvoke = runtimeInvoke;
        RuntimeInvokeConvertArgs = runtimeInvokeConvertArgs;
        ObjectUnbox = objectUnbox;
        StringLength = stringLength;
        StringChars = stringChars;
        StringNew = stringNew;
        ResolveIcall = resolveIcall;
    }

    public DomainGetDelegate DomainGet { get; }

    public ThreadAttachDelegate ThreadAttach { get; }

    public DomainGetAssembliesDelegate DomainGetAssemblies { get; }

    public AssemblyGetImageDelegate AssemblyGetImage { get; }

    public ImageGetNameDelegate ImageGetName { get; }

    public ImageGetClassCountDelegate ImageGetClassCount { get; }

    public ImageGetClassDelegate ImageGetClass { get; }

    public ClassFromNameDelegate ClassFromName { get; }

    public ClassGetMethodFromNameDelegate ClassGetMethodFromName { get; }

    public ClassGetMethodsDelegate ClassGetMethods { get; }

    public ClassGetNameDelegate ClassGetName { get; }

    public ClassGetNamespaceDelegate ClassGetNamespace { get; }

    public ClassGetParentDelegate ClassGetParent { get; }

    public ClassGetFieldsDelegate ClassGetFields { get; }

    public FieldGetNameDelegate FieldGetName { get; }

    public FieldGetOffsetDelegate FieldGetOffset { get; }

    public FieldSetValueDelegate FieldSetValue { get; }

    public FieldGetValueDelegate FieldGetValue { get; }

    public GcWriteBarrierSetFieldDelegate GcWriteBarrierSetField { get; }

    public MethodGetNameDelegate MethodGetName { get; }

    public MethodGetParamCountDelegate MethodGetParamCount { get; }

    public MethodGetParamDelegate MethodGetParam { get; }

    public MethodGetReturnTypeDelegate MethodGetReturnType { get; }

    public MethodGetFlagsDelegate MethodGetFlags { get; }

    public TypeGetNameDelegate TypeGetName { get; }

    public FreeDelegate Free { get; }

    public FormatExceptionDelegate FormatException { get; }

    public FormatStackTraceDelegate FormatStackTrace { get; }

    public ObjectNewDelegate ObjectNew { get; }

    public ValueBoxDelegate ValueBox { get; }

    public GcHandleNewDelegate GcHandleNew { get; }

    public GcHandleFreeDelegate GcHandleFree { get; }

    public ClassGetTypeDelegate? ClassGetType { get; }

    public TypeGetObjectDelegate? TypeGetObject { get; }

    public RuntimeInvokeDelegate RuntimeInvoke { get; }

    public RuntimeInvokeConvertArgsDelegate? RuntimeInvokeConvertArgs { get; }

    public ObjectUnboxDelegate ObjectUnbox { get; }

    public StringLengthDelegate StringLength { get; }

    public StringCharsDelegate StringChars { get; }

    public StringNewDelegate StringNew { get; }

    public ResolveIcallDelegate ResolveIcall { get; }

    public static Il2CppApi Load(IntPtr module) => new(
        LoadExport<DomainGetDelegate>(module, "il2cpp_domain_get"),
        LoadExport<ThreadAttachDelegate>(module, "il2cpp_thread_attach"),
        LoadExport<DomainGetAssembliesDelegate>(module, "il2cpp_domain_get_assemblies"),
        LoadExport<AssemblyGetImageDelegate>(module, "il2cpp_assembly_get_image"),
        LoadExport<ImageGetNameDelegate>(module, "il2cpp_image_get_name"),
        LoadExport<ImageGetClassCountDelegate>(module, "il2cpp_image_get_class_count"),
        LoadExport<ImageGetClassDelegate>(module, "il2cpp_image_get_class"),
        LoadExport<ClassFromNameDelegate>(module, "il2cpp_class_from_name"),
        LoadExport<ClassGetMethodFromNameDelegate>(module, "il2cpp_class_get_method_from_name"),
        LoadExport<ClassGetMethodsDelegate>(module, "il2cpp_class_get_methods"),
        LoadExport<ClassGetNameDelegate>(module, "il2cpp_class_get_name"),
        LoadExport<ClassGetNamespaceDelegate>(module, "il2cpp_class_get_namespace"),
        LoadExport<ClassGetParentDelegate>(module, "il2cpp_class_get_parent"),
        LoadExport<ClassGetFieldsDelegate>(module, "il2cpp_class_get_fields"),
        LoadExport<FieldGetNameDelegate>(module, "il2cpp_field_get_name"),
        LoadExport<FieldGetOffsetDelegate>(module, "il2cpp_field_get_offset"),
        LoadExport<FieldSetValueDelegate>(module, "il2cpp_field_set_value"),
        LoadExport<FieldGetValueDelegate>(module, "il2cpp_field_get_value"),
        LoadExport<GcWriteBarrierSetFieldDelegate>(module, "il2cpp_gc_wbarrier_set_field"),
        LoadExport<MethodGetNameDelegate>(module, "il2cpp_method_get_name"),
        LoadExport<MethodGetParamCountDelegate>(module, "il2cpp_method_get_param_count"),
        LoadExport<MethodGetParamDelegate>(module, "il2cpp_method_get_param"),
        LoadExport<MethodGetReturnTypeDelegate>(module, "il2cpp_method_get_return_type"),
        LoadExport<MethodGetFlagsDelegate>(module, "il2cpp_method_get_flags"),
        LoadExport<TypeGetNameDelegate>(module, "il2cpp_type_get_name"),
        LoadExport<FreeDelegate>(module, "il2cpp_free"),
        LoadExport<FormatExceptionDelegate>(module, "il2cpp_format_exception"),
        LoadExport<FormatStackTraceDelegate>(module, "il2cpp_format_stack_trace"),
        LoadExport<ObjectNewDelegate>(module, "il2cpp_object_new"),
        LoadExport<ValueBoxDelegate>(module, "il2cpp_value_box"),
        LoadExport<GcHandleNewDelegate>(module, "il2cpp_gchandle_new"),
        LoadExport<GcHandleFreeDelegate>(module, "il2cpp_gchandle_free"),
        TryLoadExport<ClassGetTypeDelegate>(module, "il2cpp_class_get_type"),
        TryLoadExport<TypeGetObjectDelegate>(module, "il2cpp_type_get_object"),
        LoadExport<RuntimeInvokeDelegate>(module, "il2cpp_runtime_invoke"),
        TryLoadExport<RuntimeInvokeConvertArgsDelegate>(module, "il2cpp_runtime_invoke_convert_args"),
        LoadExport<ObjectUnboxDelegate>(module, "il2cpp_object_unbox"),
        LoadExport<StringLengthDelegate>(module, "il2cpp_string_length"),
        LoadExport<StringCharsDelegate>(module, "il2cpp_string_chars"),
        LoadExport<StringNewDelegate>(module, "il2cpp_string_new"),
        LoadExport<ResolveIcallDelegate>(module, "il2cpp_resolve_icall"));

    private static TDelegate LoadExport<TDelegate>(IntPtr module, string name)
        where TDelegate : Delegate
    {
        IntPtr address = NativeMethods.GetProcAddress(module, name);
        if (address == IntPtr.Zero)
        {
            throw new MissingMethodException($"GameAssembly.dll does not export {name}.");
        }

        return Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
    }

    private static TDelegate? TryLoadExport<TDelegate>(IntPtr module, string name)
        where TDelegate : Delegate
    {
        IntPtr address = NativeMethods.GetProcAddress(module, name);
        return address == IntPtr.Zero
            ? null
            : Marshal.GetDelegateForFunctionPointer<TDelegate>(address);
    }
}

internal static class NativeMethods
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    internal static extern IntPtr GetProcAddress(IntPtr module, string procedureName);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool OpenProcessToken(
        IntPtr processHandle,
        uint desiredAccess,
        out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    internal static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        out int tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern bool CloseHandle(IntPtr handle);
}
