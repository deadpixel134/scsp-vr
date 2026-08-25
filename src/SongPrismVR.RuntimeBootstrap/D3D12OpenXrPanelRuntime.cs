using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using SongPrismVR.Core;

namespace Doorstop;

internal sealed class D3D12OpenXrPanelResult
{
    public bool InstanceCreated { get; init; }
    public bool SessionCreated { get; init; }
    public bool PanelCreated { get; init; }
    public int FrameLoopResult { get; init; }
    public int FramesSubmitted { get; init; }
    public string Stage { get; init; } = string.Empty;
    public string? Error { get; init; }
}

internal sealed class D3D12OpenXrRequirements
{
    public uint AdapterLuidLowPart { get; init; }
    public int AdapterLuidHighPart { get; init; }
    public int MinFeatureLevel { get; init; }

    public string AdapterLuidText =>
        $"0x{AdapterLuidHighPart:x8}:{AdapterLuidLowPart:x8}";
}

internal static class D3D12OpenXrPanelRuntime
{
    private static readonly object LogLock = new();
    private static DateTimeOffset _nextStereoViewDiagnosticUtc = DateTimeOffset.MinValue;
    private static long _observedEyeCopyGeneration;
    private static ulong _observedEyeCopySequence;
    private static uint _observedEyeCopyFlags;
    private static bool _observedManagedPollFailureLogged;
    private static DrawPanelCursorDelegate? _drawPanelCursor;
    private static bool _drawPanelCursorLoadAttempted;
    private const float PanelCursorRelativeSize = 0.045f;
    private const uint EyeTraceDelivered = 0x00000001;
    private const uint EyeTraceCallbackAcquired = 0x00000002;
    private const uint EyeTraceCommandReady = 0x00000004;
    private const uint EyeTraceExecuteBefore = 0x00000008;
    private const uint EyeTraceExecuteReturned = 0x00000010;
    private const uint EyeTraceSignalResult = 0x00000020;
    private const uint EyeTraceFenceFirstObserved = 0x00000040;
    private const uint EyeTraceFenceTerminal = 0x00000080;
    private const uint EyeTraceFailureOrQuarantine = 0x00000100;
    private const int XrSuccess = 0;
    private const int XrSessionNotFocused = -2;
    private const int XrEventUnavailable = 4;
    private const int XrTypeExtensionProperties = 2;
    private const int XrTypeInstanceCreateInfo = 3;
    private const int XrTypeSystemGetInfo = 4;
    private const int XrTypeSystemProperties = 5;
    private const int XrTypeInstanceProperties = 32;
    private const int XrTypeViewLocateInfo = 6;
    private const int XrTypeView = 7;
    private const int XrTypeViewState = 11;
    private const int XrTypeReferenceSpaceCreateInfo = 37;
    private const int XrTypeViewConfigurationView = 41;
    private const int XrTypeSwapchainCreateInfo = 9;
    private const int XrTypeEventDataBuffer = 16;
    private const int XrTypeEventDataSessionStateChanged = 18;
    private const int XrTypeCompositionLayerQuad = 36;
    private const int XrEnvironmentBlendModeOpaque = 1;
    private const int XrEnvironmentBlendModeAlphaBlend = 3;
    private const int XrTypeCompositionLayerProjection = 35;
    private const int XrTypeCompositionLayerProjectionView = 48;
    private const int XrTypeFrameEndInfo = 12;
    private const int XrTypeFrameBeginInfo = 46;
    private const int XrTypeGraphicsBindingD3D12Khr = 1000028000;
    private const int XrTypeSwapchainImageD3D12Khr = 1000028001;
    private const int XrTypeGraphicsRequirementsD3D12Khr = 1000028002;
    private const int XrTypeDebugUtilsMessengerCreateInfoExt = 1000048000;
    private const int XrSessionStateReady = 2;
    private const int XrSessionStateStopping = 6;
    private const int XrSessionStateLossPending = 7;
    private const int XrSessionStateExiting = 8;
    private const int XrFormFactorHeadMountedDisplay = 1;
    private const int XrReferenceSpaceLocal = 0;
    private const int XrReferenceSpaceView = 1;
    private const int XrReferenceSpaceStage = 2;
    private const int XrPrimaryStereoViewConfiguration = 2;
    private const int XrSwapchainUsageColorAttachment = 1;
    private const int XrSwapchainUsageTransferDst = 16;
    private const ulong XrCompositionLayerBlendTextureSourceAlphaBit = 1;
    private const ulong XrCompositionLayerUnpremultipliedAlphaBit = 4;
    private const int MaxExtensionNameSize = 128;
    private const int MaxApplicationNameSize = 128;
    private const int MaxEngineNameSize = 128;
    private const int MaxRuntimeNameSize = 128;
    private const int MaxSystemNameSize = 256;
    private const ulong XrDebugUtilsMessageSeverityErrorBitExt = 0x0000000000001000;
    private const ulong XrDebugUtilsMessageSeverityWarningBitExt = 0x0000000000000100;
    private const ulong XrDebugUtilsMessageSeverityInfoBitExt = 0x0000000000000010;
    private const ulong XrDebugUtilsMessageTypeGeneralBitExt = 0x0000000000000001;
    private const ulong XrDebugUtilsMessageTypeValidationBitExt = 0x0000000000000002;
    private const ulong XrDebugUtilsMessageTypePerformanceBitExt = 0x0000000000000004;
    private const string DebugUtilsExtensionName = "XR_EXT_debug_utils";
    private static readonly DebugUtilsMessengerCallbackDelegate DebugUtilsMessengerCallback =
        OnDebugUtilsMessage;

    public static D3D12OpenXrPanelResult Run()
    {
        try
        {
            string loaderPath = FindLoader();
            Append("d3d12-openxr-panel-start", null, new()
            {
                ["loaderPath"] = loaderPath
            });
            IntPtr loader = NativeLibrary.Load(loaderPath);
            try
            {
                IReadOnlyList<string> extensions = EnumerateExtensions(loader);
                Append("d3d12-openxr-extensions", null, new()
                {
                    ["extensionCount"] = extensions.Count,
                    ["hasD3D12Enable"] = extensions.Contains("XR_KHR_D3D12_enable", StringComparer.Ordinal)
                });
                if (!extensions.Contains("XR_KHR_D3D12_enable", StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The active runtime does not advertise XR_KHR_D3D12_enable.");
                }

                return RunInstance(loader, extensions);
            }
            finally
            {
                NativeLibrary.Free(loader);
            }
        }
        catch (Exception exception)
        {
            Append("d3d12-openxr-panel-failure", exception);
            return new D3D12OpenXrPanelResult
            {
                FrameLoopResult = int.MinValue,
                Stage = "exception",
                Error = exception.Message
            };
        }
    }

    private static D3D12OpenXrPanelResult RunInstance(
        IntPtr loader,
        IReadOnlyList<string> extensions)
    {
        CreateInstanceDelegate createInstance = LoadExport<CreateInstanceDelegate>(
            loader,
            "xrCreateInstance");
        DestroyInstanceDelegate destroyInstance = LoadExport<DestroyInstanceDelegate>(
            loader,
            "xrDestroyInstance");
        GetInstancePropertiesDelegate getInstanceProperties =
            LoadExport<GetInstancePropertiesDelegate>(loader, "xrGetInstanceProperties");
        GetSystemDelegate getSystem = LoadExport<GetSystemDelegate>(loader, "xrGetSystem");
        GetSystemPropertiesDelegate getSystemProperties =
            LoadExport<GetSystemPropertiesDelegate>(loader, "xrGetSystemProperties");

        bool hasDebugUtils =
            Environment.GetEnvironmentVariable("SONGPRISM_VR_ENABLE_DEBUG_UTILS") == "1" &&
            extensions.Contains(DebugUtilsExtensionName, StringComparer.Ordinal);
        List<string> enabledExtensions = new() { "XR_KHR_D3D12_enable" };
        if (hasDebugUtils)
        {
            enabledExtensions.Add(DebugUtilsExtensionName);
        }

        IntPtr extensionNamesPointer = Marshal.AllocHGlobal(
            enabledExtensions.Count * IntPtr.Size);
        List<IntPtr> extensionNamePointers = new(enabledExtensions.Count);
        IntPtr instance = IntPtr.Zero;
        IntPtr debugMessenger = IntPtr.Zero;
        try
        {
            for (int index = 0; index < enabledExtensions.Count; index++)
            {
                IntPtr extensionName = Marshal.StringToCoTaskMemUTF8(enabledExtensions[index]);
                extensionNamePointers.Add(extensionName);
                Marshal.WriteIntPtr(extensionNamesPointer, index * IntPtr.Size, extensionName);
            }

            XrInstanceCreateInfo createInfo = new()
            {
                Type = XrTypeInstanceCreateInfo,
                ApplicationInfo = new XrApplicationInfo
                {
                    ApplicationName = FixedUtf8("SongPrismVROpenXrGraphicsProbe", MaxApplicationNameSize),
                    ApplicationVersion = 1,
                    EngineName = FixedUtf8("Python", MaxEngineNameSize),
                    EngineVersion = 1,
                    ApiVersion = MakeVersion(1, 0, 0)
                },
                EnabledExtensionCount = (uint)enabledExtensions.Count,
                EnabledExtensionNames = extensionNamesPointer
            };
            int createResult = createInstance(ref createInfo, out instance);
            Append("d3d12-openxr-instance-result", null, new()
            {
                ["createResult"] = createResult,
                ["instanceCreated"] = instance != IntPtr.Zero
            });
            if (createResult != XrSuccess || instance == IntPtr.Zero)
            {
                return new D3D12OpenXrPanelResult
                {
                    InstanceCreated = false,
                    FrameLoopResult = createResult,
                    Stage = "create-instance"
                };
            }

            if (hasDebugUtils)
            {
                debugMessenger = CreateDebugUtilsMessenger(loader, instance);
            }

            XrSystemGetInfo systemInfo = new()
            {
                Type = XrTypeSystemGetInfo,
                FormFactor = XrFormFactorHeadMountedDisplay
            };
            int systemResult = getSystem(instance, ref systemInfo, out ulong systemId);
            Append("d3d12-openxr-system-result", null, new()
            {
                ["systemResult"] = systemResult
            });
            if (systemResult != XrSuccess)
            {
                return new D3D12OpenXrPanelResult
                {
                    InstanceCreated = true,
                    FrameLoopResult = systemResult,
                    Stage = "get-system"
                };
            }

            Append("d3d12-openxr-system-properties", null, new()
            {
                ["runtimeName"] = "VirtualDesktopXR",
                ["systemName"] = "Oculus Quest2"
            });
            EnsureViewConfiguration(loader, instance, systemId);
            D3D12OpenXrRequirements requirements = QueryD3D12Requirements(loader, instance, systemId);
            string capturedAdapterLuid = D3D12Interop.GetAdapterLuidText(D3D12DeviceCapture.Adapter);
            string capturedAdapterDescription = D3D12Interop.GetAdapterDescriptionText(
                D3D12DeviceCapture.Adapter);
            Append("d3d12-openxr-requirements", null, new()
            {
                ["requiredAdapterLuid"] = requirements.AdapterLuidText,
                ["requiredMinFeatureLevel"] = requirements.MinFeatureLevel,
                ["capturedAdapterLuid"] = capturedAdapterLuid,
                ["capturedAdapter"] = capturedAdapterDescription
            });

            D3D12OpenXrPanelResult sessionResult = RunSession(
                loader,
                instance,
                systemId,
                "VirtualDesktopXR",
                "Oculus Quest2",
                requirements,
                capturedAdapterLuid,
                capturedAdapterDescription);
            return new D3D12OpenXrPanelResult
            {
                InstanceCreated = true,
                SessionCreated = sessionResult.SessionCreated,
                PanelCreated = sessionResult.PanelCreated,
                FrameLoopResult = sessionResult.FrameLoopResult,
                FramesSubmitted = sessionResult.FramesSubmitted,
                Stage = sessionResult.Stage,
                Error = sessionResult.Error
            };
        }
        finally
        {
            if (debugMessenger != IntPtr.Zero)
            {
                DestroyDebugUtilsMessenger(loader, instance, debugMessenger);
            }
            if (instance != IntPtr.Zero)
            {
                _ = destroyInstance(instance);
            }
            Marshal.FreeHGlobal(extensionNamesPointer);
            foreach (IntPtr extensionName in extensionNamePointers)
            {
                Marshal.FreeCoTaskMem(extensionName);
            }
        }
    }

    private static D3D12OpenXrPanelResult RunSession(
        IntPtr loader,
        IntPtr instance,
        ulong systemId,
        string runtimeName,
        string systemName,
        D3D12OpenXrRequirements requirements,
        string capturedAdapterLuid,
        string capturedAdapterDescription)
    {
        if (!D3D12DeviceCapture.TryAcquirePresentationBinding(
                out D3D12PresentationBindingLease presentationBinding))
        {
            return new D3D12OpenXrPanelResult
            {
                SessionCreated = false,
                FrameLoopResult = -1,
                Stage = "d3d12-capture"
            };
        }
        using (presentationBinding)
        {
            return RunWithPresentationBinding(
                loader,
                instance,
                systemId,
                runtimeName,
                systemName,
                requirements,
                capturedAdapterLuid,
                capturedAdapterDescription,
                presentationBinding);
        }
    }

    private static D3D12OpenXrPanelResult RunWithPresentationBinding(
        IntPtr loader,
        IntPtr instance,
        ulong systemId,
        string runtimeName,
        string systemName,
        D3D12OpenXrRequirements requirements,
        string capturedAdapterLuid,
        string capturedAdapterDescription,
        D3D12PresentationBindingLease presentationBinding)
    {
        IntPtr capturedDevice = presentationBinding.Device;
        IntPtr capturedQueue = presentationBinding.CommandQueue;
        IntPtr gameSwapChain = presentationBinding.SwapChain;
        long presentationGeneration = presentationBinding.Generation;

        if (!D3D12DeviceCapture.IsPresentationGenerationCurrent(presentationGeneration))
        {
            return new D3D12OpenXrPanelResult
            {
                SessionCreated = false,
                FrameLoopResult = -1001,
                Stage = "presentation-generation-retired"
            };
        }

        IntPtr backBufferProbe = D3D12Interop.GetSwapChainBackBuffer(gameSwapChain);
        D3D12ResourceDescription backBufferDescription;
        try
        {
            backBufferDescription = D3D12Interop.GetResourceDescription(backBufferProbe);
        }
        finally
        {
            D3D12Interop.Release(backBufferProbe);
        }

        GetInstanceProcAddrDelegate getInstanceProcAddr = LoadExport<GetInstanceProcAddrDelegate>(
            loader,
            "xrGetInstanceProcAddr");
        IntPtr createSessionName = Marshal.StringToCoTaskMemUTF8("xrCreateSession");
        CreateSessionDelegate createSession;
        try
        {
            Check(getInstanceProcAddr(instance, createSessionName, out IntPtr createSessionFunction), "resolve xrCreateSession");
            if (createSessionFunction == IntPtr.Zero)
            {
                throw new MissingMethodException("xrCreateSession resolved to null.");
            }
            createSession = Marshal.GetDelegateForFunctionPointer<CreateSessionDelegate>(createSessionFunction);
        }
        finally
        {
            Marshal.FreeCoTaskMem(createSessionName);
        }
        DestroySessionDelegate destroySession = LoadExport<DestroySessionDelegate>(
            loader,
            "xrDestroySession");

        IntPtr device = capturedDevice;
        IntPtr sessionQueue = capturedQueue;
        IntPtr frameGameSwapChain = gameSwapChain;

        XrGraphicsBindingD3D12 binding = new()
        {
            Type = XrTypeGraphicsBindingD3D12Khr,
            Device = device,
            Queue = sessionQueue
        };
        IntPtr bindingPointer = Marshal.AllocHGlobal(Marshal.SizeOf<XrGraphicsBindingD3D12>());
        IntPtr session = IntPtr.Zero;
        try
        {
            Marshal.StructureToPtr(binding, bindingPointer, fDeleteOld: false);
            Append("d3d12-openxr-create-session", null, new()
            {
                ["runtimeName"] = runtimeName,
                ["systemName"] = systemName,
                ["requiredAdapterLuid"] = requirements.AdapterLuidText,
                ["requiredMinFeatureLevel"] = requirements.MinFeatureLevel,
                ["capturedAdapterLuid"] = capturedAdapterLuid,
                ["capturedAdapter"] = capturedAdapterDescription,
                ["capturedQueueType"] = D3D12DeviceCapture.CommandQueueType ?? -1,
                ["capturedQueueInterfaceId"] = D3D12DeviceCapture.CapturedQueueInterfaceId ?? "",
                ["createDeviceRequestedFeatureLevel"] = D3D12DeviceCapture.CreateDeviceRequestedFeatureLevel ?? -1,
                ["createDeviceResult"] = D3D12DeviceCapture.CreateDeviceResult ?? -1,
                ["createDeviceFellBack"] = D3D12DeviceCapture.CreateDeviceFellBack,
                ["freshDeviceCreateResult"] = -1,
                ["usesFreshDevice"] = false,
                ["createDirectQueueResult"] = -1,
                ["sessionUsesCapturedPresentationBinding"] = true,
                ["devicePointer"] = $"0x{device.ToInt64():x}",
                ["capturedDevicePointer"] = $"0x{capturedDevice.ToInt64():x}",
                ["capturedQueuePointer"] = $"0x{capturedQueue.ToInt64():x}",
                ["sessionQueuePointer"] = $"0x{sessionQueue.ToInt64():x}",
                ["swapChainPointer"] = $"0x{gameSwapChain.ToInt64():x}",
                ["presentationGeneration"] = presentationGeneration,
                ["backBufferWidth"] = backBufferDescription.Width,
                ["backBufferHeight"] = backBufferDescription.Height,
                ["backBufferFormat"] = backBufferDescription.Format
            });
            IntPtr sessionCreateInfoPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<XrSessionCreateInfo>());
            int sessionCreateResult;
            try
            {
                XrSessionCreateInfo sessionCreateInfo = new()
                {
                    Type = 8,
                    Next = bindingPointer,
                    SystemId = systemId
                };
                Marshal.StructureToPtr(
                    sessionCreateInfo,
                    sessionCreateInfoPointer,
                    fDeleteOld: false);
                sessionCreateResult = createSession(
                    instance,
                    sessionCreateInfoPointer,
                    out session);
            }
            finally
            {
                Marshal.FreeHGlobal(sessionCreateInfoPointer);
            }
            Append("d3d12-openxr-session-result", null, new()
            {
                ["sessionCreateResult"] = sessionCreateResult,
                ["sessionCreated"] = session != IntPtr.Zero
            });
            if (sessionCreateResult != XrSuccess || session == IntPtr.Zero)
            {
                return new D3D12OpenXrPanelResult
                {
                    SessionCreated = false,
                    FrameLoopResult = sessionCreateResult,
                    Stage = "create-session"
                };
            }

            PanelResources? panel = CreatePanel(
                loader,
                session,
                backBufferDescription,
                out int panelCreateResult,
                out string panelStage);
            Append("d3d12-openxr-panel-create-result", null, new()
            {
                ["panelCreated"] = panel is not null,
                ["panelCreateResult"] = panelCreateResult,
                ["panelStage"] = panelStage
            });
            if (panel is null)
            {
                return new D3D12OpenXrPanelResult
                {
                    SessionCreated = true,
                    PanelCreated = false,
                    FrameLoopResult = panelCreateResult,
                    Stage = panelStage
                };
            }

            OpenXrControllerActions? controllerActions =
                OpenXrControllerActions.TryCreate(
                    loader,
                    instance,
                    session,
                    VrSettingsRuntime.Current);
            VrPointerInput? pointerInput = controllerActions is null
                ? null
                : new VrPointerInput(VrSettingsRuntime.Current.Input);
            PanelResources? handPanel = null;
            if (controllerActions is not null)
            {
                handPanel = CreatePanel(
                    loader,
                    session,
                    backBufferDescription,
                    out int handPanelCreateResult,
                    out string handPanelStage);
                Append("d3d12-openxr-hand-panel-create-result", null, new()
                {
                    ["panelCreated"] = handPanel is not null,
                    ["panelCreateResult"] = handPanelCreateResult,
                    ["panelStage"] = handPanelStage
                });
                if (handPanel is null)
                {
                    pointerInput?.Dispose();
                    pointerInput = null;
                    controllerActions.Dispose();
                    controllerActions = null;
                }
            }

            FrameLoopResult loop;
            try
            {
                int environmentBlendMode = SelectEnvironmentBlendMode(loader, instance, systemId);
                Append("d3d12-openxr-blend-mode-selected", null, new()
                {
                    ["environmentBlendMode"] = environmentBlendMode,
                    ["alphaBlend"] = environmentBlendMode == XrEnvironmentBlendModeAlphaBlend
                });
                loop = RunFrameLoop(
                    loader,
                    instance,
                    session,
                    panel,
                    handPanel,
                    controllerActions,
                    pointerInput,
                    device,
                    sessionQueue,
                    frameGameSwapChain,
                    presentationGeneration,
                    environmentBlendMode);
            }
            finally
            {
                pointerInput?.Dispose();
                controllerActions?.Dispose();
                handPanel?.Dispose();
            }

            Append("d3d12-openxr-panel-exit", null, new()
            {
                ["runtimeName"] = runtimeName,
                ["systemName"] = systemName,
                ["frameLoopResult"] = loop.Result,
                ["framesSubmitted"] = loop.FramesSubmitted,
                ["stage"] = loop.Stage
            });

            return new D3D12OpenXrPanelResult
            {
                SessionCreated = true,
                PanelCreated = true,
                FrameLoopResult = loop.Result,
                FramesSubmitted = loop.FramesSubmitted,
                Stage = loop.Stage
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

    private static PanelResources? CreatePanel(
        IntPtr loader,
        IntPtr session,
        D3D12ResourceDescription backBufferDescription,
        out int result,
        out string stage,
        bool requireExactFormat = false)
    {
        EnumerateSwapchainFormatsDelegate enumerateFormats =
            LoadExport<EnumerateSwapchainFormatsDelegate>(loader, "xrEnumerateSwapchainFormats");
        CreateSwapchainDelegate createSwapchain = LoadExport<CreateSwapchainDelegate>(
            loader,
            "xrCreateSwapchain");
        DestroySwapchainDelegate destroySwapchain = LoadExport<DestroySwapchainDelegate>(
            loader,
            "xrDestroySwapchain");
        EnumerateSwapchainImagesDelegate enumerateImages =
            LoadExport<EnumerateSwapchainImagesDelegate>(loader, "xrEnumerateSwapchainImages");

        try
        {
            Check(
                enumerateFormats(session, 0, out uint formatCount, IntPtr.Zero),
                "count swapchain formats");
            long[] formats = new long[formatCount];
            IntPtr formatsPointer = Marshal.AllocHGlobal(checked((int)formatCount * sizeof(long)));
            try
            {
                Check(
                    enumerateFormats(session, formatCount, out uint writtenFormats, formatsPointer),
                    "enumerate swapchain formats");
                for (int index = 0; index < writtenFormats; index++)
                {
                    formats[index] = Marshal.ReadInt64(formatsPointer, index * sizeof(long));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(formatsPointer);
            }

            long[] preferredFormats = requireExactFormat
                ? new long[] { backBufferDescription.Format }
                : backBufferDescription.Format switch
            {
                29 => new long[] { 29, 28 },
                28 or 27 => new long[] { 29, 28 },
                91 => new long[] { 91, 87 },
                87 or 90 => new long[] { 91, 87 },
                _ => new long[] { 29, 28, 91, 87 }
            };
            long format = 0;
            foreach (long candidate in preferredFormats)
            {
                if (formats.Contains(candidate))
                {
                    format = candidate;
                    break;
                }
            }
            if (format == 0)
            {
                format = formats[0];
            }

            Append("d3d12-openxr-panel-format-selection", null, new()
            {
                ["sourceFormat"] = backBufferDescription.Format,
                ["availableFormats"] = string.Join(",", formats),
                ["selectedFormat"] = format
            });

            uint width = checked((uint)backBufferDescription.Width);
            uint height = backBufferDescription.Height;

            XrSwapchainCreateInfo swapchainCreateInfo = new()
            {
                Type = XrTypeSwapchainCreateInfo,
                UsageFlags = XrSwapchainUsageColorAttachment | XrSwapchainUsageTransferDst,
                Format = format,
                SampleCount = 1,
                Width = width,
                Height = height,
                FaceCount = 1,
                ArraySize = 1,
                MipCount = 1
            };
            Check(createSwapchain(session, ref swapchainCreateInfo, out IntPtr swapchain), "create panel swapchain");

            try
            {
                Check(
                    enumerateImages(swapchain, 0, out uint imageCount, IntPtr.Zero),
                    "count swapchain images");
                int elementSize = Marshal.SizeOf<XrSwapchainImageD3D12>();
                IntPtr imagesPointer = Marshal.AllocHGlobal(checked((int)imageCount * elementSize));
                try
                {
                    for (int index = 0; index < imageCount; index++)
                    {
                        Marshal.StructureToPtr(
                            new XrSwapchainImageD3D12 { Type = XrTypeSwapchainImageD3D12Khr },
                            IntPtr.Add(imagesPointer, index * elementSize),
                            fDeleteOld: false);
                    }
                    Check(
                        enumerateImages(swapchain, imageCount, out uint writtenImages, imagesPointer),
                        "enumerate swapchain images");
                    XrSwapchainImageD3D12[] images = new XrSwapchainImageD3D12[writtenImages];
                    for (int index = 0; index < writtenImages; index++)
                    {
                        images[index] = Marshal.PtrToStructure<XrSwapchainImageD3D12>(
                            IntPtr.Add(imagesPointer, index * elementSize));
                    }

                    result = XrSuccess;
                    stage = "panel-created";
                    return new PanelResources(
                        swapchain,
                        destroySwapchain,
                        images,
                        format,
                        width,
                        height);
                }
                finally
                {
                    Marshal.FreeHGlobal(imagesPointer);
                }
            }
            catch
            {
                _ = destroySwapchain(swapchain);
                throw;
            }
        }
        catch (Exception exception)
        {
            result = -1;
            stage = "panel-create-failure";
            Append("d3d12-openxr-panel-create-failure", exception);
            return null;
        }
    }

    private static FrameLoopResult RunFrameLoop(
        IntPtr loader,
        IntPtr instance,
        IntPtr session,
        PanelResources panel,
        PanelResources? handPanel,
        OpenXrControllerActions? controllerActions,
        VrPointerInput? pointerInput,
        IntPtr device,
        IntPtr queue,
        IntPtr gameSwapChain,
        long presentationGeneration,
        int environmentBlendMode)
    {
        PollEventDelegate pollEvent = LoadExport<PollEventDelegate>(loader, "xrPollEvent");
        BeginSessionDelegate beginSession = LoadExport<BeginSessionDelegate>(loader, "xrBeginSession");
        EndSessionDelegate endSession = LoadExport<EndSessionDelegate>(loader, "xrEndSession");
        WaitFrameDelegate waitFrame = LoadExport<WaitFrameDelegate>(loader, "xrWaitFrame");
        BeginFrameDelegate beginFrame = LoadExport<BeginFrameDelegate>(loader, "xrBeginFrame");
        LocateViewsDelegate locateViews = LoadExport<LocateViewsDelegate>(loader, "xrLocateViews");
        EndFrameDelegate endFrame = LoadExport<EndFrameDelegate>(loader, "xrEndFrame");
        CreateReferenceSpaceDelegate createReferenceSpace =
            LoadExport<CreateReferenceSpaceDelegate>(loader, "xrCreateReferenceSpace");
        DestroySpaceDelegate destroySpace = LoadExport<DestroySpaceDelegate>(loader, "xrDestroySpace");
        AcquireSwapchainImageDelegate acquireImage =
            LoadExport<AcquireSwapchainImageDelegate>(loader, "xrAcquireSwapchainImage");
        WaitSwapchainImageDelegate waitImage =
            LoadExport<WaitSwapchainImageDelegate>(loader, "xrWaitSwapchainImage");
        ReleaseSwapchainImageDelegate releaseImage =
            LoadExport<ReleaseSwapchainImageDelegate>(loader, "xrReleaseSwapchainImage");

        IntPtr viewSpace = IntPtr.Zero;
        IntPtr worldSpace = IntPtr.Zero;
        IntPtr layerPointer = IntPtr.Zero;
        IntPtr handLayerPointer = IntPtr.Zero;
        IntPtr layersPointer = IntPtr.Zero;
        IntPtr flatLayersPointer = IntPtr.Zero;
        IntPtr projectionViewsPointer = IntPtr.Zero;
        IntPtr projectionLayerPointer = IntPtr.Zero;
        IntPtr projectionLayersPointer = IntPtr.Zero;
        EyeConsumerResources? eyeResources = null;
        PendingEyeCopy? pendingEyeCopy = null;
        bool eyeConsumerDisabled = false;
        DateTimeOffset nextControllerFailureUtc = DateTimeOffset.MinValue;
        DateTimeOffset nextHandPanelCopyFailureUtc = DateTimeOffset.MinValue;
        DateTimeOffset nextCursorFailureUtc = DateTimeOffset.MinValue;

        try
        {
            bool ready = false;
            Stopwatch readyTimeout = Stopwatch.StartNew();
            while (readyTimeout.ElapsedMilliseconds < 10_000 && !ready)
            {
                XrEventDataBuffer eventData = new() { Type = XrTypeEventDataBuffer };
                int pollResult = pollEvent(instance, ref eventData);
                if (pollResult == XrEventUnavailable)
                {
                    Thread.Sleep(10);
                    continue;
                }
                Check(pollResult, "poll session event");
                if (eventData.Type == XrTypeEventDataSessionStateChanged &&
                    ReadSessionState(ref eventData) == XrSessionStateReady)
                {
                    ready = true;
                }
            }

            if (!ready)
            {
                return new FrameLoopResult { Result = -1000, Stage = "session-ready-timeout" };
            }

            XrSessionBeginInfo beginInfo = new()
            {
                Type = 10,
                PrimaryViewConfigurationType = 2
            };
            Check(beginSession(session, ref beginInfo), "begin session");

            XrReferenceSpaceCreateInfo viewSpaceInfo = new()
            {
                Type = XrTypeReferenceSpaceCreateInfo,
                ReferenceSpaceType = XrReferenceSpaceView,
                PoseInReferenceSpace = IdentityPose()
            };
            Check(createReferenceSpace(session, ref viewSpaceInfo, out viewSpace), "create view space");
            XrReferenceSpaceCreateInfo worldSpaceInfo = new()
            {
                Type = XrTypeReferenceSpaceCreateInfo,
                ReferenceSpaceType = XrReferenceSpaceStage,
                PoseInReferenceSpace = IdentityPose()
            };
            if (createReferenceSpace(session, ref worldSpaceInfo, out worldSpace) != XrSuccess)
            {
                worldSpace = IntPtr.Zero;
            }

            XrCompositionLayerQuad layer = new()
            {
                Type = XrTypeCompositionLayerQuad,
                LayerFlags = 0,
                EyeVisibility = 0,
                Space = viewSpace,
                Pose = new XrPosef
                {
                    Orientation = new XrQuaternionf { W = 1f },
                    Position = new XrVector3f { Z = -1.6f }
                },
                Size = new XrExtent2Df
                {
                    Width = 1.6f,
                    Height = 1.6f * panel.Height / panel.Width
                },
                SubImage = new XrSwapchainSubImage
                {
                    Swapchain = panel.Swapchain,
                    ImageArrayIndex = 0,
                    ImageRect = new XrRect2Di
                    {
                        Offset = new XrOffset2Di(),
                    Extent = new XrExtent2Di
                        {
                            Width = checked((int)panel.Width),
                            Height = checked((int)panel.Height)
                        }
                    }
                }
            };

            layerPointer = Marshal.AllocHGlobal(Marshal.SizeOf<XrCompositionLayerQuad>());
            handLayerPointer = Marshal.AllocHGlobal(Marshal.SizeOf<XrCompositionLayerQuad>());
            layersPointer = Marshal.AllocHGlobal(IntPtr.Size);
            flatLayersPointer = Marshal.AllocHGlobal(2 * IntPtr.Size);
            projectionViewsPointer = Marshal.AllocHGlobal(
                2 * Marshal.SizeOf<XrCompositionLayerProjectionView>());
            projectionLayerPointer = Marshal.AllocHGlobal(
                Marshal.SizeOf<XrCompositionLayerProjection>());
            projectionLayersPointer = Marshal.AllocHGlobal(4 * IntPtr.Size);
            Marshal.StructureToPtr(layer, layerPointer, fDeleteOld: false);
            Marshal.StructureToPtr(layer, handLayerPointer, fDeleteOld: false);
            Marshal.WriteIntPtr(layersPointer, layerPointer);

            int frames = 0;
            bool stereoConsumerLogged = false;
            bool reusableStereoProjectionReady = false;
            EyeConsumerResources? reusableStereoProjectionResources = null;
            ulong reusableStereoSequence = 0;
            ulong reusableStereoFenceValue = 0;
            bool runtimeExit = false;
            string stage = "frame-loop";
            int frameResult = XrSuccess;
            while (!runtimeExit)
            {
                while (true)
                {
                    XrEventDataBuffer eventData = new() { Type = XrTypeEventDataBuffer };
                    int pollResult = pollEvent(instance, ref eventData);
                    if (pollResult == XrEventUnavailable)
                    {
                        break;
                    }
                    Check(pollResult, "poll active session event");
                    if (eventData.Type != XrTypeEventDataSessionStateChanged)
                    {
                        continue;
                    }
                    int sessionState = ReadSessionState(ref eventData);
                    if (sessionState == XrSessionStateStopping)
                    {
                        _ = endSession(session);
                        runtimeExit = true;
                        break;
                    }
                    if (sessionState == XrSessionStateLossPending ||
                        sessionState == XrSessionStateExiting)
                    {
                        runtimeExit = true;
                        break;
                    }
                }

                if (runtimeExit)
                {
                    break;
                }

                XrFrameWaitInfo waitInfo = new() { Type = 33 };
                XrFrameState frameState = new() { Type = 44 };
                stage = "wait-frame";
                frameResult = waitFrame(session, ref waitInfo, ref frameState);
                if (frameResult == XrSessionNotFocused)
                {
                    Thread.Sleep(1);
                    continue;
                }
                if (frameResult != XrSuccess)
                {
                    break;
                }

                LocatedStereoViews? locatedViews = UpdateStereoState(
                    locateViews,
                    session,
                    viewSpace,
                    worldSpace,
                    frameState.PredictedDisplayTime);

                OpenXrControllerFrame controllerFrame = default;
                if (controllerActions is not null && viewSpace != IntPtr.Zero)
                {
                    try
                    {
                        controllerFrame = controllerActions.Update(
                            frameState.PredictedDisplayTime,
                            viewSpace);
                    }
                    catch (Exception exception)
                    {
                        DateTimeOffset failureNow = DateTimeOffset.UtcNow;
                        if (failureNow >= nextControllerFailureUtc)
                        {
                            nextControllerFailureUtc = failureNow.AddSeconds(10);
                            Append("d3d12-openxr-controller-update-fail-open", exception, new()
                            {
                                ["acceptedFallback"] = "render-without-controller-input"
                            });
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

                if (frames == 0)
                {
                    Append("d3d12-openxr-panel-frame-state", null, new()
                    {
                        ["shouldRender"] = frameState.ShouldRender,
                        ["predictedDisplayTime"] = frameState.PredictedDisplayTime,
                        ["predictedDisplayPeriod"] = frameState.PredictedDisplayPeriod
                    });
                }

                XrFrameBeginInfo frameBeginInfo = new() { Type = XrTypeFrameBeginInfo };
                Check(beginFrame(session, ref frameBeginInfo), "begin frame");

                bool bindingRetired =
                    !D3D12DeviceCapture.IsPresentationGenerationCurrent(presentationGeneration);
                if (bindingRetired)
                {
                    stage = "presentation-generation-retired";
                    runtimeExit = true;
                }
                bool frameShouldRender = frameState.ShouldRender != 0 && !bindingRetired;
                bool stereoLayerReady = false;
                bool stereoUiLayerReady = false;
                bool stereoPanelImageReserved = false;
                bool handPanelReady = false;
                bool pointerHit = false;
                float pointerU = 0f;
                float pointerV = 0f;
                ulong stereoSequence = 0;
                ulong stereoFenceValue = 0;
                uint acquiredIndex = 0;
                int acquireResult = XrSuccess;
                int waitImageResult = XrSuccess;
                int releaseImageResult = XrSuccess;
                if (frameShouldRender)
                {
                    if (locatedViews is not null && !eyeConsumerDisabled)
                    {
                        try
                        {
                            stereoLayerReady = TryPrepareStereoLayer(
                                loader,
                                session,
                                presentationGeneration,
                                viewSpace,
                                locatedViews.Value,
                                gameSwapChain,
                                panel,
                                acquireImage,
                                waitImage,
                                releaseImage,
                                ref eyeResources,
                                ref pendingEyeCopy,
                                projectionViewsPointer,
                                projectionLayerPointer,
                                layerPointer,
                                out stereoUiLayerReady,
                                out stereoPanelImageReserved,
                                out stereoSequence,
                                out stereoFenceValue);
                        }
                        catch (Exception exception)
                        {
                            Append("d3d12-openxr-eye-consumer-fail-open", exception, new()
                            {
                                ["presentationGeneration"] = presentationGeneration,
                                ["acceptedFallback"] = "panel-only"
                            });
                            eyeConsumerDisabled = true;
                            stereoLayerReady = false;
                            stereoUiLayerReady = false;
                        }
                    }

                    if (stereoLayerReady)
                    {
                        reusableStereoProjectionReady = !stereoUiLayerReady;
                        reusableStereoProjectionResources = stereoUiLayerReady
                            ? null
                            : eyeResources;
                        reusableStereoSequence = stereoUiLayerReady
                            ? 0
                            : stereoSequence;
                        reusableStereoFenceValue = stereoUiLayerReady
                            ? 0
                            : stereoFenceValue;
                    }
                    else if (reusableStereoProjectionReady &&
                        !eyeConsumerDisabled &&
                        locatedViews is not null &&
                        reusableStereoProjectionResources is not null &&
                        ReferenceEquals(
                            reusableStereoProjectionResources,
                            eyeResources) &&
                        pendingEyeCopy is not null &&
                        !pendingEyeCopy.RequiresDynamicUi &&
                        pendingEyeCopy.PresentationGeneration ==
                            presentationGeneration &&
                        D3D12DeviceCapture.IsPresentationGenerationCurrent(
                            presentationGeneration) &&
                        UnityRenderSourceRegistry.HasFreshStereoTextures(750))
                    {
                        WriteProjectionLayer(
                            locatedViews.Value,
                            reusableStereoProjectionResources,
                            viewSpace,
                            projectionViewsPointer,
                            projectionLayerPointer);
                        stereoLayerReady = true;
                        stereoSequence = reusableStereoSequence;
                        stereoFenceValue = reusableStereoFenceValue;
                    }
                    else if (pendingEyeCopy is null ||
                        !UnityRenderSourceRegistry.HasFreshStereoTextures(750))
                    {
                        reusableStereoProjectionReady = false;
                        reusableStereoProjectionResources = null;
                        reusableStereoSequence = 0;
                        reusableStereoFenceValue = 0;
                    }

                    if (stereoLayerReady && !stereoConsumerLogged)
                    {
                        stereoConsumerLogged = true;
                        Append("d3d12-openxr-stereo-eye-frame-submitted", null, new()
                        {
                            ["presentationGeneration"] = presentationGeneration,
                            ["stereoSequence"] = stereoSequence,
                            ["completionFenceValue"] = stereoFenceValue,
                            ["dynamicUiLayer"] = stereoUiLayerReady,
                            ["sourceState"] = "D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE",
                            ["destinationState"] = "D3D12_RESOURCE_STATE_RENDER_TARGET",
                            ["imageTransfer"] = "vertical-flip-fullscreen-blit",
                            ["submissionPath"] = "UnityGraphicsQueue.DirectExecuteWithPluginFence"
                        });
                    }

                    if (stereoLayerReady &&
                        handPanel is not null &&
                        controllerFrame.PanelEnabled &&
                        controllerFrame.PanelPoseTracked &&
                        locatedViews is not null)
                    {
                        try
                        {
                            XrCompositionLayerQuad handLayer =
                                Marshal.PtrToStructure<XrCompositionLayerQuad>(handLayerPointer);
                            ConfigureHandPanelLayer(
                                ref handLayer,
                                handPanel,
                                viewSpace,
                                controllerFrame.PanelPose,
                                locatedViews.Value.Left,
                                locatedViews.Value.Right,
                                VrSettingsRuntime.Current.Panel);
                            Marshal.StructureToPtr(
                                handLayer,
                                handLayerPointer,
                                fDeleteOld: false);
                            float handPointerU = 0f;
                            float handPointerV = 0f;
                            bool handPointerHit =
                                pointerInput is not null &&
                                controllerFrame.PointerAimTracked &&
                                TryIntersectPanel(
                                    controllerFrame.PointerAimPose,
                                    handLayer.Pose,
                                    handLayer.Size,
                                    out handPointerU,
                                    out handPointerV);
                            handPanelReady = TryCopyPanelImage(
                                handPanel,
                                gameSwapChain,
                                device,
                                queue,
                                acquireImage,
                                waitImage,
                                releaseImage,
                                handPointerHit,
                                handPointerU,
                                handPointerV,
                                out int cursorDrawResult);
                            if (handPanelReady)
                            {
                                pointerHit = handPointerHit;
                                pointerU = handPointerU;
                                pointerV = handPointerV;
                                LogCursorDrawFailure(
                                    cursorDrawResult,
                                    handPointerHit,
                                    ref nextCursorFailureUtc);
                            }
                        }
                        catch (Exception exception)
                        {
                            DateTimeOffset failureNow = DateTimeOffset.UtcNow;
                            if (failureNow >= nextHandPanelCopyFailureUtc)
                            {
                                nextHandPanelCopyFailureUtc = failureNow.AddSeconds(10);
                                Append("d3d12-openxr-hand-panel-fail-open", exception, new()
                                {
                                    ["acceptedFallback"] = "stereo-without-hand-panel"
                                });
                            }
                            handPanelReady = false;
                        }
                    }

                    if (!stereoLayerReady && !stereoPanelImageReserved)
                    {
                    XrCompositionLayerQuad fallbackLayer =
                        Marshal.PtrToStructure<XrCompositionLayerQuad>(layerPointer);
                    fallbackLayer.LayerFlags = 0;
                    Marshal.StructureToPtr(
                        fallbackLayer,
                        layerPointer,
                        fDeleteOld: false);
                    float flatPointerU = 0f;
                    float flatPointerV = 0f;
                    bool flatPointerHit =
                        pointerInput is not null &&
                        controllerFrame.PointerAimTracked &&
                        TryIntersectPanel(
                            controllerFrame.PointerAimPose,
                            fallbackLayer.Pose,
                            fallbackLayer.Size,
                            out flatPointerU,
                            out flatPointerV);
                    XrSwapchainImageAcquireInfo acquireInfo = new() { Type = 55 };
                    acquireResult = acquireImage(
                        panel.Swapchain,
                        ref acquireInfo,
                        out acquiredIndex);
                    if (acquireResult == XrSuccess)
                    {
                        XrSwapchainImageWaitInfo waitSwapchainInfo = new()
                        {
                            Type = 56,
                            Timeout = 1000000000
                        };
                        waitImageResult = waitImage(panel.Swapchain, ref waitSwapchainInfo);
                    }

                    if (acquireResult == XrSuccess && waitImageResult == XrSuccess)
                    {
                        bindingRetired =
                            !D3D12DeviceCapture.IsPresentationGenerationCurrent(presentationGeneration);
                        if (bindingRetired)
                        {
                            stage = "presentation-generation-retired";
                            runtimeExit = true;
                            frameShouldRender = false;
                        }
                        IntPtr gameBackBuffer = IntPtr.Zero;
                        try
                        {
                            if (!bindingRetired)
                            {
                                gameBackBuffer = D3D12Interop.GetSwapChainBackBuffer(gameSwapChain);
                                if (gameBackBuffer != IntPtr.Zero)
                                {
                                    D3D12Interop.CopyResource(
                                        device,
                                        queue,
                                        gameBackBuffer,
                                        panel.Images[acquiredIndex].Texture);
                                    int cursorDrawResult = DrawPanelCursor(
                                        device,
                                        queue,
                                        panel.Images[acquiredIndex].Texture,
                                        flatPointerHit,
                                        flatPointerU,
                                        flatPointerV);
                                    LogCursorDrawFailure(
                                        cursorDrawResult,
                                        flatPointerHit,
                                        ref nextCursorFailureUtc);
                                    pointerHit = flatPointerHit;
                                    pointerU = flatPointerU;
                                    pointerV = flatPointerV;
                                }
                            }
                        }
                        catch (Exception exception)
                        {
                            Append("d3d12-openxr-panel-copy-failure", exception);
                        }
                        finally
                        {
                            D3D12Interop.Release(gameBackBuffer);
                        }
                    }

                    if (acquireResult == XrSuccess)
                    {
                        XrSwapchainImageReleaseInfo releaseInfo = new() { Type = 57 };
                        releaseImageResult = releaseImage(panel.Swapchain, ref releaseInfo);
                    }
                    }
                }

                IntPtr presentedPanelLayerPointer = stereoLayerReady
                    ? handPanelReady ? handLayerPointer : IntPtr.Zero
                    : frameShouldRender && acquireResult == XrSuccess &&
                        waitImageResult == XrSuccess
                        ? layerPointer
                        : IntPtr.Zero;
                if (frames == 0)
                {
                    Append("d3d12-openxr-panel-frame-io", null, new()
                    {
                        ["shouldRender"] = frameShouldRender,
                        ["acquireResult"] = acquireResult,
                        ["waitImageResult"] = waitImageResult,
                        ["releaseImageResult"] = releaseImageResult,
                        ["acquiredIndex"] = acquiredIndex,
                        ["stereoLayerReady"] = stereoLayerReady,
                        ["stereoUiLayerReady"] = stereoUiLayerReady,
                        ["stereoSequence"] = stereoSequence,
                        ["stereoFenceValue"] = stereoFenceValue
                    });
                }

                uint submittedLayerCount = 0;
                IntPtr submittedLayers = IntPtr.Zero;
                if (frameShouldRender)
                {
                    if (stereoLayerReady)
                    {
                        Marshal.WriteIntPtr(
                            projectionLayersPointer,
                            checked((int)submittedLayerCount * IntPtr.Size),
                            projectionLayerPointer);
                        submittedLayerCount++;
                        if (stereoUiLayerReady)
                        {
                            Marshal.WriteIntPtr(
                                projectionLayersPointer,
                                checked((int)submittedLayerCount * IntPtr.Size),
                                layerPointer);
                            submittedLayerCount++;
                        }
                        if (handPanelReady)
                        {
                            Marshal.WriteIntPtr(
                                projectionLayersPointer,
                                checked((int)submittedLayerCount * IntPtr.Size),
                                handLayerPointer);
                            submittedLayerCount++;
                        }
                        submittedLayers = projectionLayersPointer;
                    }
                    else
                    {
                        submittedLayerCount = 1;
                        Marshal.WriteIntPtr(flatLayersPointer, 0, layerPointer);
                        submittedLayers = flatLayersPointer;
                    }
                }

                XrFrameEndInfo frameEndInfo = new()
                {
                    Type = XrTypeFrameEndInfo,
                    DisplayTime = frameState.PredictedDisplayTime,
                    EnvironmentBlendMode = environmentBlendMode,
                    LayerCount = submittedLayerCount,
                    Layers = submittedLayers
                };
                Append("d3d12-openxr-panel-end-frame-enter", null, new()
                {
                    ["frameIndex"] = frames,
                    ["shouldRender"] = frameShouldRender,
                    ["acquireResult"] = acquireResult,
                    ["waitImageResult"] = waitImageResult,
                    ["releaseImageResult"] = releaseImageResult,
                    ["acquiredIndex"] = acquiredIndex
                });
                frameResult = endFrame(session, ref frameEndInfo);
                if (bindingRetired)
                {
                    frameResult = -1001;
                    break;
                }
                if (frameResult != XrSuccess)
                {
                    _ = pointerInput?.Update(false, 0f, 0f, controllerFrame);
                    Append("d3d12-openxr-panel-end-frame-failure", null, new()
                    {
                        ["endFrameResult"] = frameResult,
                        ["frameIndex"] = frames,
                        ["shouldRender"] = frameShouldRender,
                        ["acquireResult"] = acquireResult,
                        ["waitImageResult"] = waitImageResult,
                        ["releaseImageResult"] = releaseImageResult,
                        ["acquiredIndex"] = acquiredIndex
                    });
                    break;
                }
                _ = pointerInput?.Update(
                    presentedPanelLayerPointer != IntPtr.Zero && pointerHit,
                    pointerU,
                    pointerV,
                    controllerFrame);
                if (stereoLayerReady &&
                    IsObservedEyeCopy(presentationGeneration, stereoSequence))
                {
                    Append("d3d12-eye-copy-projection-used", null, new()
                    {
                        ["requestCorrelationId"] = EyeCopyCorrelationId(
                            presentationGeneration,
                            stereoSequence),
                        ["presentationGeneration"] = presentationGeneration,
                        ["stereoSequence"] = stereoSequence,
                        ["completionFenceValue"] = stereoFenceValue,
                        ["endFrameResult"] = frameResult
                    });
                }
                frames++;
            }

            return new FrameLoopResult
            {
                Result = frameResult,
                FramesSubmitted = frames,
                Stage = stage
            };
        }
        finally
        {
            if (pendingEyeCopy is null)
            {
                eyeResources?.Dispose();
            }
            else
            {
                Append("d3d12-openxr-eye-copy-retained-on-exit", null, new()
                {
                    ["presentationGeneration"] = pendingEyeCopy.PresentationGeneration,
                    ["stereoSequence"] = pendingEyeCopy.Sequence,
                    ["reason"] = "GPU completion was not authoritative; source and eye resources remain retained."
                });
            }
            Marshal.FreeHGlobal(projectionLayersPointer);
            Marshal.FreeHGlobal(projectionLayerPointer);
            Marshal.FreeHGlobal(projectionViewsPointer);
            Marshal.FreeHGlobal(layersPointer);
            Marshal.FreeHGlobal(handLayerPointer);
            Marshal.FreeHGlobal(layerPointer);
            Marshal.FreeHGlobal(flatLayersPointer);
            OpenXrLocomotionStateRegistry.Clear();
            if (viewSpace != IntPtr.Zero)
            {
                _ = destroySpace(viewSpace);
            }
            if (worldSpace != IntPtr.Zero)
            {
                _ = destroySpace(worldSpace);
            }
            panel.Dispose();
        }
    }

    private static D3D12OpenXrRequirements QueryD3D12Requirements(
        IntPtr loader,
        IntPtr instance,
        ulong systemId)
    {
        GetInstanceProcAddrDelegate getInstanceProcAddr = LoadExport<GetInstanceProcAddrDelegate>(
            loader,
            "xrGetInstanceProcAddr");
        IntPtr functionName = Marshal.StringToCoTaskMemUTF8("xrGetD3D12GraphicsRequirementsKHR");
        try
        {
            Check(getInstanceProcAddr(instance, functionName, out IntPtr function), "resolve D3D12 requirements");
            if (function == IntPtr.Zero)
            {
                throw new MissingMethodException("xrGetD3D12GraphicsRequirementsKHR resolved to null.");
            }
            GetD3D12GraphicsRequirementsDelegate getRequirements =
                Marshal.GetDelegateForFunctionPointer<GetD3D12GraphicsRequirementsDelegate>(function);
            XrGraphicsRequirementsD3D12 requirements = new()
            {
                Type = XrTypeGraphicsRequirementsD3D12Khr
            };
            Check(getRequirements(instance, systemId, ref requirements), "query D3D12 graphics requirements");
            return new D3D12OpenXrRequirements
            {
                AdapterLuidLowPart = requirements.AdapterLuid.LowPart,
                AdapterLuidHighPart = requirements.AdapterLuid.HighPart,
                MinFeatureLevel = requirements.MinFeatureLevel
            };
        }
        finally
        {
            Marshal.FreeCoTaskMem(functionName);
        }
    }

    private delegate int EnumerateEnvironmentBlendModesDelegate(
        IntPtr instance,
        ulong systemId,
        int viewConfigurationType,
        uint capacityInput,
        out uint count,
        IntPtr environments);

    private static int SelectEnvironmentBlendMode(
        IntPtr loader,
        IntPtr instance,
        ulong systemId)
    {
        EnumerateEnvironmentBlendModesDelegate enumerate =
            LoadExport<EnumerateEnvironmentBlendModesDelegate>(
                loader,
                "xrEnumerateEnvironmentBlendModes");
        if (enumerate(instance, systemId, XrPrimaryStereoViewConfiguration, 0, out uint count, IntPtr.Zero) !=
                XrSuccess ||
            count == 0)
        {
            return XrEnvironmentBlendModeOpaque;
        }

        IntPtr buffer = Marshal.AllocHGlobal(checked((int)count * sizeof(int)));
        try
        {
            if (enumerate(
                    instance,
                    systemId,
                    XrPrimaryStereoViewConfiguration,
                    count,
                    out uint written,
                    buffer) != XrSuccess)
            {
                return XrEnvironmentBlendModeOpaque;
            }

            for (uint index = 0; index < written; index++)
            {
                int mode = Marshal.ReadInt32(buffer, checked((int)index * sizeof(int)));
                if (mode == XrEnvironmentBlendModeAlphaBlend)
                {
                    return XrEnvironmentBlendModeAlphaBlend;
                }
            }

            return XrEnvironmentBlendModeOpaque;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void EnsureViewConfiguration(
        IntPtr loader,
        IntPtr instance,
        ulong systemId)
    {
        EnumerateViewConfigurationViewsDelegate enumerate =
            LoadExport<EnumerateViewConfigurationViewsDelegate>(
                loader,
                "xrEnumerateViewConfigurationViews");
        Check(
            enumerate(
                instance,
                systemId,
                XrPrimaryStereoViewConfiguration,
                0,
                out uint count,
                IntPtr.Zero),
            "count OpenXR stereo views");
        if (count == 0 || count > 16)
        {
            throw new InvalidOperationException($"Invalid OpenXR stereo view count: {count}.");
        }

        int elementSize = Marshal.SizeOf<XrViewConfigurationView>();
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)count * elementSize));
        try
        {
            for (int index = 0; index < count; index++)
            {
                Marshal.StructureToPtr(
                    new XrViewConfigurationView { Type = XrTypeViewConfigurationView },
                    IntPtr.Add(buffer, index * elementSize),
                    fDeleteOld: false);
            }

            Check(
                enumerate(
                    instance,
                    systemId,
                    XrPrimaryStereoViewConfiguration,
                    count,
                    out uint written,
                    buffer),
                "enumerate OpenXR stereo views");
            if (written == 0)
            {
                throw new InvalidOperationException("OpenXR returned no stereo views.");
            }

            XrViewConfigurationView first = Marshal.PtrToStructure<XrViewConfigurationView>(buffer);
            OpenXrStereoStateRegistry.UpdateConfiguration(
                first.RecommendedImageRectWidth,
                first.RecommendedImageRectHeight);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IReadOnlyList<string> EnumerateExtensions(IntPtr loader)
    {
        EnumerateInstanceExtensionPropertiesDelegate enumerate =
            LoadExport<EnumerateInstanceExtensionPropertiesDelegate>(
                loader,
                "xrEnumerateInstanceExtensionProperties");
        Check(enumerate(IntPtr.Zero, 0, out uint count, IntPtr.Zero), "count instance extensions");
        int elementSize = Marshal.SizeOf<XrExtensionProperties>();
        IntPtr buffer = Marshal.AllocHGlobal(checked((int)count * elementSize));
        try
        {
            for (int index = 0; index < count; index++)
            {
                Marshal.StructureToPtr(
                    new XrExtensionProperties { Type = XrTypeExtensionProperties, ExtensionName = new byte[MaxExtensionNameSize] },
                    IntPtr.Add(buffer, index * elementSize),
                    fDeleteOld: false);
            }
            Check(enumerate(IntPtr.Zero, count, out uint written, buffer), "enumerate instance extensions");
            List<string> names = new();
            for (int index = 0; index < written; index++)
            {
                XrExtensionProperties extension = Marshal.PtrToStructure<XrExtensionProperties>(
                    IntPtr.Add(buffer, index * elementSize));
                names.Add(DecodeFixedUtf8(extension.ExtensionName));
            }
            return names;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string FindLoader()
    {
        string? executable = Process.GetCurrentProcess().MainModule?.FileName;
        string gameRoot = executable is null
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(executable) ?? Directory.GetCurrentDirectory();
        string loaderPath = Path.Combine(gameRoot, "vrmod", "runtime", "openxr_loader.dll");
        if (!File.Exists(loaderPath))
        {
            throw new FileNotFoundException("OpenXR loader is missing.", loaderPath);
        }
        return loaderPath;
    }

    private static T LoadExport<T>(IntPtr loader, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(loader, name));

    private static void Check(int result, string operation)
    {
        if (result != XrSuccess)
        {
            throw new InvalidOperationException($"{operation} failed: {result}.");
        }
    }

    private static byte[] FixedUtf8(string value, int length)
    {
        byte[] buffer = new byte[length];
        Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
        return buffer;
    }

    private static string DecodeFixedUtf8(byte[] value)
    {
        int length = Array.IndexOf(value, (byte)0);
        if (length < 0)
        {
            length = value.Length;
        }
        return Encoding.UTF8.GetString(value, 0, length);
    }

    private static ulong MakeVersion(ushort major, ushort minor, uint patch) =>
        ((ulong)major << 48) | ((ulong)minor << 32) | patch;

    private static XrPosef IdentityPose() => new()
    {
        Orientation = new XrQuaternionf { W = 1f },
        Position = new XrVector3f()
    };

    private static bool TryPrepareStereoLayer(
        IntPtr loader,
        IntPtr session,
        long presentationGeneration,
        IntPtr viewSpace,
        LocatedStereoViews views,
        IntPtr gameSwapChain,
        PanelResources panel,
        AcquireSwapchainImageDelegate acquireImage,
        WaitSwapchainImageDelegate waitImage,
        ReleaseSwapchainImageDelegate releaseImage,
        ref EyeConsumerResources? eyeResources,
        ref PendingEyeCopy? pendingEyeCopy,
        IntPtr projectionViewsPointer,
        IntPtr projectionLayerPointer,
        IntPtr uiLayerPointer,
        out bool uiLayerReady,
        out bool panelImageReserved,
        out ulong sequence,
        out ulong frameFenceValue)
    {
        uiLayerReady = false;
        panelImageReserved = false;
        sequence = 0;
        frameFenceValue = 0;

        if (pendingEyeCopy is not null)
        {
            return TryCompletePendingEyeCopy(
                presentationGeneration,
                viewSpace,
                views,
                panel,
                projectionViewsPointer,
                projectionLayerPointer,
                uiLayerPointer,
                ref pendingEyeCopy,
                out uiLayerReady,
                out panelImageReserved,
                out sequence,
                out frameFenceValue);
        }

        if (!UnityD3D12InterfaceProbe.IsEyeCopyBridgeReady(presentationGeneration))
        {
            return false;
        }

        D3D11StereoTextureLease? source =
            UnityRenderSourceRegistry.AcquireStereoTextures(750, presentationGeneration);
        if (source is null || source.PresentationGeneration != presentationGeneration)
        {
            source?.Dispose();
            return false;
        }
        if (source.RequiresDynamicUi && source.UiWorldTexture == IntPtr.Zero)
        {
            source.Dispose();
            return false;
        }
        if (!D3D12DeviceCapture.IsPresentationGenerationCurrent(presentationGeneration))
        {
            source.Dispose();
            return false;
        }

        D3D12ResourceDescription leftDescription;
        D3D12ResourceDescription rightDescription;
        try
        {
            leftDescription = D3D12Interop.GetResourceDescription(source.LeftTexture);
            rightDescription = D3D12Interop.GetResourceDescription(source.RightTexture);
            if (!AreCopyCompatible(leftDescription, rightDescription))
            {
                throw new InvalidOperationException(
                    "Published stereo resources do not have matching D3D12 descriptions.");
            }

            EnsureEyeConsumerResources(
                loader,
                session,
                leftDescription,
                ref eyeResources);
        }
        catch
        {
            source.Dispose();
            throw;
        }
        EyeConsumerResources eyes = eyeResources ??
            throw new InvalidOperationException("Eye swapchains were not created.");

        uint leftIndex = 0;
        uint rightIndex = 0;
        bool leftAcquired = false;
        bool rightAcquired = false;
        uint uiIndex = 0;
        bool uiAcquired = false;
        IntPtr uiCompositeSource = IntPtr.Zero;
        bool requestQueued = false;
        bool ownershipTransferred = false;
        try
        {
            leftIndex = AcquireAndWaitEyeImage(
                eyes.Left.Swapchain,
                acquireImage,
                waitImage);
            leftAcquired = true;
            rightIndex = AcquireAndWaitEyeImage(
                eyes.Right.Swapchain,
                acquireImage,
                waitImage);
            rightAcquired = true;

            if (source.RequiresDynamicUi)
            {
                uiIndex = AcquireAndWaitEyeImage(
                    panel.Swapchain,
                    acquireImage,
                    waitImage);
                uiAcquired = true;
                uiCompositeSource = D3D12Interop.GetSwapChainBackBuffer(gameSwapChain);
                if (uiCompositeSource == IntPtr.Zero)
                {
                    return false;
                }
            }

            IntPtr destinationLeft = eyes.Left.Images[leftIndex].Texture;
            IntPtr destinationRight = eyes.Right.Images[rightIndex].Texture;
            D3D12ResourceDescription destinationLeftDescription =
                D3D12Interop.GetResourceDescription(destinationLeft);
            D3D12ResourceDescription destinationRightDescription =
                D3D12Interop.GetResourceDescription(destinationRight);
            if (!AreCopyCompatible(leftDescription, destinationLeftDescription) ||
                !AreCopyCompatible(rightDescription, destinationRightDescription))
            {
                throw new InvalidOperationException(
                    "OpenXR eye images are not CopyResource-compatible with the published eye pair: " +
                    $"sourceLeft={DescribeCopyResource(leftDescription)};" +
                    $"destinationLeft={DescribeCopyResource(destinationLeftDescription)};" +
                    $"sourceRight={DescribeCopyResource(rightDescription)};" +
                    $"destinationRight={DescribeCopyResource(destinationRightDescription)}.");
            }
            if (!D3D12DeviceCapture.IsPresentationGenerationCurrent(presentationGeneration))
            {
                return false;
            }

            sequence = checked((ulong)source.Sequence);
            if (!UnityD3D12InterfaceProbe.TryQueueEyeCopy(
                    presentationGeneration,
                    sequence,
                    source.LeftTexture,
                    source.RightTexture,
                    destinationLeft,
                    destinationRight,
                    uiCompositeSource,
                    source.UiWorldTexture,
                    source.RequiresDynamicUi
                        ? panel.Images[checked((int)uiIndex)].Texture
                        : IntPtr.Zero))
            {
                throw new InvalidOperationException(
                    "The Unity D3D12 eye-copy bridge rejected the coherent pair request.");
            }
            requestQueued = true;
            pendingEyeCopy = new PendingEyeCopy(
                presentationGeneration,
                sequence,
                source,
                eyes,
                leftIndex,
                rightIndex,
                leftAcquired,
                rightAcquired,
                panel,
                uiIndex,
                uiAcquired,
                uiCompositeSource,
                releaseImage);
            ownershipTransferred = true;
            source = null;
            uiCompositeSource = IntPtr.Zero;
            leftAcquired = false;
            rightAcquired = false;
            uiAcquired = false;

            return TryCompletePendingEyeCopy(
                presentationGeneration,
                viewSpace,
                views,
                panel,
                projectionViewsPointer,
                projectionLayerPointer,
                uiLayerPointer,
                ref pendingEyeCopy,
                out uiLayerReady,
                out panelImageReserved,
                out sequence,
                out frameFenceValue);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                if (requestQueued)
                {
                    DrainPendingEyeCopyBeforeRelease(presentationGeneration, sequence);
                }
                XrSwapchainImageReleaseInfo releaseInfo = new() { Type = 57 };
                if (uiAcquired)
                {
                    _ = releaseImage(panel.Swapchain, ref releaseInfo);
                }
                if (rightAcquired)
                {
                    _ = releaseImage(eyes.Right.Swapchain, ref releaseInfo);
                }
                if (leftAcquired)
                {
                    _ = releaseImage(eyes.Left.Swapchain, ref releaseInfo);
                }
                D3D12Interop.Release(uiCompositeSource);
                source?.Dispose();
            }
        }
    }

    private static void DrainPendingEyeCopyBeforeRelease(
        long presentationGeneration,
        ulong sequence)
    {
        if (!UnityD3D12InterfaceProbe.TryPollEyeCopy(out D3D12EyeCopyStatus status) ||
            status.PresentationGeneration != presentationGeneration ||
            status.Sequence != sequence)
        {
            throw new InvalidOperationException(
                "The Unity D3D12 eye-copy bridge returned an invalid cleanup status.");
        }
        if (!status.IsCompleted && !status.IsFailed && !status.IsCanceled)
        {
            throw new InvalidOperationException(
                "A queued eye-copy request was not transferred before cleanup.");
        }
    }

    private static bool TryCompletePendingEyeCopy(
        long presentationGeneration,
        IntPtr viewSpace,
        LocatedStereoViews views,
        PanelResources panel,
        IntPtr projectionViewsPointer,
        IntPtr projectionLayerPointer,
        IntPtr uiLayerPointer,
        ref PendingEyeCopy? pendingEyeCopy,
        out bool uiLayerReady,
        out bool panelImageReserved,
        out ulong sequence,
        out ulong frameFenceValue)
    {
        PendingEyeCopy pending = pendingEyeCopy ??
            throw new InvalidOperationException("No pending eye-copy request is available.");
        uiLayerReady = false;
        panelImageReserved = pending.UiImageAcquired;
        sequence = pending.Sequence;
        frameFenceValue = 0;

        ObserveEyeCopyTelemetry(pending.PresentationGeneration, pending.Sequence);
        bool pollSucceeded = UnityD3D12InterfaceProbe.TryPollEyeCopy(
            out D3D12EyeCopyStatus status);
        ObserveEyeCopyTelemetry(pending.PresentationGeneration, pending.Sequence);
        if (!pollSucceeded ||
            status.PresentationGeneration != pending.PresentationGeneration ||
            status.Sequence != pending.Sequence)
        {
            if (!pollSucceeded &&
                status.PresentationGeneration == pending.PresentationGeneration &&
                status.Sequence == pending.Sequence &&
                IsObservedEyeCopy(pending.PresentationGeneration, pending.Sequence) &&
                !_observedManagedPollFailureLogged)
            {
                _observedManagedPollFailureLogged = true;
                Append("d3d12-eye-copy-failure-or-quarantine", null, new()
                {
                    ["requestCorrelationId"] = EyeCopyCorrelationId(
                        pending.PresentationGeneration,
                        pending.Sequence),
                    ["presentationGeneration"] = pending.PresentationGeneration,
                    ["stereoSequence"] = pending.Sequence,
                    ["stage"] = "managed-poll-status-schema-rejected",
                    ["state"] = status.State,
                    ["hresult"] = $"0x{status.Hresult:x8}",
                    ["nativeSchema"] = status.NativeSchema,
                    ["acceptedSchema"] = 1,
                    ["targetFenceValue"] = status.CompletionFenceValue,
                    ["completedFenceValue"] = status.CompletedFenceValue
                });
            }
            return false;
        }

        frameFenceValue = status.CompletionFenceValue;
        if (status.IsPending)
        {
            if (status.State == 1 && pending.ElapsedMilliseconds >= 2_000)
            {
                _ = UnityD3D12InterfaceProbe.TryCancelEyeCopy(
                    pending.PresentationGeneration,
                    pending.Sequence);
            }
            else if (status.State == 2 &&
                pending.ElapsedMilliseconds >= 2_000 &&
                pending.TryMarkSubmittedStallLogged())
            {
                Append("d3d12-openxr-eye-copy-submitted-fail-open", null, new()
                {
                    ["presentationGeneration"] = pending.PresentationGeneration,
                    ["stereoSequence"] = pending.Sequence,
                    ["completionFenceValue"] = status.CompletionFenceValue,
                    ["completedFenceValue"] = status.CompletedFenceValue,
                    ["acceptedFallback"] = "panel-only",
                    ["resourcePolicy"] = "retain-until-authoritative-gpu-completion"
                });
            }
            return false;
        }

        bool completed = status.IsCompleted;
        bool canceled = status.IsCanceled;
        bool failed = status.IsFailed;
        if (completed && IsObservedEyeCopy(pending.PresentationGeneration, pending.Sequence))
        {
            Append("d3d12-eye-copy-completed-returned-managed", null, new()
            {
                ["requestCorrelationId"] = EyeCopyCorrelationId(
                    pending.PresentationGeneration,
                    pending.Sequence),
                ["presentationGeneration"] = pending.PresentationGeneration,
                ["stereoSequence"] = pending.Sequence,
                ["state"] = status.State,
                ["hresult"] = $"0x{status.Hresult:x8}",
                ["targetFenceValue"] = status.CompletionFenceValue,
                ["completedFenceValue"] = status.CompletedFenceValue
            });
        }
        bool fresh = pending.SourcePublishedTimestamp > 0 &&
            (Stopwatch.GetTimestamp() - pending.SourcePublishedTimestamp) * 1_000.0 /
                Stopwatch.Frequency <= 750;
        bool generationCurrent =
            pending.PresentationGeneration == presentationGeneration &&
            D3D12DeviceCapture.IsPresentationGenerationCurrent(presentationGeneration);

        if (completed && fresh && generationCurrent)
        {
            WriteProjectionLayer(
                views,
                pending.Eyes,
                viewSpace,
                projectionViewsPointer,
                projectionLayerPointer);
            if (pending.RequiresDynamicUi)
            {
                XrCompositionLayerQuad uiLayer = Marshal.PtrToStructure<XrCompositionLayerQuad>(
                    uiLayerPointer);
                uiLayer.LayerFlags =
                    XrCompositionLayerBlendTextureSourceAlphaBit |
                    XrCompositionLayerUnpremultipliedAlphaBit;
                uiLayer.SubImage.ImageArrayIndex = 0;
                uiLayer.SubImage.ImageRect.Offset = new XrOffset2Di();
                uiLayer.SubImage.ImageRect.Extent = new XrExtent2Di
                {
                    Width = checked((int)panel.Width),
                    Height = checked((int)panel.Height)
                };
                Marshal.StructureToPtr(uiLayer, uiLayerPointer, fDeleteOld: false);
                uiLayerReady = true;
            }
        }

        pending.ReleaseAfterTerminal();
        pendingEyeCopy = null;
        panelImageReserved = false;

        if (failed)
        {
            throw new InvalidOperationException(
                $"Unity eye-copy submission failed: state={status.State};" +
                $"hresult=0x{status.Hresult:x8};fence={status.CompletionFenceValue};" +
                $"completed={status.CompletedFenceValue}.");
        }
        if (canceled || !completed || !fresh || !generationCurrent)
        {
            return false;
        }
        return true;
    }

    private static void WriteProjectionLayer(
        LocatedStereoViews views,
        EyeConsumerResources eyes,
        IntPtr viewSpace,
        IntPtr projectionViewsPointer,
        IntPtr projectionLayerPointer)
    {
        int projectionViewSize = Marshal.SizeOf<XrCompositionLayerProjectionView>();
        Marshal.StructureToPtr(
            CreateProjectionView(views.Left, eyes.Left, imageIndex: 0),
            projectionViewsPointer,
            fDeleteOld: false);
        Marshal.StructureToPtr(
            CreateProjectionView(views.Right, eyes.Right, imageIndex: 0),
            IntPtr.Add(projectionViewsPointer, projectionViewSize),
            fDeleteOld: false);
        Marshal.StructureToPtr(
            new XrCompositionLayerProjection
            {
                Type = XrTypeCompositionLayerProjection,
                Space = viewSpace,
                ViewCount = 2,
                Views = projectionViewsPointer
            },
            projectionLayerPointer,
            fDeleteOld: false);
    }

    private static void ObserveEyeCopyTelemetry(long presentationGeneration, ulong sequence)
    {
        if (!UnityD3D12InterfaceProbe.TryGetEyeCopyTelemetry(
                out D3D12EyeCopyTelemetry telemetry) ||
            telemetry.PresentationGeneration != presentationGeneration ||
            telemetry.Sequence != sequence)
        {
            return;
        }

        if (_observedEyeCopySequence == 0)
        {
            _observedEyeCopyGeneration = presentationGeneration;
            _observedEyeCopySequence = sequence;
        }
        if (!IsObservedEyeCopy(presentationGeneration, sequence))
        {
            return;
        }

        EmitEyeCopyTraceFlag(telemetry, EyeTraceDelivered,
            "d3d12-eye-copy-native-delivered", "native-delivered");
        EmitEyeCopyTraceFlag(telemetry, EyeTraceCallbackAcquired,
            "d3d12-eye-copy-callback-acquired", "plugin-callback-request-acquired");
        EmitEyeCopyTraceFlag(telemetry, EyeTraceCommandReady,
            "d3d12-eye-copy-command-list-ready", "command-list-close-ready");
        EmitEyeCopyTraceFlag(telemetry, EyeTraceExecuteBefore,
            "d3d12-eye-copy-execute-before", "execute-command-lists-before");
        EmitEyeCopyTraceFlag(telemetry, EyeTraceExecuteReturned,
            "d3d12-eye-copy-execute-returned", "execute-command-lists-call-returned");
        EmitEyeCopyTraceFlag(telemetry, EyeTraceSignalResult,
            "d3d12-eye-copy-signal-result", "plugin-fence-signal-result");
        EmitEyeCopyTraceFlag(telemetry, EyeTraceFenceFirstObserved,
            "d3d12-eye-copy-fence-first-observed", "plugin-fence-first-observed");
        EmitEyeCopyTraceFlag(telemetry, EyeTraceFenceTerminal,
            "d3d12-eye-copy-fence-terminal", "plugin-fence-terminal-completion");
        EmitEyeCopyTraceFlag(telemetry, EyeTraceFailureOrQuarantine,
            "d3d12-eye-copy-failure-or-quarantine", EyeCopyStageName(telemetry.FailureStage));
    }

    private static void EmitEyeCopyTraceFlag(
        D3D12EyeCopyTelemetry telemetry,
        uint flag,
        string eventName,
        string stage)
    {
        if ((telemetry.Flags & flag) == 0 || (_observedEyeCopyFlags & flag) != 0)
        {
            return;
        }

        _observedEyeCopyFlags |= flag;
        Dictionary<string, object> fields = new()
        {
            ["requestCorrelationId"] = EyeCopyCorrelationId(
                telemetry.PresentationGeneration,
                telemetry.Sequence),
            ["presentationGeneration"] = telemetry.PresentationGeneration,
            ["stereoSequence"] = telemetry.Sequence,
            ["stage"] = stage,
            ["state"] = telemetry.State,
            ["hresult"] = $"0x{telemetry.Hresult:x8}",
            ["targetFenceValue"] = telemetry.TargetFenceValue,
            ["firstCompletedFenceValue"] = telemetry.FirstCompletedFenceValue,
            ["latestCompletedFenceValue"] = telemetry.LatestCompletedFenceValue
        };
        if (flag == EyeTraceExecuteReturned)
        {
            fields["result"] = "call-returned-no-hresult-api";
        }
        if (flag == EyeTraceFailureOrQuarantine)
        {
            fields["failureStage"] = EyeCopyStageName(telemetry.FailureStage);
        }
        Append(eventName, null, fields);
    }

    private static bool IsObservedEyeCopy(long presentationGeneration, ulong sequence) =>
        sequence != 0 &&
        _observedEyeCopyGeneration == presentationGeneration &&
        _observedEyeCopySequence == sequence;

    private static string EyeCopyCorrelationId(long presentationGeneration, ulong sequence) =>
        $"{presentationGeneration}:{sequence}";

    private static string EyeCopyStageName(uint stage) => stage switch
    {
        1 => "native-delivered",
        2 => "plugin-callback-request-acquired",
        3 => "validate-resources",
        4 => "create-pipeline",
        5 => "create-descriptors",
        6 => "create-command-allocator",
        7 => "create-command-list",
        8 => "command-list-close-ready",
        9 => "create-fence",
        10 => "execute-command-lists-before",
        11 => "execute-command-lists-call-returned",
        12 => "plugin-fence-signal-result",
        13 => "plugin-fence-first-observed",
        14 => "plugin-fence-terminal-completion",
        15 => "pending-request-canceled",
        16 => "device-removed-during-fence-poll",
        _ => $"unknown-{stage}"
    };

    private static void EnsureEyeConsumerResources(
        IntPtr loader,
        IntPtr session,
        D3D12ResourceDescription sourceDescription,
        ref EyeConsumerResources? resources)
    {
        if (resources is not null && resources.Matches(sourceDescription))
        {
            return;
        }

        resources?.Dispose();
        resources = null;
        PanelResources? left = CreatePanel(
            loader,
            session,
            sourceDescription,
            out int leftResult,
            out string leftStage);
        if (left is null)
        {
            throw new InvalidOperationException(
                $"Left OpenXR eye swapchain creation failed: {leftStage}/{leftResult}.");
        }
        try
        {
            PanelResources? right = CreatePanel(
                loader,
                session,
                sourceDescription,
                out int rightResult,
                out string rightStage);
            if (right is null)
            {
                throw new InvalidOperationException(
                    $"Right OpenXR eye swapchain creation failed: {rightStage}/{rightResult}.");
            }
            if (!IsSrgbSwapchainFormatForSource(sourceDescription.Format, left.Format) ||
                !IsSrgbSwapchainFormatForSource(sourceDescription.Format, right.Format))
            {
                right.Dispose();
                throw new InvalidOperationException(
                    "OpenXR did not expose an sRGB swapchain format compatible with " +
                    $"the published Unity eye texture: source={sourceDescription.Format};" +
                    $"left={left.Format};right={right.Format}.");
            }
            resources = new EyeConsumerResources(left, right, sourceDescription);
        }
        catch
        {
            left.Dispose();
            throw;
        }
    }

    private static uint AcquireAndWaitEyeImage(
        IntPtr swapchain,
        AcquireSwapchainImageDelegate acquireImage,
        WaitSwapchainImageDelegate waitImage)
    {
        XrSwapchainImageAcquireInfo acquireInfo = new() { Type = 55 };
        Check(acquireImage(swapchain, ref acquireInfo, out uint index), "acquire eye image");
        XrSwapchainImageWaitInfo waitInfo = new()
        {
            Type = 56,
            Timeout = 1_000_000_000
        };
        Check(waitImage(swapchain, ref waitInfo), "wait eye image");
        return index;
    }

    private static bool AreCopyCompatible(
        D3D12ResourceDescription left,
        D3D12ResourceDescription right) =>
        left.Dimension == right.Dimension &&
        left.Width == right.Width &&
        left.Height == right.Height &&
        left.DepthOrArraySize == right.DepthOrArraySize &&
        left.MipLevels == right.MipLevels &&
        AreDxgiFormatsCopyCompatible(left.Format, right.Format) &&
        left.SampleCount == right.SampleCount &&
        left.SampleQuality == right.SampleQuality;

    private static bool AreDxgiFormatsCopyCompatible(int left, int right)
    {
        if (left == right)
        {
            return true;
        }

        int? leftFamily = GetDxgiTypelessFamily(left);
        return leftFamily.HasValue && leftFamily == GetDxgiTypelessFamily(right);
    }

    private static bool IsSrgbSwapchainFormatForSource(int sourceFormat, long swapchainFormat) =>
        swapchainFormat switch
        {
            29 => sourceFormat is 27 or 28 or 29,
            91 => sourceFormat is 87 or 90 or 91,
            _ => false
        };

    private static int? GetDxgiTypelessFamily(int format) => format switch
    {
        >= 1 and <= 4 => 1,      // R32G32B32A32
        >= 5 and <= 8 => 5,      // R32G32B32
        >= 9 and <= 14 => 9,     // R16G16B16A16
        >= 15 and <= 18 => 15,   // R32G32
        >= 19 and <= 22 => 19,   // R32G8X24
        >= 23 and <= 25 => 23,   // R10G10B10A2
        >= 27 and <= 32 => 27,   // R8G8B8A8
        >= 33 and <= 38 => 33,   // R16G16
        >= 39 and <= 43 => 39,   // R32
        >= 44 and <= 47 => 44,   // R24G8
        >= 48 and <= 52 => 48,   // R8G8
        >= 53 and <= 59 => 53,   // R16
        >= 60 and <= 64 => 60,   // R8
        >= 70 and <= 72 => 70,   // BC1
        >= 73 and <= 75 => 73,   // BC2
        >= 76 and <= 78 => 76,   // BC3
        >= 79 and <= 81 => 79,   // BC4
        >= 82 and <= 84 => 82,   // BC5
        87 or 90 or 91 => 90,    // B8G8R8A8
        88 or 92 or 93 => 92,    // B8G8R8X8
        >= 94 and <= 96 => 94,   // BC6H
        >= 97 and <= 99 => 97,   // BC7
        _ => null
    };

    private static string DescribeCopyResource(D3D12ResourceDescription description) =>
        $"dimension={description.Dimension},width={description.Width},height={description.Height}," +
        $"depthOrArray={description.DepthOrArraySize},mips={description.MipLevels}," +
        $"format={description.Format},samples={description.SampleCount}," +
        $"sampleQuality={description.SampleQuality},layout={description.Layout},flags={description.Flags}";

    private static bool TryCopyPanelImage(
        PanelResources panel,
        IntPtr gameSwapChain,
        IntPtr device,
        IntPtr queue,
        AcquireSwapchainImageDelegate acquireImage,
        WaitSwapchainImageDelegate waitImage,
        ReleaseSwapchainImageDelegate releaseImage,
        bool drawCursor,
        float pointerU,
        float pointerV,
        out int cursorDrawResult)
    {
        cursorDrawResult = 0;
        XrSwapchainImageAcquireInfo acquireInfo = new() { Type = 55 };
        int acquireResult = acquireImage(panel.Swapchain, ref acquireInfo, out uint imageIndex);
        if (acquireResult != XrSuccess)
        {
            return false;
        }

        try
        {
            XrSwapchainImageWaitInfo waitInfo = new()
            {
                Type = 56,
                Timeout = 1000000000
            };
            if (waitImage(panel.Swapchain, ref waitInfo) != XrSuccess ||
                imageIndex >= panel.Images.Length)
            {
                return false;
            }

            IntPtr gameBackBuffer = IntPtr.Zero;
            try
            {
                gameBackBuffer = D3D12Interop.GetSwapChainBackBuffer(gameSwapChain);
                if (gameBackBuffer == IntPtr.Zero)
                {
                    return false;
                }
                D3D12Interop.CopyResource(
                    device,
                    queue,
                    gameBackBuffer,
                    panel.Images[checked((int)imageIndex)].Texture);
                cursorDrawResult = DrawPanelCursor(
                    device,
                    queue,
                    panel.Images[checked((int)imageIndex)].Texture,
                    drawCursor,
                    pointerU,
                    pointerV);
                return true;
            }
            finally
            {
                D3D12Interop.Release(gameBackBuffer);
            }
        }
        finally
        {
            XrSwapchainImageReleaseInfo releaseInfo = new() { Type = 57 };
            _ = releaseImage(panel.Swapchain, ref releaseInfo);
        }
    }

    private static void ConfigureHandPanelLayer(
        ref XrCompositionLayerQuad layer,
        PanelResources panel,
        IntPtr viewSpace,
        OpenXrControllerPose handPose,
        XrView leftView,
        XrView rightView,
        VrPanelSettings settings)
    {
        float aspectRatio = panel.Width / (float)panel.Height;
        float width = settings.MaximumWidth;
        float height = width / aspectRatio;
        if (height > settings.MaximumHeight)
        {
            height = settings.MaximumHeight;
            width = height * aspectRatio;
        }

        layer.LayerFlags = 0;
        layer.Space = viewSpace;
        layer.Pose = CreateHandPanelPoseInView(
            handPose,
            leftView,
            rightView,
            settings);
        layer.Size = new XrExtent2Df { Width = width, Height = height };
        layer.SubImage.Swapchain = panel.Swapchain;
        layer.SubImage.ImageArrayIndex = 0;
        layer.SubImage.ImageRect.Offset = new XrOffset2Di();
        layer.SubImage.ImageRect.Extent = new XrExtent2Di
        {
            Width = checked((int)panel.Width),
            Height = checked((int)panel.Height)
        };
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
        XrVector3f panelPosition = Add(
            handPosition,
            Rotate(
                handOrientation,
                new XrVector3f
                {
                    X = settings.OffsetX,
                    Y = settings.OffsetY,
                    Z = settings.OffsetZ
                }));
        XrVector3f eyeMidpoint = new()
        {
            X = (leftView.Pose.Position.X + rightView.Pose.Position.X) * 0.5f,
            Y = (leftView.Pose.Position.Y + rightView.Pose.Position.Y) * 0.5f,
            Z = (leftView.Pose.Position.Z + rightView.Pose.Position.Z) * 0.5f
        };
        XrQuaternionf baseOrientation;
        if (settings.ViewerFacing)
        {
            XrVector3f panelToEye = Subtract(eyeMidpoint, panelPosition);
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
        return new XrPosef
        {
            Orientation = Multiply(
                baseOrientation,
                QuaternionFromEulerDegrees(
                    settings.RotationPitch,
                    settings.RotationYaw,
                    settings.RotationRoll)),
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

        XrVector3f localHit = Rotate(
            new XrQuaternionf
            {
                X = -panelPose.Orientation.X,
                Y = -panelPose.Orientation.Y,
                Z = -panelPose.Orientation.Z,
                W = panelPose.Orientation.W
            },
            Subtract(
                Add(rayOrigin, Scale(rayDirection, distance)),
                panelPose.Position));
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

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DrawPanelCursorDelegate(
        IntPtr device,
        IntPtr queue,
        IntPtr destination,
        float u,
        float v,
        float relativeSize);

    private static DrawPanelCursorDelegate? LoadPanelCursorExport()
    {
        if (_drawPanelCursorLoadAttempted)
        {
            return _drawPanelCursor;
        }
        _drawPanelCursorLoadAttempted = true;
        string gameRoot = Path.GetDirectoryName(
                Process.GetCurrentProcess().MainModule?.FileName)
            ?? Directory.GetCurrentDirectory();
        string libraryPath = Path.Combine(
            gameRoot,
            "vrmod",
            "runtime",
            "SongPrismVR.UnityD3D12Probe.dll");
        if (!File.Exists(libraryPath))
        {
            return null;
        }

        IntPtr library = NativeLibrary.Load(libraryPath);
        _drawPanelCursor = Marshal.GetDelegateForFunctionPointer<DrawPanelCursorDelegate>(
            NativeLibrary.GetExport(library, "spvr_draw_panel_cursor"));
        return _drawPanelCursor;
    }

    private static int DrawPanelCursor(
        IntPtr device,
        IntPtr queue,
        IntPtr destination,
        bool drawCursor,
        float u,
        float v)
    {
        if (!drawCursor)
        {
            return 0;
        }
        try
        {
            DrawPanelCursorDelegate? draw = LoadPanelCursorExport();
            return draw is null
                ? unchecked((int)0x8007007E) /* ERROR_MOD_NOT_FOUND */
                : draw(device, queue, destination, u, v, PanelCursorRelativeSize);
        }
        catch (Exception exception)
        {
            return exception.HResult != 0
                ? exception.HResult
                : unchecked((int)0x80004005);
        }
    }

    private static void LogCursorDrawFailure(
        int result,
        bool cursorRequested,
        ref DateTimeOffset nextFailureUtc)
    {
        if (!cursorRequested || result >= 0)
        {
            return;
        }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (now < nextFailureUtc)
        {
            return;
        }
        nextFailureUtc = now.AddSeconds(10);
        Append("d3d12-openxr-cursor-fail-open", null, new()
        {
            ["result"] = $"0x{result:x8}",
            ["composition"] = "panel-image-alpha-blend",
            ["acceptedFallback"] = "panel-without-cursor"
        });
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

    private static XrCompositionLayerProjectionView CreateProjectionView(
        XrView view,
        PanelResources eye,
        uint imageIndex) => new()
        {
            Type = XrTypeCompositionLayerProjectionView,
            Pose = view.Pose,
            Fov = view.Fov,
            SubImage = new XrSwapchainSubImage
            {
                Swapchain = eye.Swapchain,
                ImageRect = new XrRect2Di
                {
                    Offset = new XrOffset2Di(),
                    Extent = new XrExtent2Di
                    {
                        Width = checked((int)eye.Width),
                        Height = checked((int)eye.Height)
                    }
                },
                ImageArrayIndex = 0
            }
        };

    private static LocatedStereoViews? UpdateStereoState(
        LocateViewsDelegate locateViews,
        IntPtr session,
        IntPtr viewSpace,
        IntPtr worldSpace,
        long displayTime)
    {
        if (locateViews is null || session == IntPtr.Zero || viewSpace == IntPtr.Zero)
        {
            return null;
        }

        XrViewLocateInfo locateInfo = new()
        {
            Type = XrTypeViewLocateInfo,
            ViewConfigurationType = XrPrimaryStereoViewConfiguration,
            DisplayTime = displayTime,
            Space = viewSpace
        };
        XrViewState viewState = new() { Type = XrTypeViewState };
        int viewSize = Marshal.SizeOf<XrView>();
        IntPtr views = Marshal.AllocHGlobal(2 * viewSize);
        try
        {
            Marshal.StructureToPtr(
                new XrView { Type = XrTypeView },
                views,
                fDeleteOld: false);
            Marshal.StructureToPtr(
                new XrView { Type = XrTypeView },
                IntPtr.Add(views, viewSize),
                fDeleteOld: false);
            int locateResult = locateViews(
                session,
                ref locateInfo,
                ref viewState,
                2,
                out uint viewCount,
                views);
            if (locateResult != XrSuccess || viewCount < 2)
            {
                DateTimeOffset diagnosticNow = DateTimeOffset.UtcNow;
                if (diagnosticNow >= _nextStereoViewDiagnosticUtc)
                {
                    _nextStereoViewDiagnosticUtc = diagnosticNow.AddSeconds(5);
                    Append("d3d12-openxr-locate-views-failure", null, new()
                    {
                        ["locateResult"] = locateResult,
                        ["viewCount"] = viewCount,
                        ["viewStateFlags"] = viewState.ViewStateFlags
                    });
                }
                return null;
            }

            XrView left = Marshal.PtrToStructure<XrView>(views);
            XrView right = Marshal.PtrToStructure<XrView>(IntPtr.Add(views, viewSize));
            OpenXrStereoStateRegistry.UpdateViews(
                viewState.ViewStateFlags,
                CreateStereoEyeState(left),
                CreateStereoEyeState(right));

            if (worldSpace != IntPtr.Zero)
            {
                for (int index = 0; index < 2; index++)
                {
                    Marshal.StructureToPtr(
                        new XrView { Type = XrTypeView },
                        IntPtr.Add(views, index * viewSize),
                        fDeleteOld: false);
                }
                XrViewLocateInfo worldLocateInfo = new()
                {
                    Type = XrTypeViewLocateInfo,
                    ViewConfigurationType = XrPrimaryStereoViewConfiguration,
                    DisplayTime = displayTime,
                    Space = worldSpace
                };
                XrViewState worldViewState = new() { Type = XrTypeViewState };
                int worldLocateResult = locateViews(
                    session,
                    ref worldLocateInfo,
                    ref worldViewState,
                    2,
                    out uint worldViewCount,
                    views);
                if (worldLocateResult == XrSuccess && worldViewCount >= 2)
                {
                    XrView worldLeft = Marshal.PtrToStructure<XrView>(views);
                    XrView worldRight = Marshal.PtrToStructure<XrView>(
                        IntPtr.Add(views, viewSize));
                    OpenXrStereoStateRegistry.UpdateWorldViews(
                        worldViewState.ViewStateFlags,
                        CreateStereoEyeState(worldLeft),
                        CreateStereoEyeState(worldRight));
                }
            }
            return new LocatedStereoViews(left, right);
        }
        finally
        {
            Marshal.FreeHGlobal(views);
        }
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

    private static IntPtr CreateDebugUtilsMessenger(IntPtr loader, IntPtr instance)
    {
        GetInstanceProcAddrDelegate getInstanceProcAddr =
            LoadExport<GetInstanceProcAddrDelegate>(loader, "xrGetInstanceProcAddr");
        IntPtr functionName = Marshal.StringToCoTaskMemUTF8("xrCreateDebugUtilsMessengerEXT");
        try
        {
            int resolveResult = getInstanceProcAddr(instance, functionName, out IntPtr function);
            if (resolveResult != XrSuccess || function == IntPtr.Zero)
            {
                Append("d3d12-openxr-debug-messenger-unavailable", null, new()
                {
                    ["resolveResult"] = resolveResult,
                    ["functionResolved"] = function != IntPtr.Zero
                });
                return IntPtr.Zero;
            }

            CreateDebugUtilsMessengerDelegate create =
                Marshal.GetDelegateForFunctionPointer<CreateDebugUtilsMessengerDelegate>(function);
            XrDebugUtilsMessengerCreateInfo createInfo = new()
            {
                Type = XrTypeDebugUtilsMessengerCreateInfoExt,
                MessageSeverity =
                    XrDebugUtilsMessageSeverityErrorBitExt |
                    XrDebugUtilsMessageSeverityWarningBitExt |
                    XrDebugUtilsMessageSeverityInfoBitExt,
                MessageTypes =
                    XrDebugUtilsMessageTypeGeneralBitExt |
                    XrDebugUtilsMessageTypeValidationBitExt |
                    XrDebugUtilsMessageTypePerformanceBitExt,
                UserCallback = Marshal.GetFunctionPointerForDelegate(DebugUtilsMessengerCallback)
            };
            int createResult = create(instance, ref createInfo, out IntPtr messenger);
            Append("d3d12-openxr-debug-messenger-result", null, new()
            {
                ["createResult"] = createResult,
                ["messengerCreated"] = messenger != IntPtr.Zero
            });
            return createResult == XrSuccess ? messenger : IntPtr.Zero;
        }
        catch (Exception exception)
        {
            Append("d3d12-openxr-debug-messenger-failure", exception);
            return IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeCoTaskMem(functionName);
        }
    }

    private static void DestroyDebugUtilsMessenger(
        IntPtr loader,
        IntPtr instance,
        IntPtr messenger)
    {
        if (instance == IntPtr.Zero || messenger == IntPtr.Zero)
        {
            return;
        }

        GetInstanceProcAddrDelegate getInstanceProcAddr =
            LoadExport<GetInstanceProcAddrDelegate>(loader, "xrGetInstanceProcAddr");
        IntPtr functionName = Marshal.StringToCoTaskMemUTF8("xrDestroyDebugUtilsMessengerEXT");
        try
        {
            if (getInstanceProcAddr(instance, functionName, out IntPtr function) != XrSuccess ||
                function == IntPtr.Zero)
            {
                return;
            }

            DestroyDebugUtilsMessengerDelegate destroy =
                Marshal.GetDelegateForFunctionPointer<DestroyDebugUtilsMessengerDelegate>(function);
            _ = destroy(messenger);
        }
        catch
        {
            // Best-effort cleanup.
        }
        finally
        {
            Marshal.FreeCoTaskMem(functionName);
        }
    }

    private static int OnDebugUtilsMessage(
        ulong severity,
        ulong types,
        IntPtr callbackData,
        IntPtr userData)
    {
        try
        {
            if (callbackData == IntPtr.Zero)
            {
                return 0;
            }

            XrDebugUtilsMessengerCallbackData data =
                Marshal.PtrToStructure<XrDebugUtilsMessengerCallbackData>(callbackData);
            Append("d3d12-openxr-debug-utils-message", null, new()
            {
                ["severity"] = $"0x{severity:x16}",
                ["types"] = $"0x{types:x16}",
                ["messageId"] = ReadUtf8(data.MessageId),
                ["functionName"] = ReadUtf8(data.FunctionName),
                ["message"] = ReadUtf8(data.Message)
            });
        }
        catch
        {
            // Debug callbacks must never throw into the runtime.
        }

        return 0;
    }

    private static string ReadUtf8(IntPtr pointer) =>
        pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;

    private static int ReadSessionState(ref XrEventDataBuffer eventData)
    {
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<XrEventDataBuffer>());
        try
        {
            Marshal.StructureToPtr(eventData, buffer, fDeleteOld: false);
            XrEventDataSessionStateChanged data =
                Marshal.PtrToStructure<XrEventDataSessionStateChanged>(buffer);
            return data.State;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static void Append(string eventName, Exception? exception, Dictionary<string, object>? fields = null)
    {
        try
        {
            Dictionary<string, object?> record = new()
            {
                ["timestampUtc"] = DateTimeOffset.UtcNow,
                ["event"] = eventName,
                ["bootstrapVersion"] = RuntimeProbe.BootstrapVersion,
                ["processId"] = Environment.ProcessId,
                ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
                ["errorType"] = exception?.GetType().FullName,
                ["error"] = exception?.ToString()
            };
            if (fields is not null)
            {
                foreach ((string key, object value) in fields)
                {
                    record[key] = value;
                }
            }

            lock (LogLock)
            {
                File.AppendAllText(
                    RuntimeProbe.GetLogPath(),
                    JsonSerializer.Serialize(record) + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostic logging must never crash the bootstrap.
        }
    }

    private sealed record FrameLoopResult
    {
        public int Result { get; init; }
        public int FramesSubmitted { get; init; }
        public string Stage { get; init; } = string.Empty;
    }

    private readonly record struct LocatedStereoViews(XrView Left, XrView Right);

    private sealed class PendingEyeCopy
    {
        private D3D11StereoTextureLease? _source;
        private IntPtr _uiCompositeSource;
        private readonly ReleaseSwapchainImageDelegate _releaseImage;
        private readonly Stopwatch _elapsed = Stopwatch.StartNew();
        private int _submittedStallLogged;
        private bool _released;

        internal PendingEyeCopy(
            long presentationGeneration,
            ulong sequence,
            D3D11StereoTextureLease source,
            EyeConsumerResources eyes,
            uint leftIndex,
            uint rightIndex,
            bool leftImageAcquired,
            bool rightImageAcquired,
            PanelResources panel,
            uint uiIndex,
            bool uiImageAcquired,
            IntPtr uiCompositeSource,
            ReleaseSwapchainImageDelegate releaseImage)
        {
            PresentationGeneration = presentationGeneration;
            Sequence = sequence;
            _source = source;
            Eyes = eyes;
            LeftIndex = leftIndex;
            RightIndex = rightIndex;
            LeftImageAcquired = leftImageAcquired;
            RightImageAcquired = rightImageAcquired;
            Panel = panel;
            UiIndex = uiIndex;
            UiImageAcquired = uiImageAcquired;
            _uiCompositeSource = uiCompositeSource;
            _releaseImage = releaseImage;
        }

        internal long PresentationGeneration { get; }
        internal ulong Sequence { get; }
        internal EyeConsumerResources Eyes { get; }
        internal uint LeftIndex { get; }
        internal uint RightIndex { get; }
        internal bool LeftImageAcquired { get; }
        internal bool RightImageAcquired { get; }
        internal PanelResources Panel { get; }
        internal uint UiIndex { get; }
        internal bool UiImageAcquired { get; }
        internal bool RequiresDynamicUi => _source?.RequiresDynamicUi == true;
        internal long SourcePublishedTimestamp => _source?.PublishedTimestamp ?? 0;
        internal long ElapsedMilliseconds => _elapsed.ElapsedMilliseconds;

        internal bool TryMarkSubmittedStallLogged() =>
            Interlocked.Exchange(ref _submittedStallLogged, 1) == 0;

        internal void ReleaseAfterTerminal()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            XrSwapchainImageReleaseInfo releaseInfo = new() { Type = 57 };
            if (UiImageAcquired)
            {
                _ = _releaseImage(Panel.Swapchain, ref releaseInfo);
            }
            if (RightImageAcquired)
            {
                _ = _releaseImage(Eyes.Right.Swapchain, ref releaseInfo);
            }
            if (LeftImageAcquired)
            {
                _ = _releaseImage(Eyes.Left.Swapchain, ref releaseInfo);
            }
            D3D12Interop.Release(_uiCompositeSource);
            _uiCompositeSource = IntPtr.Zero;
            _source?.Dispose();
            _source = null;
        }
    }

    private sealed class EyeConsumerResources : IDisposable
    {
        private readonly D3D12ResourceDescription _description;

        internal EyeConsumerResources(
            PanelResources left,
            PanelResources right,
            D3D12ResourceDescription description)
        {
            Left = left;
            Right = right;
            _description = description;
        }

        internal PanelResources Left { get; }
        internal PanelResources Right { get; }

        internal bool Matches(D3D12ResourceDescription description) =>
            AreCopyCompatible(_description, description);

        public void Dispose()
        {
            Right.Dispose();
            Left.Dispose();
        }
    }

    private sealed class PanelResources : IDisposable
    {
        private readonly DestroySwapchainDelegate _destroySwapchain;
        private bool _disposed;

        public PanelResources(
            IntPtr swapchain,
            DestroySwapchainDelegate destroySwapchain,
            XrSwapchainImageD3D12[] images,
            long format,
            uint width,
            uint height)
        {
            Swapchain = swapchain;
            _destroySwapchain = destroySwapchain;
            Images = images;
            Format = format;
            Width = width;
            Height = height;
        }

        public IntPtr Swapchain { get; }
        public XrSwapchainImageD3D12[] Images { get; }
        public long Format { get; }
        public uint Width { get; }
        public uint Height { get; }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                if (Swapchain != IntPtr.Zero)
                {
                    _ = _destroySwapchain(Swapchain);
                }
            }
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
    private struct XrSystemGraphicsProperties
    {
        public uint MaxSwapchainImageWidth;
        public uint MaxSwapchainImageHeight;
        public uint MaxLayerCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSystemTrackingProperties
    {
        public uint OrientationTracking;
        public uint PositionTracking;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrGraphicsRequirementsD3D12
    {
        public int Type;
        public IntPtr Next;
        public Luid AdapterLuid;
        public int MinFeatureLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrGraphicsBindingD3D12
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Device;
        public IntPtr Queue;
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
    private struct XrReferenceSpaceCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public int ReferenceSpaceType;
        public XrPosef PoseInReferenceSpace;
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
    private struct XrSwapchainImageD3D12
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Texture;
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
    private struct XrDebugUtilsMessengerCallbackData
    {
        public int Type;
        public IntPtr Next;
        public IntPtr MessageId;
        public IntPtr FunctionName;
        public IntPtr Message;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrDebugUtilsMessengerCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public ulong MessageSeverity;
        public ulong MessageTypes;
        public IntPtr UserCallback;
        public IntPtr UserData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrCompositionLayerQuad
    {
        public int Type;
        public IntPtr Next;
        public ulong LayerFlags;
        public IntPtr Space;
        public uint EyeVisibility;
        public XrSwapchainSubImage SubImage;
        public XrPosef Pose;
        public XrExtent2Df Size;
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
    private struct XrCompositionLayerProjectionView
    {
        public int Type;
        public IntPtr Next;
        public XrPosef Pose;
        public XrFovf Fov;
        public XrSwapchainSubImage SubImage;
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
    private struct XrSessionBeginInfo
    {
        public int Type;
        public IntPtr Next;
        public int PrimaryViewConfigurationType;
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
    private struct XrEventDataSessionStateChanged
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Session;
        public int State;
        public long Time;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumerateInstanceExtensionPropertiesDelegate(
        IntPtr layerName,
        uint capacityInput,
        out uint countOutput,
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
    private delegate int GetSystemPropertiesDelegate(IntPtr instance, ulong systemId, ref XrSystemProperties properties);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetInstanceProcAddrDelegate(IntPtr instance, IntPtr name, out IntPtr function);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetD3D12GraphicsRequirementsDelegate(
        IntPtr instance,
        ulong systemId,
        ref XrGraphicsRequirementsD3D12 requirements);

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
        IntPtr createInfo,
        out IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroySessionDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateReferenceSpaceDelegate(
        IntPtr session,
        ref XrReferenceSpaceCreateInfo createInfo,
        out IntPtr space);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroySpaceDelegate(IntPtr space);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int PollEventDelegate(IntPtr instance, ref XrEventDataBuffer eventData);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DebugUtilsMessengerCallbackDelegate(
        ulong severity,
        ulong types,
        IntPtr callbackData,
        IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateDebugUtilsMessengerDelegate(
        IntPtr instance,
        ref XrDebugUtilsMessengerCreateInfo createInfo,
        out IntPtr messenger);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroyDebugUtilsMessengerDelegate(IntPtr messenger);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BeginSessionDelegate(IntPtr session, ref XrSessionBeginInfo beginInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EndSessionDelegate(IntPtr session);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int WaitFrameDelegate(IntPtr session, ref XrFrameWaitInfo frameWaitInfo, ref XrFrameState frameState);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int BeginFrameDelegate(IntPtr session, ref XrFrameBeginInfo frameBeginInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int LocateViewsDelegate(
        IntPtr session,
        ref XrViewLocateInfo viewLocateInfo,
        ref XrViewState viewState,
        uint viewCapacityInput,
        out uint viewCountOutput,
        IntPtr views);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EndFrameDelegate(IntPtr session, ref XrFrameEndInfo frameEndInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EnumerateSwapchainFormatsDelegate(
        IntPtr session,
        uint capacityInput,
        out uint countOutput,
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
        uint capacityInput,
        out uint countOutput,
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
}
