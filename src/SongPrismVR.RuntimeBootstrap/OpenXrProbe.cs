using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using SongPrismVR.Core;
using Microsoft.Win32;

namespace Doorstop;

internal sealed class OpenXrProbeResult
{
    public string ActiveRuntimeManifest { get; init; } = string.Empty;

    public string ActiveRuntimeName { get; init; } = string.Empty;

    public string LoaderPath { get; init; } = string.Empty;

    public string LoaderVersion { get; init; } = string.Empty;

    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();

    public bool SupportsD3D11 { get; init; }

    public bool InstanceCreated { get; init; }

    public string RuntimeVersion { get; init; } = string.Empty;

    public string RuntimeReportedName { get; init; } = string.Empty;

    public int HmdSystemResult { get; init; }

    public bool HmdSystemAvailable { get; init; }

    public string SystemName { get; init; } = string.Empty;

    public uint VendorId { get; init; }

    public uint MaxSwapchainWidth { get; init; }

    public uint MaxSwapchainHeight { get; init; }

    public uint MaxLayerCount { get; init; }

    public bool OrientationTracking { get; init; }

    public bool PositionTracking { get; init; }

    public string RequiredAdapterLuid { get; init; } = string.Empty;

    public string MinD3DFeatureLevel { get; init; } = string.Empty;

    public uint ViewCount { get; init; }

    public uint RecommendedViewWidth { get; init; }

    public uint RecommendedViewHeight { get; init; }

    public uint RecommendedSampleCount { get; init; }

    public int SessionCreateResult { get; init; }

    public bool SessionCreated { get; init; }

    public bool SessionReadyObserved { get; init; }

    public int EmptyFramesSubmitted { get; init; }

    public int TestPatternFramesSubmitted { get; init; }

    public int TestPatternLayerFramesSubmitted { get; init; }

    public uint TestPatternWidth { get; init; }

    public uint TestPatternHeight { get; init; }

    public long TestPatternFormat { get; init; }

    public string TestPatternTextureDescription { get; init; } = string.Empty;

    public string TestPatternPixelReadback { get; init; } = string.Empty;

    public string FrameLoopStage { get; init; } = string.Empty;

    public int FrameLoopResult { get; init; }
}

[SupportedOSPlatform("windows")]
internal static class OpenXrProbe
{
    // The game's already-composited swap-chain frame is the reliable UI fallback.
    // It includes Localify text and exactly follows the game's UI visibility state.
    private const bool PreferCompositedGameBackBuffer = true;
    private const int XrSuccess = 0;
    private const int XrTypeExtensionProperties = 2;
    private const int XrTypeInstanceCreateInfo = 3;
    private const int XrTypeSystemGetInfo = 4;
    private const int XrTypeSystemProperties = 5;
    private const int XrTypeInstanceProperties = 32;
    private const int XrFormFactorHeadMountedDisplay = 1;
    private const int MaxExtensionNameSize = 128;
    private const int MaxApplicationNameSize = 128;
    private const int MaxEngineNameSize = 128;
    private const int MaxRuntimeNameSize = 128;
    private const int MaxSystemNameSize = 256;
    private static readonly byte[] CursorPixels = CreateCursorPixels();

    public static OpenXrProbeResult Collect()
    {
        string manifestPath = ReadActiveRuntimeManifest();
        string runtimeName = ReadRuntimeName(manifestPath);
        string loaderPath = FindLoader();
        IntPtr loader = NativeLibrary.Load(loaderPath);
        try
        {
            EnumerateInstanceExtensionPropertiesDelegate enumerate = LoadExport<EnumerateInstanceExtensionPropertiesDelegate>(
                loader,
                "xrEnumerateInstanceExtensionProperties");
            IReadOnlyList<string> extensions = EnumerateExtensions(enumerate);
            bool supportsD3D11 = extensions.Contains("XR_KHR_D3D11_enable", StringComparer.Ordinal);
            if (!supportsD3D11)
            {
                throw new InvalidOperationException("The active runtime does not advertise XR_KHR_D3D11_enable.");
            }

            OpenXrInstanceResult instance = ProbeInstance(loader);
            return new OpenXrProbeResult
            {
                ActiveRuntimeManifest = manifestPath,
                ActiveRuntimeName = runtimeName,
                LoaderPath = loaderPath,
                LoaderVersion = FileVersionInfo.GetVersionInfo(loaderPath).FileVersion ?? string.Empty,
                Extensions = extensions,
                SupportsD3D11 = true,
                InstanceCreated = true,
                RuntimeVersion = instance.RuntimeVersion,
                RuntimeReportedName = instance.RuntimeName,
                HmdSystemResult = instance.HmdSystemResult,
                HmdSystemAvailable = instance.HmdSystemAvailable,
                SystemName = instance.SystemName,
                VendorId = instance.VendorId,
                MaxSwapchainWidth = instance.MaxSwapchainWidth,
                MaxSwapchainHeight = instance.MaxSwapchainHeight,
                MaxLayerCount = instance.MaxLayerCount,
                OrientationTracking = instance.OrientationTracking,
                PositionTracking = instance.PositionTracking,
                RequiredAdapterLuid = instance.RequiredAdapterLuid,
                MinD3DFeatureLevel = instance.MinD3DFeatureLevel,
                ViewCount = instance.ViewCount,
                RecommendedViewWidth = instance.RecommendedViewWidth,
                RecommendedViewHeight = instance.RecommendedViewHeight,
                RecommendedSampleCount = instance.RecommendedSampleCount,
                SessionCreateResult = instance.SessionCreateResult,
                SessionCreated = instance.SessionCreated,
                SessionReadyObserved = instance.SessionReadyObserved,
                EmptyFramesSubmitted = instance.EmptyFramesSubmitted,
                TestPatternFramesSubmitted = instance.TestPatternFramesSubmitted,
                TestPatternLayerFramesSubmitted = instance.TestPatternLayerFramesSubmitted,
                TestPatternWidth = instance.TestPatternWidth,
                TestPatternHeight = instance.TestPatternHeight,
                TestPatternFormat = instance.TestPatternFormat,
                TestPatternTextureDescription = instance.TestPatternTextureDescription,
                TestPatternPixelReadback = instance.TestPatternPixelReadback,
                FrameLoopStage = instance.FrameLoopStage,
                FrameLoopResult = instance.FrameLoopResult
            };
        }
        finally
        {
            NativeLibrary.Free(loader);
        }
    }

    private static OpenXrInstanceResult ProbeInstance(IntPtr loader)
    {
        CreateInstanceDelegate createInstance = LoadExport<CreateInstanceDelegate>(loader, "xrCreateInstance");
        DestroyInstanceDelegate destroyInstance = LoadExport<DestroyInstanceDelegate>(loader, "xrDestroyInstance");
        GetInstancePropertiesDelegate getInstanceProperties = LoadExport<GetInstancePropertiesDelegate>(
            loader,
            "xrGetInstanceProperties");
        GetSystemDelegate getSystem = LoadExport<GetSystemDelegate>(loader, "xrGetSystem");
        GetSystemPropertiesDelegate getSystemProperties = LoadExport<GetSystemPropertiesDelegate>(
            loader,
            "xrGetSystemProperties");

        string[] enabledExtensionValues = { "XR_KHR_D3D11_enable" };
        IntPtr[] extensionNamePointers = new IntPtr[enabledExtensionValues.Length];
        IntPtr extensionNames = Marshal.AllocHGlobal(IntPtr.Size * enabledExtensionValues.Length);
        for (int index = 0; index < enabledExtensionValues.Length; index++)
        {
            extensionNamePointers[index] = Marshal.StringToCoTaskMemUTF8(enabledExtensionValues[index]);
            Marshal.WriteIntPtr(extensionNames, index * IntPtr.Size, extensionNamePointers[index]);
        }
        IntPtr instance = IntPtr.Zero;
        try
        {
            XrInstanceCreateInfo createInfo = new()
            {
                Type = XrTypeInstanceCreateInfo,
                ApplicationInfo = new XrApplicationInfo
                {
                    ApplicationName = FixedUtf8("SongPrismVR", MaxApplicationNameSize),
                    ApplicationVersion = 22,
                    EngineName = FixedUtf8("Unity", MaxEngineNameSize),
                    EngineVersion = 60000077,
                    ApiVersion = MakeVersion(1, 0, 0)
                },
                EnabledExtensionCount = checked((uint)enabledExtensionValues.Length),
                EnabledExtensionNames = extensionNames
            };
            Check(createInstance(ref createInfo, out instance), "create OpenXR instance");
            if (instance == IntPtr.Zero)
            {
                throw new InvalidOperationException("xrCreateInstance returned a null instance.");
            }

            XrInstanceProperties properties = new()
            {
                Type = XrTypeInstanceProperties,
                RuntimeName = new byte[MaxRuntimeNameSize]
            };
            Check(getInstanceProperties(instance, ref properties), "query OpenXR instance properties");

            XrSystemGetInfo systemInfo = new()
            {
                Type = XrTypeSystemGetInfo,
                FormFactor = XrFormFactorHeadMountedDisplay
            };
            int systemResult = getSystem(instance, ref systemInfo, out ulong systemId);
            if (systemResult != XrSuccess)
            {
                return new OpenXrInstanceResult
                {
                    RuntimeName = DecodeFixedUtf8(properties.RuntimeName),
                    RuntimeVersion = FormatVersion(properties.RuntimeVersion),
                    HmdSystemResult = systemResult
                };
            }

            XrSystemProperties systemProperties = new()
            {
                Type = XrTypeSystemProperties,
                SystemName = new byte[MaxSystemNameSize]
            };
            Check(
                getSystemProperties(instance, systemId, ref systemProperties),
                "query OpenXR HMD system properties");
            ViewConfigurationResult viewConfiguration = QueryViewConfiguration(
                loader,
                instance,
                systemId);
            OpenXrStereoStateRegistry.UpdateConfiguration(
                viewConfiguration.RecommendedWidth,
                viewConfiguration.RecommendedHeight);
            GetInstanceProcAddrDelegate getInstanceProcAddr = LoadExport<GetInstanceProcAddrDelegate>(
                loader,
                "xrGetInstanceProcAddr");
            IntPtr functionName = Marshal.StringToCoTaskMemUTF8("xrGetD3D11GraphicsRequirementsKHR");
            XrGraphicsRequirementsD3D11 requirements;
            try
            {
                Check(
                    getInstanceProcAddr(instance, functionName, out IntPtr function),
                    "resolve xrGetD3D11GraphicsRequirementsKHR");
                if (function == IntPtr.Zero)
                {
                    throw new MissingMethodException("xrGetD3D11GraphicsRequirementsKHR resolved to null.");
                }

                GetD3D11GraphicsRequirementsDelegate getRequirements =
                    Marshal.GetDelegateForFunctionPointer<GetD3D11GraphicsRequirementsDelegate>(function);
                requirements = new XrGraphicsRequirementsD3D11
                {
                    Type = 1000027002
                };
                Check(
                    getRequirements(instance, systemId, ref requirements),
                    "query OpenXR D3D11 graphics requirements");
            }
            finally
            {
                Marshal.FreeCoTaskMem(functionName);
            }

            SessionProbeResult sessionProbe = CreateAndProbeSession(
                loader,
                instance,
                systemId);
            return new OpenXrInstanceResult
            {
                RuntimeName = DecodeFixedUtf8(properties.RuntimeName),
                RuntimeVersion = FormatVersion(properties.RuntimeVersion),
                HmdSystemResult = systemResult,
                HmdSystemAvailable = true,
                SystemName = DecodeFixedUtf8(systemProperties.SystemName),
                VendorId = systemProperties.VendorId,
                MaxSwapchainWidth = systemProperties.GraphicsProperties.MaxSwapchainImageWidth,
                MaxSwapchainHeight = systemProperties.GraphicsProperties.MaxSwapchainImageHeight,
                MaxLayerCount = systemProperties.GraphicsProperties.MaxLayerCount,
                OrientationTracking = systemProperties.TrackingProperties.OrientationTracking != 0,
                PositionTracking = systemProperties.TrackingProperties.PositionTracking != 0,
                RequiredAdapterLuid = $"{requirements.AdapterLuid.HighPart:x8}{requirements.AdapterLuid.LowPart:x8}",
                MinD3DFeatureLevel = $"0x{requirements.MinFeatureLevel:x}",
                ViewCount = viewConfiguration.ViewCount,
                RecommendedViewWidth = viewConfiguration.RecommendedWidth,
                RecommendedViewHeight = viewConfiguration.RecommendedHeight,
                RecommendedSampleCount = viewConfiguration.RecommendedSampleCount,
                SessionCreateResult = sessionProbe.CreateResult,
                SessionCreated = sessionProbe.Created,
                SessionReadyObserved = sessionProbe.ReadyObserved,
                EmptyFramesSubmitted = 0,
                TestPatternFramesSubmitted = sessionProbe.FramesSubmitted,
                TestPatternLayerFramesSubmitted = sessionProbe.LayerFramesSubmitted,
                TestPatternWidth = sessionProbe.TestPatternWidth,
                TestPatternHeight = sessionProbe.TestPatternHeight,
                TestPatternFormat = sessionProbe.TestPatternFormat,
                TestPatternTextureDescription = sessionProbe.TestPatternTextureDescription,
                TestPatternPixelReadback = sessionProbe.TestPatternPixelReadback,
                FrameLoopStage = sessionProbe.FrameLoopStage,
                FrameLoopResult = sessionProbe.FrameLoopResult
            };
        }
        finally
        {
            if (instance != IntPtr.Zero)
            {
                _ = destroyInstance(instance);
            }

            Marshal.FreeHGlobal(extensionNames);
            foreach (IntPtr extensionNamePointer in extensionNamePointers)
            {
                if (extensionNamePointer != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(extensionNamePointer);
                }
            }
        }
    }

    private static ViewConfigurationResult QueryViewConfiguration(
        IntPtr loader,
        IntPtr instance,
        ulong systemId)
    {
        const int primaryStereo = 2;
        const int viewConfigurationViewType = 41;
        EnumerateViewConfigurationViewsDelegate enumerate =
            LoadExport<EnumerateViewConfigurationViewsDelegate>(
                loader,
                "xrEnumerateViewConfigurationViews");
        Check(
            enumerate(instance, systemId, primaryStereo, 0, out uint count, IntPtr.Zero),
            "count OpenXR stereo views");
        if (count == 0 || count > 16)
        {
            throw new InvalidOperationException($"Invalid OpenXR stereo view count: {count}.");
        }

        int elementSize = Marshal.SizeOf<XrViewConfigurationView>();
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)count * elementSize));
        try
        {
            for (uint index = 0; index < count; index++)
            {
                Marshal.StructureToPtr(
                    new XrViewConfigurationView { Type = viewConfigurationViewType },
                    IntPtr.Add(buffer, checked((int)index * elementSize)),
                    fDeleteOld: false);
            }

            Check(
                enumerate(instance, systemId, primaryStereo, count, out uint written, buffer),
                "enumerate OpenXR stereo views");
            XrViewConfigurationView first = Marshal.PtrToStructure<XrViewConfigurationView>(buffer);
            return new ViewConfigurationResult
            {
                ViewCount = written,
                RecommendedWidth = first.RecommendedImageRectWidth,
                RecommendedHeight = first.RecommendedImageRectHeight,
                RecommendedSampleCount = first.RecommendedSwapchainSampleCount
            };
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SessionProbeResult CreateAndProbeSession(
        IntPtr loader,
        IntPtr instance,
        ulong systemId)
    {
        IntPtr device = D3D11DeviceCapture.PresentDevice;
        if (device == IntPtr.Zero)
        {
            return new SessionProbeResult { CreateResult = int.MinValue };
        }

        CreateSessionDelegate createSession = LoadExport<CreateSessionDelegate>(loader, "xrCreateSession");
        DestroySessionDelegate destroySession = LoadExport<DestroySessionDelegate>(loader, "xrDestroySession");
        XrGraphicsBindingD3D11 binding = new()
        {
            Type = 1000027000,
            Device = device
        };
        IntPtr bindingPointer = Marshal.AllocHGlobal(Marshal.SizeOf<XrGraphicsBindingD3D11>());
        IntPtr session = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(binding, bindingPointer, fDeleteOld: false);
            XrSessionCreateInfo createInfo = new()
            {
                Type = 8,
                Next = bindingPointer,
                SystemId = systemId
            };
            int result = createSession(instance, ref createInfo, out session);
            bool created = result == XrSuccess && session != IntPtr.Zero;
            if (!created)
            {
                return new SessionProbeResult { CreateResult = result };
            }

            SessionProbeResult frameLoop = RunTestPatternFrameLoop(
                loader,
                instance,
                session,
                device);
            return new SessionProbeResult
            {
                CreateResult = result,
                Created = true,
                ReadyObserved = frameLoop.ReadyObserved,
                FramesSubmitted = frameLoop.FramesSubmitted,
                LayerFramesSubmitted = frameLoop.LayerFramesSubmitted,
                TestPatternWidth = frameLoop.TestPatternWidth,
                TestPatternHeight = frameLoop.TestPatternHeight,
                TestPatternFormat = frameLoop.TestPatternFormat,
                TestPatternTextureDescription = frameLoop.TestPatternTextureDescription,
                TestPatternPixelReadback = frameLoop.TestPatternPixelReadback,
                FrameLoopStage = frameLoop.FrameLoopStage,
                FrameLoopResult = frameLoop.FrameLoopResult
            };
        }
        finally
        {
            if (session != IntPtr.Zero)
            {
                _ = destroySession(session);
            }

            Marshal.FreeHGlobal(bindingPointer);
        }
    }

    private static SessionProbeResult RunTestPatternFrameLoop(
        IntPtr loader,
        IntPtr instance,
        IntPtr session,
        IntPtr device)
    {
        const int xrEventUnavailable = 4;
        const int xrSessionStateReady = 2;
        const int xrSessionStateStopping = 6;
        const int xrSessionStateLossPending = 7;
        const int xrSessionStateExiting = 8;
        PollEventDelegate pollEvent = LoadExport<PollEventDelegate>(loader, "xrPollEvent");
        BeginSessionDelegate beginSession = LoadExport<BeginSessionDelegate>(loader, "xrBeginSession");
        EndSessionDelegate endSession = LoadExport<EndSessionDelegate>(loader, "xrEndSession");
        RequestExitSessionDelegate requestExit = LoadExport<RequestExitSessionDelegate>(
            loader,
            "xrRequestExitSession");
        WaitFrameDelegate waitFrame = LoadExport<WaitFrameDelegate>(loader, "xrWaitFrame");
        BeginFrameDelegate beginFrame = LoadExport<BeginFrameDelegate>(loader, "xrBeginFrame");
        EndFrameDelegate endFrame = LoadExport<EndFrameDelegate>(loader, "xrEndFrame");
        LocateViewsDelegate locateViews = LoadExport<LocateViewsDelegate>(loader, "xrLocateViews");
        CreateReferenceSpaceDelegate createReferenceSpace = LoadExport<CreateReferenceSpaceDelegate>(
            loader,
            "xrCreateReferenceSpace");
        DestroySpaceDelegate destroySpace = LoadExport<DestroySpaceDelegate>(loader, "xrDestroySpace");
        EnumerateSwapchainFormatsDelegate enumerateFormats = LoadExport<EnumerateSwapchainFormatsDelegate>(
            loader,
            "xrEnumerateSwapchainFormats");
        CreateSwapchainDelegate createSwapchain = LoadExport<CreateSwapchainDelegate>(loader, "xrCreateSwapchain");
        DestroySwapchainDelegate destroySwapchain = LoadExport<DestroySwapchainDelegate>(loader, "xrDestroySwapchain");
        EnumerateSwapchainImagesDelegate enumerateImages = LoadExport<EnumerateSwapchainImagesDelegate>(
            loader,
            "xrEnumerateSwapchainImages");
        AcquireSwapchainImageDelegate acquireImage = LoadExport<AcquireSwapchainImageDelegate>(
            loader,
            "xrAcquireSwapchainImage");
        WaitSwapchainImageDelegate waitImage = LoadExport<WaitSwapchainImageDelegate>(
            loader,
            "xrWaitSwapchainImage");
        ReleaseSwapchainImageDelegate releaseImage = LoadExport<ReleaseSwapchainImageDelegate>(
            loader,
            "xrReleaseSwapchainImage");
        IntPtr context = D3D11DeviceCapture.PresentContext;
        if (context == IntPtr.Zero || !D3D11Interop.EnableMultithreadProtection(context))
        {
            return new SessionProbeResult
            {
                Created = true,
                FrameLoopResult = -1001
            };
        }

        IntPtr gameSwapChain = D3D11DeviceCapture.PresentSwapChain;
        if (gameSwapChain == IntPtr.Zero)
        {
            return new SessionProbeResult
            {
                Created = true,
                FrameLoopResult = -1002
            };
        }

        D3D11Texture2DDescription gameBackBufferDescription;
        IntPtr backBufferProbe = D3D11Interop.GetSwapChainBackBuffer(gameSwapChain);
        try
        {
            gameBackBufferDescription = D3D11Interop.GetTextureDescription(backBufferProbe);
        }
        finally
        {
            D3D11Interop.Release(backBufferProbe);
        }

        ValidateGameBackBuffer(gameBackBufferDescription);
        XrReferenceSpaceCreateInfo spaceInfo = new()
        {
            Type = 37,
            ReferenceSpaceType = 1,
            PoseInReferenceSpace = IdentityPose()
        };
        Check(createReferenceSpace(session, ref spaceInfo, out IntPtr localSpace), "create OpenXR view space");
        XrReferenceSpaceCreateInfo worldSpaceInfo = new()
        {
            Type = 37,
            ReferenceSpaceType = 2,
            PoseInReferenceSpace = IdentityPose()
        };
        int worldSpaceResult = createReferenceSpace(
            session,
            ref worldSpaceInfo,
            out IntPtr worldSpace);
        if (worldSpaceResult != XrSuccess)
        {
            worldSpace = IntPtr.Zero;
            RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = "openxr-world-space-unavailable",
                BootstrapVersion = RuntimeProbe.BootstrapVersion,
                ProcessId = Environment.ProcessId,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                OpenXrFrameLoopResult = worldSpaceResult,
                Reason = "XR_LOCAL_REFERENCE_SPACE is unavailable; existing VIEW-space panels remain active and non-live 6DoF safely waits."
            });
        }
        IntPtr projectionLayerPointer = IntPtr.Zero;
        IntPtr cursorLayerPointer = IntPtr.Zero;
        IntPtr stereoProjectionLayerPointer = IntPtr.Zero;
        IntPtr stereoProjectionViewsPointer = IntPtr.Zero;
        IntPtr uiLayerPointer = IntPtr.Zero;
        IntPtr layerPointers = IntPtr.Zero;
        IntPtr stereoViewBuffer = IntPtr.Zero;
        IntPtr worldStereoViewBuffer = IntPtr.Zero;
        IntPtr gpuCompletionQuery = IntPtr.Zero;
        OpenXrControllerActions? controllerActions = null;
        VrPointerInput? pointerInput = null;
        PanelSwapchainResources? panel = null;
        PanelSwapchainResources? cursorPanel = null;
        PanelSwapchainResources? uiPanel = null;
        EyeSwapchainResources? leftEyePanel = null;
        EyeSwapchainResources? rightEyePanel = null;
        D3D11VerticalBlitter? verticalBlitter = null;
        string textureDescription = string.Empty;
        string sourceDescriptionText = string.Empty;
        string sourceKind = "GAME_BACKBUFFER";
        string frameLoopStage = "setup";
        int resizeCount = 0;
        bool uiDiagnosticSnapshotSaved = false;
        int uiDiagnosticFrameCount = 0;
        bool stereoViewSampleLogged = false;
        bool stereoViewFailureLogged = false;
        bool worldStereoViewSampleLogged = false;
        bool m6DynamicUiReadyLogged = false;
        DateTimeOffset nextM6DynamicUiFailureUtc = DateTimeOffset.MinValue;
        bool latestStereoViewsValid = false;
        XrView latestLeftView = default;
        XrView latestRightView = default;
        bool handPanelVisible = false;
        bool? previousFlatPanelMode = null;
        long lastHandPanelEligibleTimestamp = long.MinValue;
        DateTimeOffset nextControllerFailureUtc = DateTimeOffset.MinValue;
        DateTimeOffset nextHandPanelGateLogUtc = DateTimeOffset.MinValue;

        try
        {
            controllerActions = OpenXrControllerActions.TryCreate(
                loader,
                instance,
                session,
                VrSettingsRuntime.Current);
            gpuCompletionQuery = D3D11Interop.CreateEventQuery(device);
            panel = CreatePanelSwapchainResources(
                session,
                localSpace,
                gameBackBufferDescription,
                enumerateFormats,
                createSwapchain,
                enumerateImages,
                destroySwapchain,
                handAttached: controllerActions is not null);
            pointerInput = new VrPointerInput(VrSettingsRuntime.Current.Input);
            try
            {
                cursorPanel = CreateCursorSwapchainResources(
                    session,
                    localSpace,
                    gameBackBufferDescription,
                    enumerateFormats,
                    createSwapchain,
                    enumerateImages,
                    destroySwapchain);
            }
            catch (Exception exception)
            {
                RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                {
                    TimestampUtc = DateTimeOffset.UtcNow,
                    Event = "controller-pointer-cursor-failure",
                    BootstrapVersion = RuntimeProbe.BootstrapVersion,
                    ProcessId = Environment.ProcessId,
                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    ErrorType = exception.GetType().FullName,
                    Error = exception.Message,
                    Reason = "The visual cursor is disabled; panel rendering and coordinate input continue."
                });
            }
            textureDescription = panel.TextureDescription;
            projectionLayerPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<XrCompositionLayerQuad>());
            Marshal.StructureToPtr(panel.Layer, projectionLayerPointer, fDeleteOld: false);
            if (cursorPanel is not null)
            {
                cursorLayerPointer = Marshal.AllocHGlobal(
                    Marshal.SizeOf<XrCompositionLayerQuad>());
                Marshal.StructureToPtr(cursorPanel.Layer, cursorLayerPointer, fDeleteOld: false);
            }
            uiLayerPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<XrCompositionLayerQuad>());
            stereoProjectionLayerPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<XrCompositionLayerProjection>());
            stereoProjectionViewsPointer = Marshal.AllocHGlobal(
                2 * Marshal.SizeOf<XrCompositionLayerProjectionView>());
            layerPointers = Marshal.AllocHGlobal(4 * IntPtr.Size);
            Marshal.WriteIntPtr(layerPointers, projectionLayerPointer);
            Marshal.WriteIntPtr(layerPointers, IntPtr.Size, uiLayerPointer);
            int stereoViewSize = Marshal.SizeOf<XrView>();
            stereoViewBuffer = Marshal.AllocHGlobal(2 * stereoViewSize);
            if (worldSpace != IntPtr.Zero)
            {
                worldStereoViewBuffer = Marshal.AllocHGlobal(2 * stereoViewSize);
            }

            bool ready = false;
            Stopwatch readyTimeout = Stopwatch.StartNew();
            while (readyTimeout.ElapsedMilliseconds < 10_000 && !ready)
            {
                XrEventDataBuffer eventData = NewEventBuffer();
                int pollResult = pollEvent(instance, ref eventData);
                if (pollResult == xrEventUnavailable)
                {
                    Thread.Sleep(10);
                    continue;
                }

                Check(pollResult, "poll OpenXR session event");
                if (eventData.Type == 18 && ReadSessionState(eventData) == xrSessionStateReady)
                {
                    ready = true;
                }
            }

            if (!ready)
            {
                return new SessionProbeResult
                {
                    Created = true,
                    TestPatternWidth = panel.Width,
                    TestPatternHeight = panel.Height,
                    TestPatternFormat = panel.Format,
                    FrameLoopResult = -1000
                };
            }

            XrSessionBeginInfo beginInfo = new()
            {
                Type = 10,
                PrimaryViewConfigurationType = 2
            };
            Check(beginSession(session, ref beginInfo), "begin OpenXR session");
            frameLoopStage = "session-begun";
            int frames = 0;
            int layerFrames = 0;
            int frameResult = XrSuccess;
            bool sessionEnded = false;
            bool runtimeExitObserved = false;
            for (; !runtimeExitObserved; frames++)
            {
                while (true)
                {
                    XrEventDataBuffer eventData = NewEventBuffer();
                    int pollResult = pollEvent(instance, ref eventData);
                    if (pollResult == xrEventUnavailable)
                    {
                        break;
                    }

                    Check(pollResult, "poll active OpenXR session event");
                    if (eventData.Type != 18)
                    {
                        continue;
                    }

                    int sessionState = ReadSessionState(eventData);
                    if (sessionState == xrSessionStateStopping)
                    {
                        _ = endSession(session);
                        sessionEnded = true;
                        runtimeExitObserved = true;
                        break;
                    }

                    if (sessionState == xrSessionStateLossPending ||
                        sessionState == xrSessionStateExiting)
                    {
                        runtimeExitObserved = true;
                        break;
                    }
                }

                if (runtimeExitObserved)
                {
                    break;
                }

                XrFrameWaitInfo waitInfo = new() { Type = 33 };
                XrFrameState frameState = new() { Type = 44 };
                frameLoopStage = "wait-frame";
                frameResult = waitFrame(session, ref waitInfo, ref frameState);
                if (frameResult == -2)
                {
                    frameResult = XrSuccess;
                    Thread.Sleep(1);
                    continue;
                }

                if (frameResult != XrSuccess)
                {
                    break;
                }

                StereoPerformanceTelemetry.RecordOpenXrFrame(
                    frameState.PredictedDisplayPeriod);

                for (int eyeIndex = 0; eyeIndex < 2; eyeIndex++)
                {
                    Marshal.StructureToPtr(
                        new XrView { Type = 7 },
                        IntPtr.Add(stereoViewBuffer, eyeIndex * stereoViewSize),
                        fDeleteOld: false);
                }
                XrViewLocateInfo viewLocateInfo = new()
                {
                    Type = 6,
                    ViewConfigurationType = 2,
                    DisplayTime = frameState.PredictedDisplayTime,
                    Space = localSpace
                };
                XrViewState viewState = new() { Type = 11 };
                int locateResult = locateViews(
                    session,
                    ref viewLocateInfo,
                    ref viewState,
                    2,
                    out uint locatedViewCount,
                    stereoViewBuffer);
                bool currentStereoViewsValid =
                    locateResult == XrSuccess && locatedViewCount == 2 &&
                    (viewState.ViewStateFlags & 3UL) == 3UL;
                if (currentStereoViewsValid)
                {
                    XrView leftView = Marshal.PtrToStructure<XrView>(stereoViewBuffer);
                    XrView rightView = Marshal.PtrToStructure<XrView>(
                        IntPtr.Add(stereoViewBuffer, stereoViewSize));
                    latestLeftView = leftView;
                    latestRightView = rightView;
                    latestStereoViewsValid = true;
                    OpenXrStereoStateRegistry.UpdateViews(
                        viewState.ViewStateFlags,
                        CreateStereoEyeState(leftView),
                        CreateStereoEyeState(rightView));
                    if (!stereoViewSampleLogged)
                    {
                        float deltaX = rightView.Pose.Position.X - leftView.Pose.Position.X;
                        float deltaY = rightView.Pose.Position.Y - leftView.Pose.Position.Y;
                        float deltaZ = rightView.Pose.Position.Z - leftView.Pose.Position.Z;
                        float ipd = MathF.Sqrt(
                            (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ));
                        RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                        {
                            TimestampUtc = DateTimeOffset.UtcNow,
                            Event = "openxr-stereo-view-sample",
                            BootstrapVersion = RuntimeProbe.BootstrapVersion,
                            ProcessId = Environment.ProcessId,
                            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                            OpenXrStereoViewStateFlags = viewState.ViewStateFlags,
                            OpenXrStereoIpdMeters = ipd,
                            OpenXrStereoViews = new[]
                            {
                                CreateStereoViewProbeRecord(0, leftView),
                                CreateStereoViewProbeRecord(1, rightView)
                            },
                            Reason = "Read-only predicted stereo views relative to XR_VIEW_REFERENCE_SPACE."
                        });
                        stereoViewSampleLogged = true;
                    }
                }
                else if (!stereoViewFailureLogged)
                {
                    RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                    {
                        TimestampUtc = DateTimeOffset.UtcNow,
                        Event = "openxr-stereo-view-sample-failure",
                        BootstrapVersion = RuntimeProbe.BootstrapVersion,
                        ProcessId = Environment.ProcessId,
                        Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                        OpenXrStereoViewStateFlags = viewState.ViewStateFlags,
                        Error = $"xrLocateViews={locateResult};viewCount={locatedViewCount}"
                    });
                    stereoViewFailureLogged = true;
                }

                if (worldSpace != IntPtr.Zero)
                {
                    for (int eyeIndex = 0; eyeIndex < 2; eyeIndex++)
                    {
                        Marshal.StructureToPtr(
                            new XrView { Type = 7 },
                            IntPtr.Add(worldStereoViewBuffer, eyeIndex * stereoViewSize),
                            fDeleteOld: false);
                    }
                    XrViewLocateInfo worldViewLocateInfo = new()
                    {
                        Type = 6,
                        ViewConfigurationType = 2,
                        DisplayTime = frameState.PredictedDisplayTime,
                        Space = worldSpace
                    };
                    XrViewState worldViewState = new() { Type = 11 };
                    int worldLocateResult = locateViews(
                        session,
                        ref worldViewLocateInfo,
                        ref worldViewState,
                        2,
                        out uint worldLocatedViewCount,
                        worldStereoViewBuffer);
                    bool worldViewsValid =
                        worldLocateResult == XrSuccess && worldLocatedViewCount == 2 &&
                        (worldViewState.ViewStateFlags & 3UL) == 3UL;
                    if (worldViewsValid)
                    {
                        XrView worldLeftView = Marshal.PtrToStructure<XrView>(
                            worldStereoViewBuffer);
                        XrView worldRightView = Marshal.PtrToStructure<XrView>(
                            IntPtr.Add(worldStereoViewBuffer, stereoViewSize));
                        OpenXrStereoStateRegistry.UpdateWorldViews(
                            worldViewState.ViewStateFlags,
                            CreateStereoEyeState(worldLeftView),
                            CreateStereoEyeState(worldRightView));
                        if (!worldStereoViewSampleLogged)
                        {
                            RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                            {
                                TimestampUtc = DateTimeOffset.UtcNow,
                                Event = "openxr-world-view-sample",
                                BootstrapVersion = RuntimeProbe.BootstrapVersion,
                                ProcessId = Environment.ProcessId,
                                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                                OpenXrStereoViewStateFlags = worldViewState.ViewStateFlags,
                                OpenXrStereoViews = new[]
                                {
                                    CreateStereoViewProbeRecord(0, worldLeftView),
                                    CreateStereoViewProbeRecord(1, worldRightView)
                                },
                                Reason = "Predicted stereo views relative to XR_LOCAL_REFERENCE_SPACE are ready for non-live positional 6DoF."
                            });
                            worldStereoViewSampleLogged = true;
                        }
                    }
                }

                OpenXrControllerFrame controllerFrame = default;
                if (controllerActions is not null)
                {
                    try
                    {
                        controllerFrame = controllerActions.Update(
                            frameState.PredictedDisplayTime,
                            localSpace);
                    }
                    catch (Exception exception)
                    {
                        DateTimeOffset failureNow = DateTimeOffset.UtcNow;
                        if (failureNow >= nextControllerFailureUtc)
                        {
                            RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                            {
                                TimestampUtc = failureNow,
                                Event = "openxr-controller-actions-update-failure",
                                BootstrapVersion = RuntimeProbe.BootstrapVersion,
                                ProcessId = Environment.ProcessId,
                                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                                ErrorType = exception.GetType().FullName,
                                Error = exception.Message,
                                Reason = "The hand panel is hidden for this frame; stereo and the PC game continue."
                            });
                            nextControllerFailureUtc = failureNow.AddSeconds(10);
                        }
                    }
                }
                OpenXrLocomotionStateRegistry.Update(
                    controllerFrame.LocomotionThumbstickActive,
                    controllerFrame.LocomotionThumbstickX,
                    controllerFrame.LocomotionThumbstickY,
                    controllerFrame.ViewTurnThumbstickActive,
                    controllerFrame.ViewTurnThumbstickX,
                    controllerFrame.ViewTurnThumbstickY);
                bool handPanelInView =
                    controllerFrame.PanelPoseTracked &&
                    currentStereoViewsValid &&
                    IsHandInView(
                        controllerFrame.PanelPose,
                        latestLeftView,
                        latestRightView);
                bool rawHandPanelEligible = controllerFrame.PanelEnabled && handPanelInView;
                if (controllerFrame.PanelEnabled && !rawHandPanelEligible)
                {
                    DateTimeOffset gateNow = DateTimeOffset.UtcNow;
                    if (gateNow >= nextHandPanelGateLogUtc)
                    {
                        RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                        {
                            TimestampUtc = gateNow,
                            Event = "hand-panel-gate-waiting",
                            BootstrapVersion = RuntimeProbe.BootstrapVersion,
                            ProcessId = Environment.ProcessId,
                            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                            Reason = $"tracked={controllerFrame.PanelPoseTracked};views={currentStereoViewsValid};handInView={handPanelInView}."
                        });
                        nextHandPanelGateLogUtc = gateNow.AddSeconds(5);
                    }
                }
                long visibilityNow = Stopwatch.GetTimestamp();
                if (rawHandPanelEligible)
                {
                    lastHandPanelEligibleTimestamp = visibilityNow;
                }
                bool nextHandPanelVisible =
                    controllerFrame.PanelEnabled &&
                    (rawHandPanelEligible ||
                        (handPanelVisible &&
                            lastHandPanelEligibleTimestamp != long.MinValue &&
                            visibilityNow - lastHandPanelEligibleTimestamp <=
                                Stopwatch.Frequency *
                                VrSettingsRuntime.Current.Panel.VisibilityHysteresisMilliseconds /
                                1000));
                if (nextHandPanelVisible != handPanelVisible)
                {
                    RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                    {
                        TimestampUtc = DateTimeOffset.UtcNow,
                        Event = nextHandPanelVisible
                            ? "hand-panel-visible"
                            : "hand-panel-hidden",
                        BootstrapVersion = RuntimeProbe.BootstrapVersion,
                        ProcessId = Environment.ProcessId,
                        Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                        Reason = nextHandPanelVisible
                            ? $"Toggle, {VrSettingsRuntime.Current.Panel.PanelHand}-hand tracking and HMD hand-FOV gates are satisfied;viewerFacing={VrSettingsRuntime.Current.Panel.ViewerFacing}."
                            : $"enabled={controllerFrame.PanelEnabled};tracked={controllerFrame.PanelPoseTracked};rawEligible={rawHandPanelEligible}."
                    });
                }
                handPanelVisible = nextHandPanelVisible;

                XrFrameBeginInfo frameBegin = new() { Type = 46 };
                frameLoopStage = "begin-frame";
                frameResult = beginFrame(session, ref frameBegin);
                if (frameResult != XrSuccess)
                {
                    break;
                }

                uint layerCount = 0;
                IntPtr layers = IntPtr.Zero;
                if (frameState.ShouldRender != 0)
                {
                    bool flatPanelMode = !latestStereoViewsValid ||
                        !UnityRenderSourceRegistry.HasFreshStereoTextures(750);
                    if (previousFlatPanelMode != flatPanelMode)
                    {
                        RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                        {
                            TimestampUtc = DateTimeOffset.UtcNow,
                            Event = flatPanelMode
                                ? "front-panel-mode-entered"
                                : "front-panel-mode-exited",
                            BootstrapVersion = RuntimeProbe.BootstrapVersion,
                            ProcessId = Environment.ProcessId,
                            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                            Reason = flatPanelMode
                                ? "No fresh stereo world is available; the full game backbuffer is automatically shown head-fixed in front of the viewer."
                                : "Fresh stereo world output is available; the automatic front panel is removed and the optional Grip hand panel is used."
                        });
                        previousFlatPanelMode = flatPanelMode;
                    }

                    bool handPanelReady = false;
                    bool panelRequested = flatPanelMode || handPanelVisible;
                    if (panelRequested)
                    {
                        D3D11TextureLease? liveWorldTexture = PreferCompositedGameBackBuffer
                            ? null
                            : UnityRenderSourceRegistry.AcquireLiveWorldTexture(1_500);
                        IntPtr frameSource = IntPtr.Zero;
                        bool releaseGameBackBuffer = false;
                        try
                        {
                            if (liveWorldTexture is not null)
                            {
                                frameSource = liveWorldTexture.Texture;
                                D3D11Texture2DDescription liveDescription =
                                    D3D11Interop.GetTextureDescription(frameSource);
                                if (!IsSupportedPanelSourceFormat(liveDescription.Format))
                                {
                                    liveWorldTexture.Dispose();
                                    liveWorldTexture = null;
                                    frameSource = IntPtr.Zero;
                                }
                            }

                            if (frameSource == IntPtr.Zero)
                            {
                                frameSource = D3D11Interop.GetSwapChainBackBuffer(gameSwapChain);
                                releaseGameBackBuffer = true;
                                sourceKind = "GAME_BACKBUFFER";
                            }
                            else
                            {
                                sourceKind = "LIVE_WORLD_RT";
                            }

                            D3D11Texture2DDescription currentDescription =
                                D3D11Interop.GetTextureDescription(frameSource);
                            ValidateGameBackBuffer(currentDescription);
                            sourceDescriptionText = D3D11Interop.DescribeTexture(frameSource);
                            if (!panel.Matches(currentDescription))
                            {
                                frameLoopStage = "recreate-panel-swapchain";
                                PanelSwapchainResources replacement = CreatePanelSwapchainResources(
                                    session,
                                    localSpace,
                                    currentDescription,
                                    enumerateFormats,
                                    createSwapchain,
                                    enumerateImages,
                                    destroySwapchain,
                                    handAttached: !flatPanelMode && controllerActions is not null);
                                verticalBlitter?.ResetViews();
                                DestroyPanelSwapchainResources(panel, destroySwapchain);
                                panel = replacement;
                                textureDescription = panel.TextureDescription;
                                resizeCount++;
                            }

                            ConfigurePanelLayer(
                                panel,
                                handAttached: !flatPanelMode,
                                localSpace,
                                controllerFrame.PanelPose,
                                latestLeftView,
                                latestRightView);
                            Marshal.StructureToPtr(
                                panel.Layer,
                                projectionLayerPointer,
                                fDeleteOld: false);

                            XrSwapchainImageAcquireInfo acquireInfo = new() { Type = 55 };
                            frameLoopStage = "acquire-image";
                            Check(
                                acquireImage(panel.Swapchain, ref acquireInfo, out uint imageIndex),
                                "acquire panel image");
                            XrSwapchainImageWaitInfo imageWaitInfo = new()
                            {
                                Type = 56,
                                Timeout = long.MaxValue
                            };
                            frameLoopStage = "wait-image";
                            Check(
                                waitImage(panel.Swapchain, ref imageWaitInfo),
                                "wait for panel image");
                            try
                            {
                                if (imageIndex >= panel.Images.Count)
                                {
                                    throw new InvalidOperationException(
                                        $"Invalid panel image index: {imageIndex}.");
                                }

                                int textureIndex = checked((int)imageIndex);
                                if (sourceKind == "LIVE_WORLD_RT")
                                {
                                    frameLoopStage = "flip-live-world-texture";
                                    verticalBlitter ??= new D3D11VerticalBlitter(device);
                                    verticalBlitter.BlitFlipped(
                                        context,
                                        panel.Images[textureIndex],
                                        frameSource,
                                        currentDescription.Width,
                                        currentDescription.Height,
                                        currentDescription.Format);
                                }
                                else
                                {
                                    frameLoopStage = "copy-game-backbuffer";
                                    D3D11Interop.CopyTexture(
                                        context,
                                        panel.Images[textureIndex],
                                        frameSource);
                                }
                                frameLoopStage = "wait-gpu";
                                D3D11Interop.WaitForGpuLowLatency(
                                    context,
                                    gpuCompletionQuery,
                                    2_000);
                            }
                            finally
                            {
                                XrSwapchainImageReleaseInfo releaseInfo = new() { Type = 57 };
                                frameLoopStage = "release-image";
                                Check(
                                    releaseImage(panel.Swapchain, ref releaseInfo),
                                    "release panel image");
                            }
                        }
                        finally
                        {
                            liveWorldTexture?.Dispose();
                            if (releaseGameBackBuffer && frameSource != IntPtr.Zero)
                            {
                                D3D11Interop.Release(frameSource);
                            }
                        }
                        handPanelReady = true;
                    }

                    bool pointerHit = false;
                    bool cursorReady = false;
                    float pointerU = 0f;
                    float pointerV = 0f;
                    if (handPanelReady && controllerFrame.PointerAimTracked)
                    {
                        pointerHit = TryIntersectPanel(
                            controllerFrame.PointerAimPose,
                            panel.Layer.Pose,
                            panel.Layer.Size,
                            out pointerU,
                            out pointerV);
                    }
                    VrPointerVisual pointerVisual = pointerInput.Update(
                        handPanelReady && pointerHit,
                        pointerU,
                        pointerV,
                        controllerFrame);
                    if (pointerVisual.Visible && cursorPanel is not null)
                    {
                        ConfigureCursorLayer(
                            cursorPanel,
                            panel,
                            pointerVisual.U,
                            pointerVisual.V);
                        UpdateCursorSwapchain(
                            context,
                            gpuCompletionQuery,
                            cursorPanel,
                            acquireImage,
                            waitImage,
                            releaseImage);
                        Marshal.StructureToPtr(
                            cursorPanel.Layer,
                            cursorLayerPointer,
                            fDeleteOld: false);
                        cursorReady = true;
                    }

                    D3D11TextureLease? m6WorldTexture = null;
                    if (m6WorldTexture is not null &&
                        !m6WorldTexture.SourceName.StartsWith(
                            "M6_NONLIVE|",
                            StringComparison.Ordinal))
                    {
                        m6WorldTexture.Dispose();
                        m6WorldTexture = null;
                    }
                    bool m6DynamicUiRequired = m6WorldTexture is not null;
                    D3D11TextureLease? liveUiTexture = null;
                    if (m6DynamicUiRequired &&
                        (liveUiTexture is null ||
                            !liveUiTexture.SourceName.StartsWith(
                                "M6_SYNC_UI|",
                                StringComparison.Ordinal)))
                    {
                        liveUiTexture?.Dispose();
                        liveUiTexture = null;
                    }
                    bool uiLayerReady = false;
                    try
                    {
                        if (liveUiTexture is not null)
                        {
                            D3D11Texture2DDescription uiDescription =
                                D3D11Interop.GetTextureDescription(liveUiTexture.Texture);
                            ValidateGameBackBuffer(uiDescription);
                            if (IsSupportedPanelSourceFormat(uiDescription.Format))
                            {
                                if (liveUiTexture.SourceName.EndsWith(
                                        "(rendered)",
                                        StringComparison.Ordinal))
                                {
                                    uiDiagnosticFrameCount++;
                                }
                                if (uiPanel is null || !uiPanel.Matches(uiDescription))
                                {
                                    frameLoopStage = "recreate-ui-swapchain";
                                    PanelSwapchainResources replacement =
                                        CreatePanelSwapchainResources(
                                            session,
                                            localSpace,
                                            uiDescription,
                                            enumerateFormats,
                                            createSwapchain,
                                            enumerateImages,
                                            destroySwapchain,
                                            alphaLayer: true);
                                    Marshal.StructureToPtr(
                                        replacement.Layer,
                                        uiLayerPointer,
                                        fDeleteOld: false);
                                    DestroyPanelSwapchainResources(uiPanel, destroySwapchain);
                                    uiPanel = replacement;
                                }

                                XrSwapchainImageAcquireInfo uiAcquireInfo = new() { Type = 55 };
                                frameLoopStage = "acquire-ui-image";
                                Check(
                                    acquireImage(
                                        uiPanel.Swapchain,
                                        ref uiAcquireInfo,
                                        out uint uiImageIndex),
                                    "acquire UI layer image");
                                XrSwapchainImageWaitInfo uiWaitInfo = new()
                                {
                                    Type = 56,
                                    Timeout = long.MaxValue
                                };
                                frameLoopStage = "wait-ui-image";
                                Check(
                                    waitImage(uiPanel.Swapchain, ref uiWaitInfo),
                                    "wait for UI layer image");
                                try
                                {
                                    if (uiImageIndex >= uiPanel.Images.Count)
                                    {
                                        throw new InvalidOperationException(
                                            $"Invalid UI layer image index: {uiImageIndex}.");
                                    }

                                    bool synchronizedM6Ui =
                                        liveUiTexture.SourceName.StartsWith(
                                            "M6_SYNC_UI|",
                                            StringComparison.Ordinal);
                                    frameLoopStage = synchronizedM6Ui
                                        ? "copy-m6-synchronized-ui-texture"
                                        : "flip-ui-element-texture";
                                    if (synchronizedM6Ui)
                                    {
                                        D3D11Interop.CopyTexture(
                                            context,
                                            uiPanel.Images[checked((int)uiImageIndex)],
                                            liveUiTexture.Texture);
                                    }
                                    else
                                    {
                                        verticalBlitter ??= new D3D11VerticalBlitter(device);
                                        verticalBlitter.BlitFlipped(
                                            context,
                                            uiPanel.Images[checked((int)uiImageIndex)],
                                            liveUiTexture.Texture,
                                            uiDescription.Width,
                                            uiDescription.Height,
                                            uiDescription.Format,
                                            transparentBlack: true);
                                    }
                                    frameLoopStage = "wait-ui-gpu";
                                    D3D11Interop.WaitForGpuLowLatency(
                                        context,
                                        gpuCompletionQuery,
                                        2_000);
                                    if (!uiDiagnosticSnapshotSaved &&
                                        uiDiagnosticFrameCount >= 30)
                                    {
                                        SaveUiDiagnosticTexture(
                                            device,
                                            context,
                                            uiPanel.Images[checked((int)uiImageIndex)]);
                                        uiDiagnosticSnapshotSaved = true;
                                    }
                                }
                                finally
                                {
                                    XrSwapchainImageReleaseInfo uiReleaseInfo = new() { Type = 57 };
                                    frameLoopStage = "release-ui-image";
                                    Check(
                                        releaseImage(uiPanel.Swapchain, ref uiReleaseInfo),
                                        "release UI layer image");
                                }

                                uiLayerReady = true;
                                layerCount = 2;
                            }
                        }
                    }
                    finally
                    {
                        liveUiTexture?.Dispose();
                    }

                    m6WorldTexture?.Dispose();
                    m6WorldTexture = null;

                    if (m6DynamicUiRequired && m6WorldTexture is not null)
                    {
                        IntPtr compositeBackBuffer = IntPtr.Zero;
                        try
                        {
                            compositeBackBuffer = D3D11Interop.GetSwapChainBackBuffer(
                                gameSwapChain);
                            D3D11Texture2DDescription compositeDescription =
                                D3D11Interop.GetTextureDescription(compositeBackBuffer);
                            D3D11Texture2DDescription worldDescription =
                                D3D11Interop.GetTextureDescription(m6WorldTexture.Texture);
                            ValidateGameBackBuffer(compositeDescription);
                            ValidateGameBackBuffer(worldDescription);
                            if (!IsSupportedPanelSourceFormat(compositeDescription.Format) ||
                                !IsSupportedPanelSourceFormat(worldDescription.Format))
                            {
                                throw new InvalidOperationException(
                                    "M6 dynamic UI sources use an unsupported texture format.");
                            }

                            if (uiPanel is null || !uiPanel.Matches(compositeDescription))
                            {
                                frameLoopStage = "recreate-m6-ui-swapchain";
                                PanelSwapchainResources replacement =
                                    CreatePanelSwapchainResources(
                                        session,
                                        localSpace,
                                        compositeDescription,
                                        enumerateFormats,
                                        createSwapchain,
                                        enumerateImages,
                                        destroySwapchain,
                                        alphaLayer: true);
                                Marshal.StructureToPtr(
                                    replacement.Layer,
                                    uiLayerPointer,
                                    fDeleteOld: false);
                                DestroyPanelSwapchainResources(uiPanel, destroySwapchain);
                                uiPanel = replacement;
                            }

                            XrSwapchainImageAcquireInfo uiAcquireInfo = new() { Type = 55 };
                            frameLoopStage = "acquire-m6-ui-image";
                            Check(
                                acquireImage(
                                    uiPanel.Swapchain,
                                    ref uiAcquireInfo,
                                    out uint uiImageIndex),
                                "acquire M6 dynamic UI layer image");
                            XrSwapchainImageWaitInfo uiWaitInfo = new()
                            {
                                Type = 56,
                                Timeout = long.MaxValue
                            };
                            frameLoopStage = "wait-m6-ui-image";
                            Check(
                                waitImage(uiPanel.Swapchain, ref uiWaitInfo),
                                "wait for M6 dynamic UI layer image");
                            try
                            {
                                if (uiImageIndex >= uiPanel.Images.Count)
                                {
                                    throw new InvalidOperationException(
                                        $"Invalid M6 UI layer image index: {uiImageIndex}.");
                                }

                                frameLoopStage = "difference-m6-ui-texture";
                                verticalBlitter ??= new D3D11VerticalBlitter(device);
                                verticalBlitter.BlitUiDifference(
                                    context,
                                    uiPanel.Images[checked((int)uiImageIndex)],
                                    compositeBackBuffer,
                                    m6WorldTexture.Texture,
                                    compositeDescription.Width,
                                    compositeDescription.Height,
                                    compositeDescription.Format,
                                    worldDescription.Format);
                                frameLoopStage = "wait-m6-ui-gpu";
                                D3D11Interop.WaitForGpuLowLatency(
                                    context,
                                    gpuCompletionQuery,
                                    2_000);
                            }
                            finally
                            {
                                XrSwapchainImageReleaseInfo uiReleaseInfo = new() { Type = 57 };
                                frameLoopStage = "release-m6-ui-image";
                                Check(
                                    releaseImage(uiPanel.Swapchain, ref uiReleaseInfo),
                                    "release M6 dynamic UI layer image");
                            }

                            uiLayerReady = true;
                            if (!m6DynamicUiReadyLogged)
                            {
                                RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                                {
                                    TimestampUtc = DateTimeOffset.UtcNow,
                                    Event = "m6-dynamic-ui-ready",
                                    BootstrapVersion = RuntimeProbe.BootstrapVersion,
                                    ProcessId = Environment.ProcessId,
                                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                                    UiCaptureSubmitted = true,
                                    UiCaptureWidth = checked((int)compositeDescription.Width),
                                    UiCaptureHeight = checked((int)compositeDescription.Height),
                                    UiCaptureTextureDescription =
                                        D3D11Interop.DescribeTexture(compositeBackBuffer),
                                    Reason = "The final backbuffer is differenced against the approved world RT every OpenXR frame; Unity Camera/Canvas targets remain unchanged."
                                });
                                m6DynamicUiReadyLogged = true;
                            }
                        }
                        catch (Exception exception)
                        {
                            uiLayerReady = false;
                            DateTimeOffset failureNow = DateTimeOffset.UtcNow;
                            if (failureNow >= nextM6DynamicUiFailureUtc)
                            {
                                RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
                                {
                                    TimestampUtc = failureNow,
                                    Event = "m6-dynamic-ui-failure",
                                    BootstrapVersion = RuntimeProbe.BootstrapVersion,
                                    ProcessId = Environment.ProcessId,
                                    Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                                    UiCaptureSubmitted = false,
                                    ErrorType = exception.GetType().FullName,
                                    Error = exception.Message,
                                    Reason = "M6 stereo is blocked for this frame; the final backbuffer SafePanel remains active."
                                });
                                nextM6DynamicUiFailureUtc = failureNow.AddSeconds(10);
                            }
                        }
                        finally
                        {
                            if (compositeBackBuffer != IntPtr.Zero)
                            {
                                D3D11Interop.Release(compositeBackBuffer);
                            }
                            m6WorldTexture.Dispose();
                        }
                    }
                    _ = uiLayerReady;

                    D3D11StereoTextureLease? stereoTextures =
                        UnityRenderSourceRegistry.AcquireStereoTextures(750);
                    try
                    {
                        if (stereoTextures is not null &&
                            (stereoTextures.RenderState is not null || latestStereoViewsValid) &&
                            (!stereoTextures.UsesWorldSpace || worldSpace != IntPtr.Zero))
                        {
                            D3D11Texture2DDescription leftDescription =
                                D3D11Interop.GetTextureDescription(stereoTextures.LeftTexture);
                            D3D11Texture2DDescription rightDescription =
                                D3D11Interop.GetTextureDescription(stereoTextures.RightTexture);
                            ValidateGameBackBuffer(leftDescription);
                            ValidateGameBackBuffer(rightDescription);
                            if (leftDescription.Width != rightDescription.Width ||
                                leftDescription.Height != rightDescription.Height ||
                                leftDescription.Format != rightDescription.Format)
                            {
                                throw new InvalidOperationException(
                                    "Left and right stereo textures do not match.");
                            }

                            if (leftEyePanel is null || !leftEyePanel.Matches(leftDescription))
                            {
                                frameLoopStage = "recreate-left-eye-swapchain";
                                EyeSwapchainResources replacement = CreateEyeSwapchainResources(
                                    session,
                                    leftDescription,
                                    enumerateFormats,
                                    createSwapchain,
                                    enumerateImages,
                                    destroySwapchain);
                                DestroyEyeSwapchainResources(leftEyePanel, destroySwapchain);
                                leftEyePanel = replacement;
                            }
                            if (rightEyePanel is null || !rightEyePanel.Matches(rightDescription))
                            {
                                frameLoopStage = "recreate-right-eye-swapchain";
                                EyeSwapchainResources replacement = CreateEyeSwapchainResources(
                                    session,
                                    rightDescription,
                                    enumerateFormats,
                                    createSwapchain,
                                    enumerateImages,
                                    destroySwapchain);
                                DestroyEyeSwapchainResources(rightEyePanel, destroySwapchain);
                                rightEyePanel = replacement;
                            }

                            verticalBlitter ??= new D3D11VerticalBlitter(device);
                            long stereoCopyStartedTimestamp = Stopwatch.GetTimestamp();
                            frameLoopStage = "copy-stereo-pair";
                            long stereoGpuWaitDurationTicks =
                                CopyStereoPairToSwapchains(
                                context,
                                gpuCompletionQuery,
                                leftEyePanel,
                                stereoTextures.LeftTexture,
                                leftDescription,
                                rightEyePanel,
                                stereoTextures.RightTexture,
                                rightDescription,
                                verticalBlitter,
                                acquireImage,
                                waitImage,
                                releaseImage);
                            StereoPerformanceTelemetry.RecordOpenXrStereoSubmission(
                                stereoTextures.PublishedTimestamp,
                                Stopwatch.GetTimestamp() - stereoCopyStartedTimestamp,
                                stereoGpuWaitDurationTicks);

                            int projectionViewSize =
                                Marshal.SizeOf<XrCompositionLayerProjectionView>();
                            OpenXrStereoStateSnapshot? renderState = stereoTextures.RenderState;
                            XrView submittedLeftView = renderState is null
                                ? latestLeftView
                                : CreateXrView(renderState.Left);
                            XrView submittedRightView = renderState is null
                                ? latestRightView
                                : CreateXrView(renderState.Right);
                            XrCompositionLayerProjectionView leftProjectionView = new()
                            {
                                Type = 48,
                                Pose = submittedLeftView.Pose,
                                Fov = submittedLeftView.Fov,
                                SubImage = CreateEyeSubImage(leftEyePanel)
                            };
                            XrCompositionLayerProjectionView rightProjectionView = new()
                            {
                                Type = 48,
                                Pose = submittedRightView.Pose,
                                Fov = submittedRightView.Fov,
                                SubImage = CreateEyeSubImage(rightEyePanel)
                            };
                            Marshal.StructureToPtr(
                                leftProjectionView,
                                stereoProjectionViewsPointer,
                                fDeleteOld: false);
                            Marshal.StructureToPtr(
                                rightProjectionView,
                                IntPtr.Add(stereoProjectionViewsPointer, projectionViewSize),
                                fDeleteOld: false);
                            XrCompositionLayerProjection stereoProjectionLayer = new()
                            {
                                Type = 35,
                                Space = stereoTextures.UsesWorldSpace
                                    ? worldSpace
                                    : localSpace,
                                ViewCount = 2,
                                Views = stereoProjectionViewsPointer
                            };
                            Marshal.StructureToPtr(
                                stereoProjectionLayer,
                                stereoProjectionLayerPointer,
                                fDeleteOld: false);
                            Marshal.WriteIntPtr(
                                layerPointers,
                                stereoProjectionLayerPointer);
                            if (handPanelReady)
                            {
                                Marshal.WriteIntPtr(
                                    layerPointers,
                                    IntPtr.Size,
                                    projectionLayerPointer);
                                if (cursorReady)
                                {
                                    Marshal.WriteIntPtr(
                                        layerPointers,
                                        2 * IntPtr.Size,
                                        cursorLayerPointer);
                                    layerCount = 3;
                                }
                                else
                                {
                                    layerCount = 2;
                                }
                            }
                            else
                            {
                                layerCount = 1;
                            }
                            layers = layerPointers;
                        }
                    }
                    finally
                    {
                        stereoTextures?.Dispose();
                    }
                    if (layerCount == 0 && handPanelReady)
                    {
                        Marshal.WriteIntPtr(layerPointers, projectionLayerPointer);
                        if (cursorReady)
                        {
                            Marshal.WriteIntPtr(
                                layerPointers,
                                IntPtr.Size,
                                cursorLayerPointer);
                            layerCount = 2;
                        }
                        else
                        {
                            layerCount = 1;
                        }
                        layers = layerPointers;
                    }
                }
                else
                {
                    _ = pointerInput.Update(false, 0f, 0f, controllerFrame);
                }

                XrFrameEndInfo frameEnd = new()
                {
                    Type = 12,
                    DisplayTime = frameState.PredictedDisplayTime,
                    EnvironmentBlendMode = 1,
                    LayerCount = layerCount,
                    Layers = layers
                };
                frameLoopStage = "end-frame";
                long endFrameStartedTimestamp = Stopwatch.GetTimestamp();
                frameResult = endFrame(session, ref frameEnd);
                StereoPerformanceTelemetry.RecordOpenXrEndFrame(
                    Stopwatch.GetTimestamp() - endFrameStartedTimestamp);
                if (frameResult != XrSuccess)
                {
                    break;
                }

                if (layerCount != 0)
                {
                    layerFrames++;
                }

                frameLoopStage = "frame-complete";
            }

            if (!sessionEnded && !runtimeExitObserved)
            {
                _ = requestExit(session);
                Stopwatch stopTimeout = Stopwatch.StartNew();
                while (stopTimeout.ElapsedMilliseconds < 5_000)
                {
                    XrEventDataBuffer eventData = NewEventBuffer();
                    int pollResult = pollEvent(instance, ref eventData);
                    if (pollResult == xrEventUnavailable)
                    {
                        Thread.Sleep(10);
                        continue;
                    }

                    if (pollResult < 0)
                    {
                        break;
                    }

                    if (eventData.Type == 18 &&
                        ReadSessionState(eventData) == xrSessionStateStopping)
                    {
                        _ = endSession(session);
                        sessionEnded = true;
                        break;
                    }
                }
            }

            return new SessionProbeResult
            {
                Created = true,
                ReadyObserved = true,
                FramesSubmitted = frames,
                LayerFramesSubmitted = layerFrames,
                TestPatternWidth = panel.Width,
                TestPatternHeight = panel.Height,
                TestPatternFormat = panel.Format,
                TestPatternTextureDescription = textureDescription,
                TestPatternPixelReadback =
                    $"{sourceKind}:{sourceDescriptionText};resizes={resizeCount}",
                FrameLoopStage = runtimeExitObserved
                    ? "runtime-session-exit-observed"
                    : frameLoopStage,
                FrameLoopResult = frameResult
            };
        }
        finally
        {
            if (gpuCompletionQuery != IntPtr.Zero)
            {
                D3D11Interop.Release(gpuCompletionQuery);
            }

            if (layerPointers != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(layerPointers);
            }

            if (stereoViewBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(stereoViewBuffer);
            }

            if (worldStereoViewBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(worldStereoViewBuffer);
            }

            if (projectionLayerPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(projectionLayerPointer);
            }

            if (cursorLayerPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(cursorLayerPointer);
            }

            if (uiLayerPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(uiLayerPointer);
            }

            if (stereoProjectionLayerPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(stereoProjectionLayerPointer);
            }

            if (stereoProjectionViewsPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(stereoProjectionViewsPointer);
            }

            verticalBlitter?.Dispose();
            pointerInput?.Dispose();
            DestroyEyeSwapchainResources(rightEyePanel, destroySwapchain);
            DestroyEyeSwapchainResources(leftEyePanel, destroySwapchain);
            DestroyPanelSwapchainResources(uiPanel, destroySwapchain);
            DestroyPanelSwapchainResources(cursorPanel, destroySwapchain);
            DestroyPanelSwapchainResources(panel, destroySwapchain);
            controllerActions?.Dispose();
            OpenXrLocomotionStateRegistry.Clear();

            if (worldSpace != IntPtr.Zero)
            {
                _ = destroySpace(worldSpace);
            }

            if (localSpace != IntPtr.Zero)
            {
                _ = destroySpace(localSpace);
            }

        }
    }

    private static bool IsHandInView(
        OpenXrControllerPose handPose,
        XrView leftView,
        XrView rightView)
    {
        XrVector3f handPosition = new()
        {
            X = handPose.PositionX,
            Y = handPose.PositionY,
            Z = handPose.PositionZ
        };
        return
            IsPointInView(handPosition, leftView) || IsPointInView(handPosition, rightView);
    }

    private static void ConfigurePanelLayer(
        PanelSwapchainResources resources,
        bool handAttached,
        IntPtr viewSpace,
        OpenXrControllerPose handPose,
        XrView leftView,
        XrView rightView)
    {
        VrPanelSettings panelSettings = VrSettingsRuntime.Current.Panel;
        float aspectRatio = resources.Width / (float)resources.Height;
        float maximumWidth = handAttached ? panelSettings.MaximumWidth : 1.8f;
        float maximumHeight = handAttached ? panelSettings.MaximumHeight : 1.3f;
        float width = maximumWidth;
        float height = width / aspectRatio;
        if (height > maximumHeight)
        {
            height = maximumHeight;
            width = height * aspectRatio;
        }

        XrPosef pose = handAttached
            ? CreateHandPanelPoseInView(handPose, leftView, rightView, panelSettings)
            : IdentityPose();
        if (!handAttached)
        {
            pose.Position.Z = -1.6f;
        }

        XrCompositionLayerQuad layer = resources.Layer;
        layer.Space = viewSpace;
        layer.Pose = pose;
        layer.Size = new XrExtent2Df { Width = width, Height = height };
        resources.Layer = layer;
    }

    private static XrPosef CreateHandPanelPoseInView(
        OpenXrControllerPose handPose,
        XrView leftView,
        XrView rightView,
        VrPanelSettings settings)
    {
        XrQuaternionf handOrientation = new()
        {
            X = handPose.OrientationX,
            Y = handPose.OrientationY,
            Z = handPose.OrientationZ,
            W = handPose.OrientationW
        };
        XrVector3f handPosition = new()
        {
            X = handPose.PositionX,
            Y = handPose.PositionY,
            Z = handPose.PositionZ
        };
        XrVector3f controllerTip = Add(
            handPosition,
            Rotate(
                handOrientation,
                new XrVector3f
                {
                    X = settings.OffsetX,
                    Y = settings.OffsetY,
                    Z = settings.OffsetZ
                }));
        XrVector3f panelPosition = controllerTip;
        XrVector3f eyeMidpoint = new()
        {
            X = (leftView.Pose.Position.X + rightView.Pose.Position.X) * 0.5f,
            Y = (leftView.Pose.Position.Y + rightView.Pose.Position.Y) * 0.5f,
            Z = (leftView.Pose.Position.Z + rightView.Pose.Position.Z) * 0.5f
        };
        XrVector3f panelToEye = Subtract(eyeMidpoint, panelPosition);
        XrQuaternionf baseOrientation;
        if (settings.ViewerFacing)
        {
            float yaw = MathF.Atan2(panelToEye.X, panelToEye.Z);
            float halfYaw = yaw * 0.5f;
            baseOrientation = new XrQuaternionf
            {
                Y = MathF.Sin(halfYaw),
                W = MathF.Cos(halfYaw)
            };
        }
        else
        {
            baseOrientation = handOrientation;
        }
        XrQuaternionf configuredRotation = QuaternionFromEulerDegrees(
            settings.RotationPitch,
            settings.RotationYaw,
            settings.RotationRoll);
        return new XrPosef
        {
            Orientation = Multiply(baseOrientation, configuredRotation),
            Position = panelPosition
        };
    }

    private static bool TryIntersectPanel(
        OpenXrControllerPose aimPose,
        XrPosef panelPose,
        XrExtent2Df panelSize,
        out float u,
        out float v)
    {
        u = 0f;
        v = 0f;
        XrQuaternionf aimOrientation = new()
        {
            X = aimPose.OrientationX,
            Y = aimPose.OrientationY,
            Z = aimPose.OrientationZ,
            W = aimPose.OrientationW
        };
        XrVector3f rayOrigin = new()
        {
            X = aimPose.PositionX,
            Y = aimPose.PositionY,
            Z = aimPose.PositionZ
        };
        XrVector3f rayDirection = Rotate(
            aimOrientation,
            new XrVector3f { Z = -1f });
        XrVector3f panelNormal = Rotate(
            panelPose.Orientation,
            new XrVector3f { Z = 1f });
        float denominator = Dot(rayDirection, panelNormal);
        if (denominator >= -0.01f)
        {
            return false;
        }

        float distance = Dot(Subtract(panelPose.Position, rayOrigin), panelNormal) /
            denominator;
        if (distance <= 0.02f || distance > 10f)
        {
            return false;
        }

        XrVector3f hitPoint = Add(rayOrigin, Scale(rayDirection, distance));
        XrQuaternionf inversePanel = new()
        {
            X = -panelPose.Orientation.X,
            Y = -panelPose.Orientation.Y,
            Z = -panelPose.Orientation.Z,
            W = panelPose.Orientation.W
        };
        XrVector3f localHit = Rotate(
            inversePanel,
            Subtract(hitPoint, panelPose.Position));
        float halfWidth = panelSize.Width * 0.5f;
        float halfHeight = panelSize.Height * 0.5f;
        if (MathF.Abs(localHit.X) > halfWidth || MathF.Abs(localHit.Y) > halfHeight)
        {
            return false;
        }

        u = Math.Clamp((localHit.X / panelSize.Width) + 0.5f, 0f, 1f);
        v = Math.Clamp(0.5f - (localHit.Y / panelSize.Height), 0f, 1f);
        return true;
    }

    private static void ConfigureCursorLayer(
        PanelSwapchainResources cursor,
        PanelSwapchainResources panel,
        float u,
        float v)
    {
        XrCompositionLayerQuad panelLayer = panel.Layer;
        float localX = (u - 0.5f) * panelLayer.Size.Width;
        float localY = (0.5f - v) * panelLayer.Size.Height;
        XrVector3f offset = Rotate(
            panelLayer.Pose.Orientation,
            new XrVector3f { X = localX, Y = localY, Z = 0.006f });
        float size = Math.Clamp(
            MathF.Min(panelLayer.Size.Width, panelLayer.Size.Height) * 0.035f,
            0.012f,
            0.035f);

        XrCompositionLayerQuad cursorLayer = cursor.Layer;
        cursorLayer.Space = panelLayer.Space;
        cursorLayer.Pose = new XrPosef
        {
            Orientation = panelLayer.Pose.Orientation,
            Position = Add(panelLayer.Pose.Position, offset)
        };
        cursorLayer.Size = new XrExtent2Df { Width = size, Height = size };
        cursor.Layer = cursorLayer;
    }

    private static bool IsPointInView(XrVector3f point, XrView view)
    {
        XrVector3f eyeRelative = Subtract(point, view.Pose.Position);
        XrQuaternionf inverseEye = new()
        {
            X = -view.Pose.Orientation.X,
            Y = -view.Pose.Orientation.Y,
            Z = -view.Pose.Orientation.Z,
            W = view.Pose.Orientation.W
        };
        XrVector3f viewPoint = Rotate(inverseEye, eyeRelative);
        float forward = -viewPoint.Z;
        if (forward <= 0.05f)
        {
            return false;
        }
        float horizontal = MathF.Atan2(viewPoint.X, forward);
        float vertical = MathF.Atan2(viewPoint.Y, forward);
        const float marginRadians = 0.08726646f;
        return horizontal >= view.Fov.AngleLeft - marginRadians &&
            horizontal <= view.Fov.AngleRight + marginRadians &&
            vertical >= view.Fov.AngleDown - marginRadians &&
            vertical <= view.Fov.AngleUp + marginRadians;
    }

    private static XrVector3f Rotate(XrQuaternionf rotation, XrVector3f value)
    {
        XrVector3f q = new() { X = rotation.X, Y = rotation.Y, Z = rotation.Z };
        XrVector3f t = Scale(Cross(q, value), 2f);
        return Add(value, Add(Scale(t, rotation.W), Cross(q, t)));
    }

    private static XrQuaternionf QuaternionFromEulerDegrees(
        float pitch,
        float yaw,
        float roll)
    {
        const float degreesToHalfRadians = MathF.PI / 360f;
        XrQuaternionf pitchRotation = new()
        {
            X = MathF.Sin(pitch * degreesToHalfRadians),
            W = MathF.Cos(pitch * degreesToHalfRadians)
        };
        XrQuaternionf yawRotation = new()
        {
            Y = MathF.Sin(yaw * degreesToHalfRadians),
            W = MathF.Cos(yaw * degreesToHalfRadians)
        };
        XrQuaternionf rollRotation = new()
        {
            Z = MathF.Sin(roll * degreesToHalfRadians),
            W = MathF.Cos(roll * degreesToHalfRadians)
        };
        return Multiply(Multiply(yawRotation, pitchRotation), rollRotation);
    }

    private static XrQuaternionf Multiply(XrQuaternionf left, XrQuaternionf right) => new()
    {
        X = (left.W * right.X) + (left.X * right.W) +
            (left.Y * right.Z) - (left.Z * right.Y),
        Y = (left.W * right.Y) - (left.X * right.Z) +
            (left.Y * right.W) + (left.Z * right.X),
        Z = (left.W * right.Z) + (left.X * right.Y) -
            (left.Y * right.X) + (left.Z * right.W),
        W = (left.W * right.W) - (left.X * right.X) -
            (left.Y * right.Y) - (left.Z * right.Z)
    };

    private static XrVector3f Cross(XrVector3f left, XrVector3f right) => new()
    {
        X = (left.Y * right.Z) - (left.Z * right.Y),
        Y = (left.Z * right.X) - (left.X * right.Z),
        Z = (left.X * right.Y) - (left.Y * right.X)
    };

    private static XrVector3f Add(XrVector3f left, XrVector3f right) => new()
    {
        X = left.X + right.X,
        Y = left.Y + right.Y,
        Z = left.Z + right.Z
    };

    private static XrVector3f Subtract(XrVector3f left, XrVector3f right) => new()
    {
        X = left.X - right.X,
        Y = left.Y - right.Y,
        Z = left.Z - right.Z
    };

    private static XrVector3f Scale(XrVector3f value, float scale) => new()
    {
        X = value.X * scale,
        Y = value.Y * scale,
        Z = value.Z * scale
    };

    private static float Dot(XrVector3f left, XrVector3f right) =>
        (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

    private static OpenXrStereoViewProbeRecord CreateStereoViewProbeRecord(
        int eyeIndex,
        XrView view)
    {
        const float radiansToDegrees = 180f / MathF.PI;
        return new OpenXrStereoViewProbeRecord
        {
            EyeIndex = eyeIndex,
            PositionX = view.Pose.Position.X,
            PositionY = view.Pose.Position.Y,
            PositionZ = view.Pose.Position.Z,
            OrientationX = view.Pose.Orientation.X,
            OrientationY = view.Pose.Orientation.Y,
            OrientationZ = view.Pose.Orientation.Z,
            OrientationW = view.Pose.Orientation.W,
            FovLeftDegrees = view.Fov.AngleLeft * radiansToDegrees,
            FovRightDegrees = view.Fov.AngleRight * radiansToDegrees,
            FovUpDegrees = view.Fov.AngleUp * radiansToDegrees,
            FovDownDegrees = view.Fov.AngleDown * radiansToDegrees
        };
    }

    private static OpenXrEyeState CreateStereoEyeState(XrView view) => new(
        view.Pose.Position.X,
        view.Pose.Position.Y,
        view.Pose.Position.Z,
        view.Pose.Orientation.X,
        view.Pose.Orientation.Y,
        view.Pose.Orientation.Z,
        view.Pose.Orientation.W,
        view.Fov.AngleLeft,
        view.Fov.AngleRight,
        view.Fov.AngleUp,
        view.Fov.AngleDown);

    private static XrView CreateXrView(OpenXrEyeState eye) => new()
    {
        Type = 7,
        Pose = new XrPosef
        {
            Position = new XrVector3f
            {
                X = eye.PositionX,
                Y = eye.PositionY,
                Z = eye.PositionZ
            },
            Orientation = new XrQuaternionf
            {
                X = eye.OrientationX,
                Y = eye.OrientationY,
                Z = eye.OrientationZ,
                W = eye.OrientationW
            }
        },
        Fov = new XrFovf
        {
            AngleLeft = eye.FovLeft,
            AngleRight = eye.FovRight,
            AngleUp = eye.FovUp,
            AngleDown = eye.FovDown
        }
    };

    private static EyeSwapchainResources CreateEyeSwapchainResources(
        IntPtr session,
        D3D11Texture2DDescription sourceDescription,
        EnumerateSwapchainFormatsDelegate enumerateFormats,
        CreateSwapchainDelegate createSwapchain,
        EnumerateSwapchainImagesDelegate enumerateImages,
        DestroySwapchainDelegate destroySwapchain)
    {
        ValidateGameBackBuffer(sourceDescription);
        long format = ChooseSwapchainFormat(enumerateFormats, session, sourceDescription.Format);
        XrSwapchainCreateInfo createInfo = new()
        {
            Type = 9,
            UsageFlags = 1 | 16,
            Format = format,
            SampleCount = 1,
            Width = sourceDescription.Width,
            Height = sourceDescription.Height,
            FaceCount = 1,
            ArraySize = 1,
            MipCount = 1
        };
        IntPtr swapchain = IntPtr.Zero;
        IntPtr imageBuffer = IntPtr.Zero;
        try
        {
            Check(createSwapchain(session, ref createInfo, out swapchain),
                "create stereo eye swapchain");
            IReadOnlyList<IntPtr> images = EnumerateSwapchainTextures(
                enumerateImages,
                swapchain,
                out imageBuffer);
            return new EyeSwapchainResources
            {
                Swapchain = swapchain,
                ImageBuffer = imageBuffer,
                Images = images,
                Width = sourceDescription.Width,
                Height = sourceDescription.Height,
                Format = format
            };
        }
        catch
        {
            if (imageBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(imageBuffer);
            }
            if (swapchain != IntPtr.Zero)
            {
                _ = destroySwapchain(swapchain);
            }
            throw;
        }
    }

    private static long CopyStereoPairToSwapchains(
        IntPtr context,
        IntPtr gpuCompletionQuery,
        EyeSwapchainResources leftResources,
        IntPtr leftSourceTexture,
        D3D11Texture2DDescription leftSourceDescription,
        EyeSwapchainResources rightResources,
        IntPtr rightSourceTexture,
        D3D11Texture2DDescription rightSourceDescription,
        D3D11VerticalBlitter verticalBlitter,
        AcquireSwapchainImageDelegate acquireImage,
        WaitSwapchainImageDelegate waitImage,
        ReleaseSwapchainImageDelegate releaseImage)
    {
        uint leftImageIndex = 0;
        uint rightImageIndex = 0;
        bool leftAcquired = false;
        bool rightAcquired = false;
        try
        {
            XrSwapchainImageAcquireInfo leftAcquireInfo = new() { Type = 55 };
            Check(acquireImage(
                    leftResources.Swapchain,
                    ref leftAcquireInfo,
                    out leftImageIndex),
                "acquire left stereo eye image");
            leftAcquired = true;
            XrSwapchainImageWaitInfo leftWaitInfo = new()
            {
                Type = 56,
                Timeout = long.MaxValue
            };
            Check(waitImage(leftResources.Swapchain, ref leftWaitInfo),
                "wait for left stereo eye image");

            XrSwapchainImageAcquireInfo rightAcquireInfo = new() { Type = 55 };
            Check(acquireImage(
                    rightResources.Swapchain,
                    ref rightAcquireInfo,
                    out rightImageIndex),
                "acquire right stereo eye image");
            rightAcquired = true;
            XrSwapchainImageWaitInfo rightWaitInfo = new()
            {
                Type = 56,
                Timeout = long.MaxValue
            };
            Check(waitImage(rightResources.Swapchain, ref rightWaitInfo),
                "wait for right stereo eye image");

            if (leftImageIndex >= leftResources.Images.Count ||
                rightImageIndex >= rightResources.Images.Count)
            {
                throw new InvalidOperationException(
                    "Invalid stereo eye image index: " +
                    $"left={leftImageIndex}/{leftResources.Images.Count};" +
                    $"right={rightImageIndex}/{rightResources.Images.Count}.");
            }
            verticalBlitter.BlitFlipped(
                context,
                leftResources.Images[checked((int)leftImageIndex)],
                leftSourceTexture,
                leftSourceDescription.Width,
                leftSourceDescription.Height,
                leftSourceDescription.Format,
                brightenVrEye: true);
            verticalBlitter.BlitFlipped(
                context,
                rightResources.Images[checked((int)rightImageIndex)],
                rightSourceTexture,
                rightSourceDescription.Width,
                rightSourceDescription.Height,
                rightSourceDescription.Format,
                brightenVrEye: true);
            long gpuWaitStartedTimestamp = Stopwatch.GetTimestamp();
            D3D11Interop.WaitForGpuLowLatency(
                context,
                gpuCompletionQuery,
                2_000);
            return Stopwatch.GetTimestamp() - gpuWaitStartedTimestamp;
        }
        finally
        {
            try
            {
                if (rightAcquired)
                {
                    XrSwapchainImageReleaseInfo rightReleaseInfo = new() { Type = 57 };
                    Check(releaseImage(rightResources.Swapchain, ref rightReleaseInfo),
                        "release right stereo eye image");
                }
            }
            finally
            {
                if (leftAcquired)
                {
                    XrSwapchainImageReleaseInfo leftReleaseInfo = new() { Type = 57 };
                    Check(releaseImage(leftResources.Swapchain, ref leftReleaseInfo),
                        "release left stereo eye image");
                }
            }
        }
    }

    private static XrSwapchainSubImage CreateEyeSubImage(EyeSwapchainResources resources) =>
        new()
        {
            Swapchain = resources.Swapchain,
            ImageRect = new XrRect2Di
            {
                Extent = new XrExtent2Di
                {
                    Width = checked((int)resources.Width),
                    Height = checked((int)resources.Height)
                }
            },
            ImageArrayIndex = 0
        };

    private static void DestroyEyeSwapchainResources(
        EyeSwapchainResources? resources,
        DestroySwapchainDelegate destroySwapchain)
    {
        if (resources is null || resources.Destroyed)
        {
            return;
        }
        resources.Destroyed = true;
        if (resources.ImageBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(resources.ImageBuffer);
        }
        if (resources.Swapchain != IntPtr.Zero)
        {
            _ = destroySwapchain(resources.Swapchain);
        }
    }

    private static PanelSwapchainResources CreatePanelSwapchainResources(
        IntPtr session,
        IntPtr space,
        D3D11Texture2DDescription sourceDescription,
        EnumerateSwapchainFormatsDelegate enumerateFormats,
        CreateSwapchainDelegate createSwapchain,
        EnumerateSwapchainImagesDelegate enumerateImages,
        DestroySwapchainDelegate destroySwapchain,
        bool alphaLayer = false,
        bool handAttached = false)
    {
        ValidateGameBackBuffer(sourceDescription);
        long format = ChooseSwapchainFormat(enumerateFormats, session, sourceDescription.Format);
        XrSwapchainCreateInfo createInfo = new()
        {
            Type = 9,
            UsageFlags = 1 | 16,
            Format = format,
            SampleCount = 1,
            Width = sourceDescription.Width,
            Height = sourceDescription.Height,
            FaceCount = 1,
            ArraySize = 1,
            MipCount = 1
        };
        IntPtr swapchain = IntPtr.Zero;
        IntPtr imageBuffer = IntPtr.Zero;
        try
        {
            Check(createSwapchain(session, ref createInfo, out swapchain), "create game panel swapchain");
            IReadOnlyList<IntPtr> images = EnumerateSwapchainTextures(
                enumerateImages,
                swapchain,
                out imageBuffer);

            float aspectRatio = sourceDescription.Width / (float)sourceDescription.Height;
            float maximumPanelWidth = handAttached ? 0.42f : 1.8f;
            float maximumPanelHeight = handAttached ? 0.42f : 1.3f;
            float physicalWidth = maximumPanelWidth;
            float physicalHeight = physicalWidth / aspectRatio;
            if (physicalHeight > maximumPanelHeight)
            {
                physicalHeight = maximumPanelHeight;
                physicalWidth = physicalHeight * aspectRatio;
            }

            XrPosef panelPose = IdentityPose();
            if (!handAttached)
            {
                panelPose.Position.Z = alphaLayer ? -1.58f : -1.6f;
            }
            XrCompositionLayerQuad layer = new()
            {
                Type = 36,
                LayerFlags = alphaLayer ? 2UL : 0UL,
                Space = space,
                EyeVisibility = 0,
                SubImage = new XrSwapchainSubImage
                {
                    Swapchain = swapchain,
                    ImageRect = new XrRect2Di
                    {
                        Extent = new XrExtent2Di
                        {
                            Width = checked((int)sourceDescription.Width),
                            Height = checked((int)sourceDescription.Height)
                        }
                    },
                    ImageArrayIndex = 0
                },
                Pose = panelPose,
                Size = new XrExtent2Df
                {
                    Width = physicalWidth,
                    Height = physicalHeight
                }
            };

            return new PanelSwapchainResources
            {
                Swapchain = swapchain,
                ImageBuffer = imageBuffer,
                Images = images,
                Width = sourceDescription.Width,
                Height = sourceDescription.Height,
                Format = format,
                TextureDescription = D3D11Interop.DescribeTexture(images[0]),
                Layer = layer
            };
        }
        catch
        {
            if (imageBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(imageBuffer);
            }

            if (swapchain != IntPtr.Zero)
            {
                _ = destroySwapchain(swapchain);
            }

            throw;
        }
    }

    private static PanelSwapchainResources CreateCursorSwapchainResources(
        IntPtr session,
        IntPtr space,
        D3D11Texture2DDescription sourceDescription,
        EnumerateSwapchainFormatsDelegate enumerateFormats,
        CreateSwapchainDelegate createSwapchain,
        EnumerateSwapchainImagesDelegate enumerateImages,
        DestroySwapchainDelegate destroySwapchain)
    {
        D3D11Texture2DDescription cursorDescription = new()
        {
            Width = 32,
            Height = 32,
            MipLevels = 1,
            ArraySize = 1,
            Format = sourceDescription.Format,
            SampleCount = 1
        };
        return CreatePanelSwapchainResources(
            session,
            space,
            cursorDescription,
            enumerateFormats,
            createSwapchain,
            enumerateImages,
            destroySwapchain,
            alphaLayer: true);
    }

    private static void UpdateCursorSwapchain(
        IntPtr context,
        IntPtr gpuCompletionQuery,
        PanelSwapchainResources cursor,
        AcquireSwapchainImageDelegate acquireImage,
        WaitSwapchainImageDelegate waitImage,
        ReleaseSwapchainImageDelegate releaseImage)
    {
        XrSwapchainImageAcquireInfo acquireInfo = new() { Type = 55 };
        Check(
            acquireImage(cursor.Swapchain, ref acquireInfo, out uint imageIndex),
            "acquire pointer cursor image");
        XrSwapchainImageWaitInfo waitInfo = new()
        {
            Type = 56,
            Timeout = long.MaxValue
        };
        Check(waitImage(cursor.Swapchain, ref waitInfo), "wait for pointer cursor image");
        try
        {
            if (imageIndex >= cursor.Images.Count)
            {
                throw new InvalidOperationException(
                    $"Invalid pointer cursor image index: {imageIndex}.");
            }

            D3D11Interop.UpdateTexture(
                context,
                cursor.Images[checked((int)imageIndex)],
                0,
                CursorPixels,
                32,
                32);
            D3D11Interop.WaitForGpuLowLatency(context, gpuCompletionQuery, 2_000);
        }
        finally
        {
            XrSwapchainImageReleaseInfo releaseInfo = new() { Type = 57 };
            Check(
                releaseImage(cursor.Swapchain, ref releaseInfo),
                "release pointer cursor image");
        }
    }

    private static byte[] CreateCursorPixels()
    {
        const int size = 32;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - (size * 0.5f);
                float dy = y + 0.5f - (size * 0.5f);
                float radiusSquared = (dx * dx) + (dy * dy);
                int offset = ((y * size) + x) * 4;
                bool outer = radiusSquared <= 14f * 14f;
                bool inner = radiusSquared <= 10f * 10f;
                bool center = radiusSquared <= 2.5f * 2.5f;
                byte color = inner && !center ? (byte)255 : (byte)24;
                pixels[offset] = color;
                pixels[offset + 1] = color;
                pixels[offset + 2] = color;
                pixels[offset + 3] = outer ? (byte)235 : (byte)0;
            }
        }
        return pixels;
    }

    private static void SaveUiDiagnosticTexture(
        IntPtr device,
        IntPtr context,
        IntPtr liveUiTexture)
    {
        string gameRoot = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)
            ?? Directory.GetCurrentDirectory();
        string logDirectory = Path.Combine(gameRoot, "vrmod", "logs");
        D3D11Interop.SaveTextureBmp(
            device,
            context,
            liveUiTexture,
            Path.Combine(
                logDirectory,
                $"v{RuntimeProbe.BootstrapVersion}-ui-natural-capture.bmp"));
    }

    private static void DestroyPanelSwapchainResources(
        PanelSwapchainResources? resources,
        DestroySwapchainDelegate destroySwapchain)
    {
        if (resources is null || resources.Destroyed)
        {
            return;
        }

        resources.Destroyed = true;
        if (resources.ImageBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(resources.ImageBuffer);
        }

        if (resources.Swapchain != IntPtr.Zero)
        {
            _ = destroySwapchain(resources.Swapchain);
        }
    }

    private static void ValidateGameBackBuffer(D3D11Texture2DDescription description)
    {
        if (description.Width == 0 || description.Height == 0 ||
            description.MipLevels != 1 || description.ArraySize != 1 ||
            description.SampleCount != 1)
        {
            throw new NotSupportedException(
                "The game backbuffer is not a copy-compatible single-sample 2D texture: " +
                $"{description.Width}x{description.Height};mips={description.MipLevels};" +
                $"array={description.ArraySize};samples={description.SampleCount}.");
        }
    }

    private static bool AreCopyCompatibleFormats(int left, int right)
    {
        if (left == right)
        {
            return true;
        }

        int leftFamily = left switch
        {
            27 or 28 or 29 => 1,
            87 or 90 or 91 => 2,
            _ => 0
        };
        int rightFamily = right switch
        {
            27 or 28 or 29 => 1,
            87 or 90 or 91 => 2,
            _ => 0
        };
        return leftFamily != 0 && leftFamily == rightFamily;
    }

    private static bool IsSupportedPanelSourceFormat(int format) =>
        format is 27 or 28 or 29 or 87 or 90 or 91;

    private static long ChooseSwapchainFormat(
        EnumerateSwapchainFormatsDelegate enumerate,
        IntPtr session,
        int sourceFormat)
    {
        Check(enumerate(session, 0, out uint count, IntPtr.Zero), "count OpenXR swapchain formats");
        if (count == 0 || count > 256)
        {
            throw new InvalidOperationException($"Invalid OpenXR swapchain format count: {count}.");
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)count * sizeof(long)));
        try
        {
            Check(enumerate(session, count, out uint written, buffer), "enumerate OpenXR swapchain formats");
            List<long> formats = new(checked((int)written));
            for (uint index = 0; index < written; index++)
            {
                formats.Add(Marshal.ReadInt64(buffer, checked((int)index * sizeof(long))));
            }

            long[] preferred = sourceFormat switch
            {
                29 => new long[] { 29, 28 },
                28 or 27 => new long[] { 29, 28 },
                91 => new long[] { 91, 87 },
                87 or 90 => new long[] { 91, 87 },
                _ => new long[] { 29, 28, 91, 87 }
            };
            foreach (long candidate in preferred)
            {
                if (formats.Contains(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                $"No supported 8-bit RGBA OpenXR swapchain format was found: {string.Join(",", formats)}.");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<IntPtr> EnumerateSwapchainTextures(
        EnumerateSwapchainImagesDelegate enumerate,
        IntPtr swapchain,
        out IntPtr buffer)
    {
        Check(enumerate(swapchain, 0, out uint count, IntPtr.Zero), "count OpenXR panel images");
        if (count == 0 || count > 16)
        {
            throw new InvalidOperationException($"Invalid OpenXR panel image count: {count}.");
        }

        int elementSize = Marshal.SizeOf<XrSwapchainImageD3D11>();
        buffer = Marshal.AllocHGlobal(checked((int)count * elementSize));
        for (uint index = 0; index < count; index++)
        {
            Marshal.StructureToPtr(
                new XrSwapchainImageD3D11 { Type = 1000027001 },
                IntPtr.Add(buffer, checked((int)index * elementSize)),
                fDeleteOld: false);
        }

        Check(enumerate(swapchain, count, out uint written, buffer), "enumerate OpenXR panel images");
        List<IntPtr> textures = new(checked((int)written));
        for (uint index = 0; index < written; index++)
        {
            XrSwapchainImageD3D11 image = Marshal.PtrToStructure<XrSwapchainImageD3D11>(
                IntPtr.Add(buffer, checked((int)index * elementSize)));
            if (image.Texture == IntPtr.Zero)
            {
                throw new InvalidOperationException($"OpenXR panel image {index} has a null texture.");
            }

            textures.Add(image.Texture);
        }

        return textures;
    }

    private static XrPosef IdentityPose() => new()
    {
        Orientation = new XrQuaternionf { W = 1f }
    };

    private static byte[] CreatePanelPattern(uint width, uint height)
    {
        const uint tileSize = 120;
        byte[] pixels = new byte[checked((int)(width * height * 4))];
        for (uint y = 0; y < height; y++)
        {
            for (uint x = 0; x < width; x++)
            {
                bool magenta = ((x / tileSize) + (y / tileSize)) % 2 != 0;
                int offset = checked((int)(((y * width) + x) * 4));
                pixels[offset] = magenta ? (byte)255 : (byte)0;
                pixels[offset + 1] = magenta ? (byte)0 : (byte)255;
                pixels[offset + 2] = magenta ? (byte)204 : (byte)255;
                pixels[offset + 3] = 255;
            }
        }

        return pixels;
    }

    private static XrEventDataBuffer NewEventBuffer() => new()
    {
        Type = 16,
        Varying = new byte[4000]
    };

    private static int ReadSessionState(XrEventDataBuffer eventData) =>
        BitConverter.ToInt32(eventData.Varying, IntPtr.Size);

    private static TDelegate LoadExport<TDelegate>(IntPtr library, string name)
        where TDelegate : Delegate =>
        Marshal.GetDelegateForFunctionPointer<TDelegate>(NativeLibrary.GetExport(library, name));

    private static byte[] FixedUtf8(string value, int capacity)
    {
        byte[] buffer = new byte[capacity];
        int length = Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
        if (length >= capacity)
        {
            throw new ArgumentException($"UTF-8 value exceeds fixed capacity {capacity}.", nameof(value));
        }

        return buffer;
    }

    private static string DecodeFixedUtf8(byte[] value)
    {
        int length = Array.IndexOf(value, (byte)0);
        return Encoding.UTF8.GetString(value, 0, length < 0 ? value.Length : length);
    }

    private static ulong MakeVersion(ushort major, ushort minor, uint patch) =>
        ((ulong)major << 48) | ((ulong)minor << 32) | patch;

    private static string FormatVersion(ulong version) =>
        $"{(version >> 48) & 0xffff}.{(version >> 32) & 0xffff}.{version & 0xffffffff}";

    private static string ReadActiveRuntimeManifest()
    {
        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Khronos\OpenXR\1");
        string? path = key?.GetValue("ActiveRuntime") as string;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("No 64-bit OpenXR ActiveRuntime is registered.");
        }

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The active OpenXR runtime manifest is missing.", fullPath);
        }

        return fullPath;
    }

    private static string ReadRuntimeName(string manifestPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (document.RootElement.TryGetProperty("runtime", out JsonElement runtime) &&
            runtime.TryGetProperty("name", out JsonElement name))
        {
            return name.GetString() ?? string.Empty;
        }

        return Path.GetFileNameWithoutExtension(manifestPath);
    }

    private static string FindLoader()
    {
        string gameRoot = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName)
            ?? Directory.GetCurrentDirectory();
        string[] candidates =
        {
            Path.Combine(gameRoot, "vrmod", "runtime", "openxr_loader.dll"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Steam",
                "steamapps",
                "common",
                "SteamVR",
                "bin",
                "win64",
                "openxr_loader.dll")
        };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException(
            "No OpenXR loader was found in the mod runtime or the SteamVR fallback path.");
    }

    private static IReadOnlyList<string> EnumerateExtensions(
        EnumerateInstanceExtensionPropertiesDelegate enumerate)
    {
        Check(enumerate(IntPtr.Zero, 0, out uint count, IntPtr.Zero), "count OpenXR extensions");
        if (count == 0 || count > 4096)
        {
            throw new InvalidOperationException($"Invalid OpenXR extension count: {count}.");
        }

        int elementSize = Marshal.SizeOf<XrExtensionProperties>();
        IntPtr buffer = Marshal.AllocHGlobal(checked(elementSize * (int)count));
        try
        {
            for (uint index = 0; index < count; index++)
            {
                Marshal.StructureToPtr(
                    new XrExtensionProperties
                    {
                        Type = XrTypeExtensionProperties,
                        ExtensionName = new byte[MaxExtensionNameSize]
                    },
                    IntPtr.Add(buffer, checked((int)index * elementSize)),
                    fDeleteOld: false);
            }

            Check(enumerate(IntPtr.Zero, count, out uint written, buffer), "enumerate OpenXR extensions");
            List<string> names = new(checked((int)written));
            for (uint index = 0; index < written; index++)
            {
                XrExtensionProperties property = Marshal.PtrToStructure<XrExtensionProperties>(
                    IntPtr.Add(buffer, checked((int)index * elementSize)));
                int length = Array.IndexOf(property.ExtensionName, (byte)0);
                if (length < 0)
                {
                    length = property.ExtensionName.Length;
                }

                string name = Encoding.UTF8.GetString(property.ExtensionName, 0, length);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            names.Sort(StringComparer.Ordinal);
            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Check(int result, string operation)
    {
        if (result != XrSuccess)
        {
            throw new InvalidOperationException($"Failed to {operation}: XrResult={result}.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrExtensionProperties
    {
        public int Type;

        public IntPtr Next;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxExtensionNameSize)]
        public byte[] ExtensionName;

        public uint ExtensionVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrApplicationInfo
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxApplicationNameSize)]
        public byte[] ApplicationName;

        public uint ApplicationVersion;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxEngineNameSize)]
        public byte[] EngineName;

        public uint EngineVersion;

        public ulong ApiVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrInstanceCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public XrApplicationInfo ApplicationInfo;
        public uint EnabledApiLayerCount;
        public IntPtr EnabledApiLayerNames;
        public uint EnabledExtensionCount;
        public IntPtr EnabledExtensionNames;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrInstanceProperties
    {
        public int Type;
        public IntPtr Next;
        public ulong RuntimeVersion;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxRuntimeNameSize)]
        public byte[] RuntimeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSystemGetInfo
    {
        public int Type;
        public IntPtr Next;
        public int FormFactor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSystemGraphicsProperties
    {
        public uint MaxSwapchainImageHeight;
        public uint MaxSwapchainImageWidth;
        public uint MaxLayerCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSystemTrackingProperties
    {
        public uint OrientationTracking;
        public uint PositionTracking;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSystemProperties
    {
        public int Type;
        public IntPtr Next;
        public ulong SystemId;
        public uint VendorId;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = MaxSystemNameSize)]
        public byte[] SystemName;

        public XrSystemGraphicsProperties GraphicsProperties;
        public XrSystemTrackingProperties TrackingProperties;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrGraphicsRequirementsD3D11
    {
        public int Type;
        public IntPtr Next;
        public Luid AdapterLuid;
        public int MinFeatureLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrViewConfigurationView
    {
        public int Type;
        public IntPtr Next;
        public uint RecommendedImageRectWidth;
        public uint MaxImageRectWidth;
        public uint RecommendedImageRectHeight;
        public uint MaxImageRectHeight;
        public uint RecommendedSwapchainSampleCount;
        public uint MaxSwapchainSampleCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrGraphicsBindingD3D11
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSessionCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public ulong SystemId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrEventDataBuffer
    {
        public int Type;
        public IntPtr Next;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4000)]
        public byte[] Varying;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSessionBeginInfo
    {
        public int Type;
        public IntPtr Next;
        public int PrimaryViewConfigurationType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameWaitInfo
    {
        public int Type;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameState
    {
        public int Type;
        public IntPtr Next;
        public long PredictedDisplayTime;
        public long PredictedDisplayPeriod;
        public uint ShouldRender;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameBeginInfo
    {
        public int Type;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFrameEndInfo
    {
        public int Type;
        public IntPtr Next;
        public long DisplayTime;
        public int EnvironmentBlendMode;
        public uint LayerCount;
        public IntPtr Layers;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong CreateFlags;
        public ulong UsageFlags;
        public long Format;
        public uint SampleCount;
        public uint Width;
        public uint Height;
        public uint FaceCount;
        public uint ArraySize;
        public uint MipCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageD3D11
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Texture;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrQuaternionf
    {
        public float X;
        public float Y;
        public float Z;
        public float W;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrVector3f
    {
        public float X;
        public float Y;
        public float Z;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrPosef
    {
        public XrQuaternionf Orientation;
        public XrVector3f Position;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrFovf
    {
        public float AngleLeft;
        public float AngleRight;
        public float AngleUp;
        public float AngleDown;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrViewLocateInfo
    {
        public int Type;
        public IntPtr Next;
        public int ViewConfigurationType;
        public long DisplayTime;
        public IntPtr Space;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrViewState
    {
        public int Type;
        public IntPtr Next;
        public ulong ViewStateFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrView
    {
        public int Type;
        public IntPtr Next;
        public XrPosef Pose;
        public XrFovf Fov;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrReferenceSpaceCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public int ReferenceSpaceType;
        public XrPosef PoseInReferenceSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrOffset2Di
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrExtent2Di
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrRect2Di
    {
        public XrOffset2Di Offset;
        public XrExtent2Di Extent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainSubImage
    {
        public IntPtr Swapchain;
        public XrRect2Di ImageRect;
        public uint ImageArrayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrExtent2Df
    {
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrCompositionLayerQuad
    {
        public int Type;
        public IntPtr Next;
        public ulong LayerFlags;
        public IntPtr Space;
        public int EyeVisibility;
        public XrSwapchainSubImage SubImage;
        public XrPosef Pose;
        public XrExtent2Df Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrCompositionLayerProjectionView
    {
        public int Type;
        public IntPtr Next;
        public XrPosef Pose;
        public XrFovf Fov;
        public XrSwapchainSubImage SubImage;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrCompositionLayerProjection
    {
        public int Type;
        public IntPtr Next;
        public ulong LayerFlags;
        public IntPtr Space;
        public uint ViewCount;
        public IntPtr Views;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageAcquireInfo
    {
        public int Type;
        public IntPtr Next;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageWaitInfo
    {
        public int Type;
        public IntPtr Next;
        public long Timeout;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSwapchainImageReleaseInfo
    {
        public int Type;
        public IntPtr Next;
    }

    private sealed class OpenXrInstanceResult
    {
        public string RuntimeName { get; init; } = string.Empty;
        public string RuntimeVersion { get; init; } = string.Empty;
        public int HmdSystemResult { get; init; }
        public bool HmdSystemAvailable { get; init; }
        public string SystemName { get; init; } = string.Empty;
        public uint VendorId { get; init; }
        public uint MaxSwapchainWidth { get; init; }
        public uint MaxSwapchainHeight { get; init; }
        public uint MaxLayerCount { get; init; }
        public bool OrientationTracking { get; init; }
        public bool PositionTracking { get; init; }
        public string RequiredAdapterLuid { get; init; } = string.Empty;
        public string MinD3DFeatureLevel { get; init; } = string.Empty;
        public uint ViewCount { get; init; }
        public uint RecommendedViewWidth { get; init; }
        public uint RecommendedViewHeight { get; init; }
        public uint RecommendedSampleCount { get; init; }
        public int SessionCreateResult { get; init; }
        public bool SessionCreated { get; init; }
        public bool SessionReadyObserved { get; init; }
        public int EmptyFramesSubmitted { get; init; }
        public int TestPatternFramesSubmitted { get; init; }
        public int TestPatternLayerFramesSubmitted { get; init; }
        public uint TestPatternWidth { get; init; }
        public uint TestPatternHeight { get; init; }
        public long TestPatternFormat { get; init; }
        public string TestPatternTextureDescription { get; init; } = string.Empty;
        public string TestPatternPixelReadback { get; init; } = string.Empty;
        public string FrameLoopStage { get; init; } = string.Empty;
        public int FrameLoopResult { get; init; }
    }

    private sealed class ViewConfigurationResult
    {
        public uint ViewCount { get; init; }
        public uint RecommendedWidth { get; init; }
        public uint RecommendedHeight { get; init; }
        public uint RecommendedSampleCount { get; init; }
    }

    private sealed class PanelSwapchainResources
    {
        public IntPtr Swapchain { get; init; }
        public IntPtr ImageBuffer { get; init; }
        public IReadOnlyList<IntPtr> Images { get; init; } = Array.Empty<IntPtr>();
        public uint Width { get; init; }
        public uint Height { get; init; }
        public long Format { get; init; }
        public string TextureDescription { get; init; } = string.Empty;
        public XrCompositionLayerQuad Layer { get; set; }
        public bool Destroyed { get; set; }

        public bool Matches(D3D11Texture2DDescription source) =>
            source.Width == Width &&
            source.Height == Height &&
            AreCopyCompatibleFormats(source.Format, checked((int)Format));
    }

    private sealed class EyeSwapchainResources
    {
        public IntPtr Swapchain { get; init; }
        public IntPtr ImageBuffer { get; init; }
        public IReadOnlyList<IntPtr> Images { get; init; } = Array.Empty<IntPtr>();
        public uint Width { get; init; }
        public uint Height { get; init; }
        public long Format { get; init; }
        public bool Destroyed { get; set; }

        public bool Matches(D3D11Texture2DDescription source) =>
            source.Width == Width &&
            source.Height == Height &&
            AreCopyCompatibleFormats(source.Format, checked((int)Format));
    }

    private sealed class SessionProbeResult
    {
        public int CreateResult { get; init; }
        public bool Created { get; init; }
        public bool ReadyObserved { get; init; }
        public int FramesSubmitted { get; init; }
        public int LayerFramesSubmitted { get; init; }
        public uint TestPatternWidth { get; init; }
        public uint TestPatternHeight { get; init; }
        public long TestPatternFormat { get; init; }
        public string TestPatternTextureDescription { get; init; } = string.Empty;
        public string TestPatternPixelReadback { get; init; } = string.Empty;
        public string FrameLoopStage { get; init; } = string.Empty;
        public int FrameLoopResult { get; init; }
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumerateInstanceExtensionPropertiesDelegate(
        IntPtr layerName,
        uint propertyCapacityInput,
        out uint propertyCountOutput,
        IntPtr properties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateInstanceDelegate(ref XrInstanceCreateInfo createInfo, out IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroyInstanceDelegate(IntPtr instance);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetInstancePropertiesDelegate(IntPtr instance, ref XrInstanceProperties properties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSystemDelegate(IntPtr instance, ref XrSystemGetInfo getInfo, out ulong systemId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetSystemPropertiesDelegate(
        IntPtr instance,
        ulong systemId,
        ref XrSystemProperties properties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetInstanceProcAddrDelegate(
        IntPtr instance,
        IntPtr name,
        out IntPtr function);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetD3D11GraphicsRequirementsDelegate(
        IntPtr instance,
        ulong systemId,
        ref XrGraphicsRequirementsD3D11 requirements);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumerateViewConfigurationViewsDelegate(
        IntPtr instance,
        ulong systemId,
        int viewConfigurationType,
        uint viewCapacityInput,
        out uint viewCountOutput,
        IntPtr views);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateSessionDelegate(
        IntPtr instance,
        ref XrSessionCreateInfo createInfo,
        out IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroySessionDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PollEventDelegate(IntPtr instance, ref XrEventDataBuffer eventData);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BeginSessionDelegate(IntPtr session, ref XrSessionBeginInfo beginInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EndSessionDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int RequestExitSessionDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WaitFrameDelegate(IntPtr session, ref XrFrameWaitInfo waitInfo, ref XrFrameState frameState);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BeginFrameDelegate(IntPtr session, ref XrFrameBeginInfo frameBeginInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EndFrameDelegate(IntPtr session, ref XrFrameEndInfo frameEndInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumerateSwapchainFormatsDelegate(
        IntPtr session,
        uint formatCapacityInput,
        out uint formatCountOutput,
        IntPtr formats);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateSwapchainDelegate(
        IntPtr session,
        ref XrSwapchainCreateInfo createInfo,
        out IntPtr swapchain);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroySwapchainDelegate(IntPtr swapchain);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumerateSwapchainImagesDelegate(
        IntPtr swapchain,
        uint imageCapacityInput,
        out uint imageCountOutput,
        IntPtr images);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int AcquireSwapchainImageDelegate(
        IntPtr swapchain,
        ref XrSwapchainImageAcquireInfo acquireInfo,
        out uint index);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WaitSwapchainImageDelegate(
        IntPtr swapchain,
        ref XrSwapchainImageWaitInfo waitInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int ReleaseSwapchainImageDelegate(
        IntPtr swapchain,
        ref XrSwapchainImageReleaseInfo releaseInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateReferenceSpaceDelegate(
        IntPtr session,
        ref XrReferenceSpaceCreateInfo createInfo,
        out IntPtr space);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroySpaceDelegate(IntPtr space);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int LocateViewsDelegate(
        IntPtr session,
        ref XrViewLocateInfo viewLocateInfo,
        ref XrViewState viewState,
        uint viewCapacityInput,
        out uint viewCountOutput,
        IntPtr views);
}
