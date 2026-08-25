#define WIN32_LEAN_AND_MEAN
#define COBJMACROS
#include <windows.h>
#include <d3d12.h>
#include <d3dcompiler.h>
#include <math.h>
#include <stddef.h>
#include <stdint.h>

#define SPVR_PROBE_SCHEMA 1u
#define SPVR_FLAG_PLUGIN_LOAD 0x00000001u
#define SPVR_FLAG_RENDER_EVENT 0x00000002u
#define SPVR_FLAG_INTERFACE_FOUND 0x00000004u
#define SPVR_RENDER_EVENT_ID 0x53505652

#define SPVR_D3D12_EVENT_ENSURE_PREVIOUS_FRAME_SUBMISSION (1u << 0)
#define SPVR_D3D12_EVENT_FLUSH_COMMAND_BUFFERS (1u << 1)
#define SPVR_D3D12_EVENT_SYNC_WORKER_THREADS (1u << 2)
#define SPVR_D3D12_EVENT_MODIFIES_COMMAND_BUFFER_STATE (1u << 3)

typedef void IUnityInterface;

typedef struct UnityInterfaceGUID
{
    uint64_t high;
    uint64_t low;
} UnityInterfaceGUID;

typedef struct IUnityInterfaces
{
    IUnityInterface* (__stdcall *GetInterface)(UnityInterfaceGUID guid);
    void (__stdcall *RegisterInterface)(UnityInterfaceGUID guid, IUnityInterface* value);
    IUnityInterface* (__stdcall *GetInterfaceSplit)(uint64_t guidHigh, uint64_t guidLow);
    void (__stdcall *RegisterInterfaceSplit)(uint64_t guidHigh, uint64_t guidLow, IUnityInterface* value);
} IUnityInterfaces;

typedef void* (__stdcall *GetDeviceFn)(void);
typedef void* (__stdcall *GetFrameFenceFn)(void);
typedef uint64_t (__stdcall *GetNextFrameFenceValueFn)(void);
typedef void* (__stdcall *GetCommandQueueFn)(void);
typedef void* (__stdcall *GetSwapChainFn)(void);
typedef uint32_t (__stdcall *GetUInt32Fn)(void);
typedef uint64_t (__stdcall *ExecuteCommandListFn)(
    ID3D12GraphicsCommandList* commandList,
    int stateCount,
    void* states);

typedef struct UnityD3D12PluginEventConfig
{
    int graphicsQueueAccess;
    uint32_t flags;
    BOOL ensureActiveRenderTextureIsBound;
} UnityD3D12PluginEventConfig;

typedef void (__stdcall *ConfigureEventFn)(
    int eventId,
    const UnityD3D12PluginEventConfig* config);

typedef struct UnityGraphicsD3D12ResourceState
{
    ID3D12Resource* resource;
    D3D12_RESOURCE_STATES expected;
    D3D12_RESOURCE_STATES current;
} UnityGraphicsD3D12ResourceState;

typedef struct UnityD3D12V2Prefix
{
    GetDeviceFn GetDevice;
    GetFrameFenceFn GetFrameFence;
    GetNextFrameFenceValueFn GetNextFrameFenceValue;
    ExecuteCommandListFn ExecuteCommandList;
} UnityD3D12V2Prefix;

typedef struct UnityD3D12V4Prefix
{
    GetDeviceFn GetDevice;
    GetFrameFenceFn GetFrameFence;
    GetNextFrameFenceValueFn GetNextFrameFenceValue;
    ExecuteCommandListFn ExecuteCommandList;
    void* SetPhysicalVideoMemoryControlValues;
    GetCommandQueueFn GetCommandQueue;
} UnityD3D12V4Prefix;

typedef struct UnityD3D12V6Prefix
{
    GetDeviceFn GetDevice;
    GetFrameFenceFn GetFrameFence;
    GetNextFrameFenceValueFn GetNextFrameFenceValue;
    ExecuteCommandListFn ExecuteCommandList;
    void* SetPhysicalVideoMemoryControlValues;
    GetCommandQueueFn GetCommandQueue;
    void* TextureFromRenderBuffer;
    void* TextureFromNativeTexture;
    ConfigureEventFn ConfigureEvent;
} UnityD3D12V6Prefix;

/* v7 inserted swapchain/present accessors after GetDevice. It is not prefix-compatible
 * with v2-v6 beyond the first slot. Keep this declaration synchronized with Unity's
 * official IUnityGraphicsD3D12v7 interface. */
typedef struct UnityD3D12V7Prefix
{
    GetDeviceFn GetDevice;
    GetSwapChainFn GetSwapChain;
    GetUInt32Fn GetSyncInterval;
    GetUInt32Fn GetPresentFlags;
    GetFrameFenceFn GetFrameFence;
    GetNextFrameFenceValueFn GetNextFrameFenceValue;
    ExecuteCommandListFn ExecuteCommandList;
    void* SetPhysicalVideoMemoryControlValues;
    GetCommandQueueFn GetCommandQueue;
    void* TextureFromRenderBuffer;
    void* TextureFromNativeTexture;
    ConfigureEventFn ConfigureEvent;
} UnityD3D12V7Prefix;

_Static_assert(offsetof(UnityD3D12V4Prefix, GetCommandQueue) == 5u * sizeof(void*),
    "IUnityGraphicsD3D12v4-v6 queue slot changed");
_Static_assert(offsetof(UnityD3D12V7Prefix, GetFrameFence) == 4u * sizeof(void*),
    "IUnityGraphicsD3D12v7 frame-fence slot changed");
_Static_assert(offsetof(UnityD3D12V7Prefix, GetCommandQueue) == 8u * sizeof(void*),
    "IUnityGraphicsD3D12v7 queue slot changed");
_Static_assert(offsetof(UnityD3D12V6Prefix, ConfigureEvent) == 8u * sizeof(void*),
    "IUnityGraphicsD3D12v6 configure-event slot changed");
_Static_assert(offsetof(UnityD3D12V7Prefix, ConfigureEvent) == 11u * sizeof(void*),
    "IUnityGraphicsD3D12v7 configure-event slot changed");

typedef struct SpvrUnityD3D12ProbeSnapshot
{
    uint32_t schema;
    uint32_t flags;
    uint32_t pluginLoadThreadId;
    uint32_t renderEventThreadId;
    uint32_t highestInterfaceVersion;
    uint32_t reserved;
    uintptr_t unityInterfaces;
    uintptr_t unityD3D12Interface;
    uintptr_t unityDevice;
    uintptr_t unityCommandQueue;
    uintptr_t unityFrameFence;
    uint64_t nextFrameFenceValue;
} SpvrUnityD3D12ProbeSnapshot;

_Static_assert(sizeof(SpvrUnityD3D12ProbeSnapshot) == 72u,
    "managed/native probe snapshot size changed");

static volatile LONG g_flags;
static volatile LONG g_pluginLoadThreadId;
static volatile LONG g_renderEventThreadId;
static volatile LONG g_highestInterfaceVersion;
static IUnityInterfaces* g_unityInterfaces;
static void* g_unityD3D12;
static void* g_unityDevice;
static void* g_unityCommandQueue;
static void* g_unityFrameFence;
static uint64_t g_nextFrameFenceValue;

#define SPVR_EYE_COPY_SCHEMA 2u
#define SPVR_EYE_COPY_IDLE 0u
#define SPVR_EYE_COPY_PENDING 1u
#define SPVR_EYE_COPY_SUBMITTED 2u
#define SPVR_EYE_COPY_COMPLETED 3u
#define SPVR_EYE_COPY_FAILED 4u
#define SPVR_EYE_COPY_CANCELED 5u

#define SPVR_EYE_TRACE_DELIVERED 0x00000001u
#define SPVR_EYE_TRACE_CALLBACK_ACQUIRED 0x00000002u
#define SPVR_EYE_TRACE_COMMAND_READY 0x00000004u
#define SPVR_EYE_TRACE_EXECUTE_BEFORE 0x00000008u
#define SPVR_EYE_TRACE_EXECUTE_RETURNED 0x00000010u
#define SPVR_EYE_TRACE_SIGNAL_RESULT 0x00000020u
#define SPVR_EYE_TRACE_FENCE_FIRST_OBSERVED 0x00000040u
#define SPVR_EYE_TRACE_FENCE_TERMINAL 0x00000080u
#define SPVR_EYE_TRACE_FAILURE_OR_QUARANTINE 0x00000100u

#define SPVR_EYE_STAGE_NATIVE_DELIVERED 1u
#define SPVR_EYE_STAGE_CALLBACK_ACQUIRED 2u
#define SPVR_EYE_STAGE_VALIDATE_RESOURCES 3u
#define SPVR_EYE_STAGE_CREATE_PIPELINE 4u
#define SPVR_EYE_STAGE_CREATE_DESCRIPTORS 5u
#define SPVR_EYE_STAGE_CREATE_COMMAND_ALLOCATOR 6u
#define SPVR_EYE_STAGE_CREATE_COMMAND_LIST 7u
#define SPVR_EYE_STAGE_COMMAND_LIST_CLOSE_READY 8u
#define SPVR_EYE_STAGE_CREATE_FENCE 9u
#define SPVR_EYE_STAGE_EXECUTE_BEFORE 10u
#define SPVR_EYE_STAGE_EXECUTE_RETURNED 11u
#define SPVR_EYE_STAGE_SIGNAL_RESULT 12u
#define SPVR_EYE_STAGE_FENCE_POLL_FIRST 13u
#define SPVR_EYE_STAGE_FENCE_TERMINAL 14u
#define SPVR_EYE_STAGE_CANCEL 15u
#define SPVR_EYE_STAGE_DEVICE_REMOVED 16u

typedef struct SpvrEyeCopyRequest
{
    uint32_t schema;
    uint32_t reserved;
    int64_t presentationGeneration;
    uint64_t sequence;
    uintptr_t sourceLeft;
    uintptr_t sourceRight;
    uintptr_t destinationLeft;
    uintptr_t destinationRight;
    uintptr_t uiCompositeSource;
    uintptr_t uiWorldSource;
    uintptr_t uiDestination;
} SpvrEyeCopyRequest;

typedef struct SpvrEyeCopyStatus
{
    uint32_t schema;
    uint32_t state;
    uint32_t hresult;
    uint32_t reserved;
    int64_t presentationGeneration;
    uint64_t sequence;
    uint64_t frameFenceValue;
    uint64_t completedFenceValue;
} SpvrEyeCopyStatus;

typedef struct SpvrEyeCopyTelemetry
{
    uint32_t schema;
    uint32_t flags;
    uint32_t state;
    uint32_t lastStage;
    uint32_t hresult;
    uint32_t failureStage;
    uint32_t reserved0;
    uint32_t reserved1;
    int64_t presentationGeneration;
    uint64_t sequence;
    uint64_t targetFenceValue;
    uint64_t firstCompletedFenceValue;
    uint64_t latestCompletedFenceValue;
} SpvrEyeCopyTelemetry;

_Static_assert(sizeof(SpvrEyeCopyRequest) == 80u,
    "managed/native eye-copy request size changed");
_Static_assert(sizeof(SpvrEyeCopyStatus) == 48u,
    "managed/native eye-copy status size changed");
_Static_assert(sizeof(SpvrEyeCopyTelemetry) == 72u,
    "managed/native eye-copy telemetry size changed");

static SRWLOCK g_eyeCopyLock = SRWLOCK_INIT;
static SpvrEyeCopyRequest g_eyeCopyRequest;
static uint32_t g_eyeCopyState;
static HRESULT g_eyeCopyHresult = S_OK;
static uint64_t g_eyeCopyFenceValue;
static uint64_t g_eyeCopyCompletedFenceValue;
static uint32_t g_eyeCopyTelemetryFlags;
static uint32_t g_eyeCopyTelemetryState;
static uint32_t g_eyeCopyTelemetryLastStage;
static uint32_t g_eyeCopyTelemetryFailureStage;
static HRESULT g_eyeCopyTelemetryHresult = S_OK;
static uint64_t g_eyeCopyTelemetryFirstCompletedFenceValue;
static ID3D12CommandAllocator* g_eyeCopyAllocator;
static ID3D12GraphicsCommandList* g_eyeCopyCommandList;
static ID3D12Fence* g_eyeCopyCompletionFence;
static ID3D12RootSignature* g_eyeBlitRootSignature;
static ID3D12PipelineState* g_eyeBlitPipelineState;
static ID3D12PipelineState* g_uiDifferencePipelineState;
static ID3D12DescriptorHeap* g_eyeBlitSrvHeap;
static ID3D12DescriptorHeap* g_eyeBlitRtvHeap;
static DXGI_FORMAT g_eyeBlitFormat = DXGI_FORMAT_UNKNOWN;
static DXGI_FORMAT g_uiDifferenceFormat = DXGI_FORMAT_UNKNOWN;
static SRWLOCK g_cursorDrawLock = SRWLOCK_INIT;
static ID3D12RootSignature* g_cursorDrawRootSignature;
static ID3D12PipelineState* g_cursorDrawPipelineState;
static DXGI_FORMAT g_cursorDrawFormat = DXGI_FORMAT_UNKNOWN;

static const GUID SpvrIidCommandAllocator =
    { 0x6102dee4, 0xaf59, 0x4b09, { 0xb9, 0x99, 0xb4, 0x4d, 0x73, 0xf0, 0x9b, 0x24 } };
static const GUID SpvrIidGraphicsCommandList =
    { 0x5b160d0f, 0xac1b, 0x4185, { 0x8b, 0xa8, 0xb3, 0xae, 0x42, 0xa5, 0xa4, 0x55 } };
static const GUID SpvrIidFence =
    { 0x0a753dcf, 0xc4d8, 0x4b91, { 0xad, 0xf6, 0xbe, 0x5a, 0x60, 0xd9, 0x5a, 0x76 } };
static const GUID SpvrIidResource =
    { 0x696442be, 0xa72e, 0x4059, { 0xbc, 0x79, 0x5b, 0x5c, 0x98, 0x04, 0x0f, 0xad } };
static const GUID SpvrIidRootSignature =
    { 0xc54a6b66, 0x72df, 0x4ee8, { 0x8b, 0xe5, 0xa9, 0x46, 0xa1, 0x42, 0x92, 0x14 } };
static const GUID SpvrIidPipelineState =
    { 0x765a30f3, 0xf624, 0x4c6f, { 0xa8, 0x28, 0xac, 0xe9, 0x48, 0x62, 0x24, 0x45 } };
static const GUID SpvrIidDescriptorHeap =
    { 0x8efb471d, 0x616c, 0x4f49, { 0x90, 0xf7, 0x12, 0x7b, 0xb7, 0x63, 0xfa, 0x51 } };

typedef HRESULT (WINAPI *SpvrD3DCompileFn)(
    LPCVOID sourceData,
    SIZE_T sourceDataSize,
    LPCSTR sourceName,
    const D3D_SHADER_MACRO* defines,
    ID3DInclude* include,
    LPCSTR entryPoint,
    LPCSTR target,
    UINT flags1,
    UINT flags2,
    ID3DBlob** code,
    ID3DBlob** errors);

typedef HRESULT (WINAPI *SpvrSerializeRootSignatureFn)(
    const D3D12_ROOT_SIGNATURE_DESC* rootSignature,
    D3D_ROOT_SIGNATURE_VERSION version,
    ID3DBlob** blob,
    ID3DBlob** errors);

static D3D12_RESOURCE_DESC GetResourceDescription(ID3D12Resource* resource)
{
    D3D12_RESOURCE_DESC description;
    resource->lpVtbl->GetDesc(resource, &description);
    return description;
}

static D3D12_CPU_DESCRIPTOR_HANDLE GetCpuDescriptorStart(ID3D12DescriptorHeap* heap)
{
    D3D12_CPU_DESCRIPTOR_HANDLE handle;
    heap->lpVtbl->GetCPUDescriptorHandleForHeapStart(heap, &handle);
    return handle;
}

static D3D12_GPU_DESCRIPTOR_HANDLE GetGpuDescriptorStart(ID3D12DescriptorHeap* heap)
{
    D3D12_GPU_DESCRIPTOR_HANDLE handle;
    heap->lpVtbl->GetGPUDescriptorHandleForHeapStart(heap, &handle);
    return handle;
}

static DXGI_FORMAT EyeBlitViewFormat(DXGI_FORMAT resourceFormat)
{
    switch (resourceFormat)
    {
        case DXGI_FORMAT_R8G8B8A8_TYPELESS:
        case DXGI_FORMAT_R8G8B8A8_UNORM:
        case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
            return DXGI_FORMAT_R8G8B8A8_UNORM;
        case DXGI_FORMAT_B8G8R8A8_TYPELESS:
        case DXGI_FORMAT_B8G8R8A8_UNORM:
        case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
            return DXGI_FORMAT_B8G8R8A8_UNORM;
        default:
            return DXGI_FORMAT_UNKNOWN;
    }
}

static DXGI_FORMAT CursorDrawViewFormat(DXGI_FORMAT resourceFormat)
{
    switch (resourceFormat)
    {
        case DXGI_FORMAT_R8G8B8A8_TYPELESS:
            return DXGI_FORMAT_R8G8B8A8_UNORM;
        case DXGI_FORMAT_R8G8B8A8_UNORM:
        case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
        case DXGI_FORMAT_B8G8R8A8_UNORM:
        case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
            return resourceFormat;
        case DXGI_FORMAT_B8G8R8A8_TYPELESS:
            return DXGI_FORMAT_B8G8R8A8_UNORM;
        default:
            return DXGI_FORMAT_UNKNOWN;
    }
}

static void ReleaseCursorDrawPipeline(void)
{
    if (g_cursorDrawPipelineState != NULL)
    {
        ID3D12PipelineState_Release(g_cursorDrawPipelineState);
        g_cursorDrawPipelineState = NULL;
    }
    if (g_cursorDrawRootSignature != NULL)
    {
        ID3D12RootSignature_Release(g_cursorDrawRootSignature);
        g_cursorDrawRootSignature = NULL;
    }
    g_cursorDrawFormat = DXGI_FORMAT_UNKNOWN;
}

static HRESULT EnsureCursorDrawPipeline(ID3D12Device* device, DXGI_FORMAT format)
{
    static const char shaderSource[] =
        "struct VsOutput { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };\n"
        "VsOutput VSMain(uint vertexId : SV_VertexID) {\n"
        "  float2 p[3] = { float2(-1,-1), float2(-1,3), float2(3,-1) };\n"
        "  VsOutput o; o.position = float4(p[vertexId], 0, 1);\n"
        "  o.uv = float2((p[vertexId].x + 1.0) * 0.5, (1.0 - p[vertexId].y) * 0.5);\n"
        "  return o;\n"
        "}\n"
        "float4 PSMain(VsOutput input) : SV_TARGET {\n"
        "  float distanceFromCenter = length(input.uv * 2.0 - 1.0);\n"
        "  float ring = 1.0 - smoothstep(0.08, 0.13, abs(distanceFromCenter - 0.76));\n"
        "  float ringOutline = 1.0 - smoothstep(0.13, 0.18, abs(distanceFromCenter - 0.76));\n"
        "  float dot = 1.0 - smoothstep(0.11, 0.15, distanceFromCenter);\n"
        "  float dotOutline = 1.0 - smoothstep(0.15, 0.20, distanceFromCenter);\n"
        "  float white = max(ring, dot);\n"
        "  float silhouette = max(ringOutline, dotOutline);\n"
        "  return float4(white, white, white, silhouette);\n"
        "}\n";
    D3D12_ROOT_SIGNATURE_DESC rootDescription;
    D3D12_GRAPHICS_PIPELINE_STATE_DESC pipelineDescription;
    ID3DBlob* rootBlob = NULL;
    ID3DBlob* vertexBlob = NULL;
    ID3DBlob* pixelBlob = NULL;
    ID3DBlob* errors = NULL;
    HMODULE d3d12Library = NULL;
    HMODULE compilerLibrary = NULL;
    SpvrSerializeRootSignatureFn serializeRootSignature;
    SpvrD3DCompileFn compile;
    HRESULT result = E_FAIL;

    if (g_cursorDrawPipelineState != NULL &&
        g_cursorDrawRootSignature != NULL &&
        g_cursorDrawFormat == format)
        return S_OK;
    ReleaseCursorDrawPipeline();

    d3d12Library = LoadLibraryA("d3d12.dll");
    compilerLibrary = LoadLibraryA("d3dcompiler_47.dll");
    if (d3d12Library == NULL || compilerLibrary == NULL)
    {
        result = HRESULT_FROM_WIN32(GetLastError());
        goto cleanup;
    }
    serializeRootSignature = (SpvrSerializeRootSignatureFn)(void*)
        GetProcAddress(d3d12Library, "D3D12SerializeRootSignature");
    compile = (SpvrD3DCompileFn)(void*)GetProcAddress(compilerLibrary, "D3DCompile");
    if (serializeRootSignature == NULL || compile == NULL)
    {
        result = HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND);
        goto cleanup;
    }

    ZeroMemory(&rootDescription, sizeof(rootDescription));
    rootDescription.Flags = D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;
    result = serializeRootSignature(
        &rootDescription,
        D3D_ROOT_SIGNATURE_VERSION_1,
        &rootBlob,
        &errors);
    if (FAILED(result))
        goto cleanup;
    result = ID3D12Device_CreateRootSignature(
        device,
        0,
        ID3D10Blob_GetBufferPointer(rootBlob),
        ID3D10Blob_GetBufferSize(rootBlob),
        &SpvrIidRootSignature,
        (void**)&g_cursorDrawRootSignature);
    if (FAILED(result))
        goto cleanup;

    result = compile(
        shaderSource,
        sizeof(shaderSource) - 1,
        "SongPrismVR.PanelCursor.hlsl",
        NULL,
        NULL,
        "VSMain",
        "vs_5_0",
        0,
        0,
        &vertexBlob,
        &errors);
    if (FAILED(result))
        goto cleanup;
    if (errors != NULL)
    {
        ID3D10Blob_Release(errors);
        errors = NULL;
    }
    result = compile(
        shaderSource,
        sizeof(shaderSource) - 1,
        "SongPrismVR.PanelCursor.hlsl",
        NULL,
        NULL,
        "PSMain",
        "ps_5_0",
        0,
        0,
        &pixelBlob,
        &errors);
    if (FAILED(result))
        goto cleanup;

    ZeroMemory(&pipelineDescription, sizeof(pipelineDescription));
    pipelineDescription.pRootSignature = g_cursorDrawRootSignature;
    pipelineDescription.VS.pShaderBytecode = ID3D10Blob_GetBufferPointer(vertexBlob);
    pipelineDescription.VS.BytecodeLength = ID3D10Blob_GetBufferSize(vertexBlob);
    pipelineDescription.PS.pShaderBytecode = ID3D10Blob_GetBufferPointer(pixelBlob);
    pipelineDescription.PS.BytecodeLength = ID3D10Blob_GetBufferSize(pixelBlob);
    pipelineDescription.BlendState.RenderTarget[0].BlendEnable = TRUE;
    pipelineDescription.BlendState.RenderTarget[0].SrcBlend = D3D12_BLEND_SRC_ALPHA;
    pipelineDescription.BlendState.RenderTarget[0].DestBlend = D3D12_BLEND_INV_SRC_ALPHA;
    pipelineDescription.BlendState.RenderTarget[0].BlendOp = D3D12_BLEND_OP_ADD;
    pipelineDescription.BlendState.RenderTarget[0].SrcBlendAlpha = D3D12_BLEND_ONE;
    pipelineDescription.BlendState.RenderTarget[0].DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
    pipelineDescription.BlendState.RenderTarget[0].BlendOpAlpha = D3D12_BLEND_OP_ADD;
    pipelineDescription.BlendState.RenderTarget[0].RenderTargetWriteMask =
        D3D12_COLOR_WRITE_ENABLE_ALL;
    pipelineDescription.SampleMask = UINT_MAX;
    pipelineDescription.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
    pipelineDescription.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
    pipelineDescription.RasterizerState.DepthClipEnable = TRUE;
    pipelineDescription.DepthStencilState.DepthEnable = FALSE;
    pipelineDescription.DepthStencilState.StencilEnable = FALSE;
    pipelineDescription.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    pipelineDescription.NumRenderTargets = 1;
    pipelineDescription.RTVFormats[0] = format;
    pipelineDescription.SampleDesc.Count = 1;
    result = ID3D12Device_CreateGraphicsPipelineState(
        device,
        &pipelineDescription,
        &SpvrIidPipelineState,
        (void**)&g_cursorDrawPipelineState);
    if (SUCCEEDED(result))
        g_cursorDrawFormat = format;

cleanup:
    if (errors != NULL)
        ID3D10Blob_Release(errors);
    if (pixelBlob != NULL)
        ID3D10Blob_Release(pixelBlob);
    if (vertexBlob != NULL)
        ID3D10Blob_Release(vertexBlob);
    if (rootBlob != NULL)
        ID3D10Blob_Release(rootBlob);
    if (compilerLibrary != NULL)
        FreeLibrary(compilerLibrary);
    if (d3d12Library != NULL)
        FreeLibrary(d3d12Library);
    if (FAILED(result))
        ReleaseCursorDrawPipeline();
    return result;
}

static void ReleaseEyeBlitPipeline(void)
{
    if (g_uiDifferencePipelineState != NULL)
    {
        ID3D12PipelineState_Release(g_uiDifferencePipelineState);
        g_uiDifferencePipelineState = NULL;
    }
    if (g_eyeBlitPipelineState != NULL)
    {
        ID3D12PipelineState_Release(g_eyeBlitPipelineState);
        g_eyeBlitPipelineState = NULL;
    }
    if (g_eyeBlitRootSignature != NULL)
    {
        ID3D12RootSignature_Release(g_eyeBlitRootSignature);
        g_eyeBlitRootSignature = NULL;
    }
    g_eyeBlitFormat = DXGI_FORMAT_UNKNOWN;
    g_uiDifferenceFormat = DXGI_FORMAT_UNKNOWN;
}

static HRESULT EnsureEyeBlitPipeline(
    ID3D12Device* device,
    DXGI_FORMAT format,
    int requiresUi,
    DXGI_FORMAT uiFormat)
{
    static const char shaderSource[] =
        "Texture2D<float4> SourceTexture : register(t0);\n"
        "Texture2D<float4> WorldTexture : register(t1);\n"
        "struct VsOutput { float4 position : SV_POSITION; };\n"
        "VsOutput VSMain(uint vertexId : SV_VertexID) {\n"
        "  float2 p[3] = { float2(-1,-1), float2(-1,3), float2(3,-1) };\n"
        "  VsOutput o; o.position = float4(p[vertexId], 0, 1); return o;\n"
        "}\n"
        "float4 PSMain(VsOutput input) : SV_TARGET {\n"
        "  uint width, height; SourceTexture.GetDimensions(width, height);\n"
        "  uint x = min((uint)input.position.x, width - 1);\n"
        "  uint y = min((uint)input.position.y, height - 1);\n"
        "  return SourceTexture.Load(int3(x, height - 1 - y, 0));\n"
        "}\n"
        "float4 LoadWorldBilinear(float2 uv) {\n"
        "  uint width, height; WorldTexture.GetDimensions(width, height);\n"
        "  float2 pixel = saturate(uv) * float2(width, height) - 0.5;\n"
        "  int2 lower = int2(floor(pixel)); float2 blend = frac(pixel);\n"
        "  int2 maximum = int2((int)width - 1, (int)height - 1);\n"
        "  int2 p00 = clamp(lower, int2(0,0), maximum);\n"
        "  int2 p10 = clamp(lower + int2(1,0), int2(0,0), maximum);\n"
        "  int2 p01 = clamp(lower + int2(0,1), int2(0,0), maximum);\n"
        "  int2 p11 = clamp(lower + int2(1,1), int2(0,0), maximum);\n"
        "  float4 top = lerp(WorldTexture.Load(int3(p00,0)), WorldTexture.Load(int3(p10,0)), blend.x);\n"
        "  float4 bottom = lerp(WorldTexture.Load(int3(p01,0)), WorldTexture.Load(int3(p11,0)), blend.x);\n"
        "  return lerp(top, bottom, blend.y);\n"
        "}\n"
        "float4 PSUiDifferenceMain(VsOutput input) : SV_TARGET {\n"
        "  uint width, height; SourceTexture.GetDimensions(width, height);\n"
        "  uint x = min((uint)input.position.x, width - 1);\n"
        "  uint y = min((uint)input.position.y, height - 1);\n"
        "  float4 composite = SourceTexture.Load(int3(x, y, 0));\n"
        "  float2 uv = (float2(x, y) + 0.5) / float2(width, height);\n"
        "  float4 directWorld = LoadWorldBilinear(uv);\n"
        "  float4 flippedWorld = LoadWorldBilinear(float2(uv.x, 1.0 - uv.y));\n"
        "  float directDifference = max(abs(composite.r-directWorld.r), max(abs(composite.g-directWorld.g), abs(composite.b-directWorld.b)));\n"
        "  float flippedDifference = max(abs(composite.r-flippedWorld.r), max(abs(composite.g-flippedWorld.g), abs(composite.b-flippedWorld.b)));\n"
        "  float alpha = smoothstep(4.0/255.0, 24.0/255.0, min(directDifference, flippedDifference));\n"
        "  return float4(composite.rgb, alpha);\n"
        "}\n";
    D3D12_DESCRIPTOR_RANGE range;
    D3D12_ROOT_PARAMETER parameters[1];
    D3D12_ROOT_SIGNATURE_DESC rootDescription;
    D3D12_GRAPHICS_PIPELINE_STATE_DESC pipelineDescription;
    ID3DBlob* rootBlob = NULL;
    ID3DBlob* vertexBlob = NULL;
    ID3DBlob* pixelBlob = NULL;
    ID3DBlob* uiPixelBlob = NULL;
    ID3DBlob* errors = NULL;
    HMODULE d3d12Library = NULL;
    HMODULE compilerLibrary = NULL;
    SpvrSerializeRootSignatureFn serializeRootSignature;
    SpvrD3DCompileFn compile;
    HRESULT result = E_FAIL;

    if (g_eyeBlitPipelineState != NULL && g_eyeBlitRootSignature != NULL &&
        g_eyeBlitFormat == format &&
        (!requiresUi || (g_uiDifferencePipelineState != NULL &&
            g_uiDifferenceFormat == uiFormat)))
        return S_OK;
    ReleaseEyeBlitPipeline();

    d3d12Library = LoadLibraryA("d3d12.dll");
    compilerLibrary = LoadLibraryA("d3dcompiler_47.dll");
    if (d3d12Library == NULL || compilerLibrary == NULL)
    {
        result = HRESULT_FROM_WIN32(GetLastError());
        goto cleanup;
    }
    serializeRootSignature = (SpvrSerializeRootSignatureFn)(void*)
        GetProcAddress(d3d12Library, "D3D12SerializeRootSignature");
    compile = (SpvrD3DCompileFn)(void*)GetProcAddress(compilerLibrary, "D3DCompile");
    if (serializeRootSignature == NULL || compile == NULL)
    {
        result = HRESULT_FROM_WIN32(ERROR_PROC_NOT_FOUND);
        goto cleanup;
    }

    ZeroMemory(&range, sizeof(range));
    range.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
    /* Preserve the accepted one-SRV live eye-blit layout.  The second SRV is
       part of the coherent request only when a UI-difference pass is needed. */
    range.NumDescriptors = requiresUi ? 2 : 1;
    range.BaseShaderRegister = 0;
    range.RegisterSpace = 0;
    range.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;
    ZeroMemory(parameters, sizeof(parameters));
    parameters[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    parameters[0].DescriptorTable.NumDescriptorRanges = 1;
    parameters[0].DescriptorTable.pDescriptorRanges = &range;
    parameters[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;
    ZeroMemory(&rootDescription, sizeof(rootDescription));
    rootDescription.NumParameters = 1;
    rootDescription.pParameters = parameters;
    rootDescription.Flags = D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT;
    result = serializeRootSignature(
        &rootDescription,
        D3D_ROOT_SIGNATURE_VERSION_1,
        &rootBlob,
        &errors);
    if (FAILED(result))
        goto cleanup;

    result = ID3D12Device_CreateRootSignature(
        device,
        0,
        ID3D10Blob_GetBufferPointer(rootBlob),
        ID3D10Blob_GetBufferSize(rootBlob),
        &SpvrIidRootSignature,
        (void**)&g_eyeBlitRootSignature);
    if (FAILED(result))
        goto cleanup;

    result = compile(
        shaderSource,
        sizeof(shaderSource) - 1,
        "SongPrismVR.EyeVerticalFlip.hlsl",
        NULL,
        NULL,
        "VSMain",
        "vs_5_0",
        0,
        0,
        &vertexBlob,
        &errors);
    if (FAILED(result))
        goto cleanup;
    if (errors != NULL)
    {
        ID3D10Blob_Release(errors);
        errors = NULL;
    }
    result = compile(
        shaderSource,
        sizeof(shaderSource) - 1,
        "SongPrismVR.EyeVerticalFlip.hlsl",
        NULL,
        NULL,
        "PSMain",
        "ps_5_0",
        0,
        0,
        &pixelBlob,
        &errors);
    if (FAILED(result))
        goto cleanup;
    if (requiresUi)
    {
        if (errors != NULL)
        {
            ID3D10Blob_Release(errors);
            errors = NULL;
        }
        result = compile(
            shaderSource,
            sizeof(shaderSource) - 1,
            "SongPrismVR.UiDifference.hlsl",
            NULL,
            NULL,
            "PSUiDifferenceMain",
            "ps_5_0",
            0,
            0,
            &uiPixelBlob,
            &errors);
        if (FAILED(result))
            goto cleanup;
    }

    ZeroMemory(&pipelineDescription, sizeof(pipelineDescription));
    pipelineDescription.pRootSignature = g_eyeBlitRootSignature;
    pipelineDescription.VS.pShaderBytecode = ID3D10Blob_GetBufferPointer(vertexBlob);
    pipelineDescription.VS.BytecodeLength = ID3D10Blob_GetBufferSize(vertexBlob);
    pipelineDescription.PS.pShaderBytecode = ID3D10Blob_GetBufferPointer(pixelBlob);
    pipelineDescription.PS.BytecodeLength = ID3D10Blob_GetBufferSize(pixelBlob);
    pipelineDescription.BlendState.RenderTarget[0].RenderTargetWriteMask =
        D3D12_COLOR_WRITE_ENABLE_ALL;
    pipelineDescription.SampleMask = UINT_MAX;
    pipelineDescription.RasterizerState.FillMode = D3D12_FILL_MODE_SOLID;
    pipelineDescription.RasterizerState.CullMode = D3D12_CULL_MODE_NONE;
    pipelineDescription.RasterizerState.DepthClipEnable = TRUE;
    pipelineDescription.DepthStencilState.DepthEnable = FALSE;
    pipelineDescription.DepthStencilState.StencilEnable = FALSE;
    pipelineDescription.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
    pipelineDescription.NumRenderTargets = 1;
    pipelineDescription.RTVFormats[0] = format;
    pipelineDescription.SampleDesc.Count = 1;
    result = ID3D12Device_CreateGraphicsPipelineState(
        device,
        &pipelineDescription,
        &SpvrIidPipelineState,
        (void**)&g_eyeBlitPipelineState);
    if (SUCCEEDED(result))
        g_eyeBlitFormat = format;
    if (SUCCEEDED(result) && requiresUi)
    {
        pipelineDescription.PS.pShaderBytecode = ID3D10Blob_GetBufferPointer(uiPixelBlob);
        pipelineDescription.PS.BytecodeLength = ID3D10Blob_GetBufferSize(uiPixelBlob);
        pipelineDescription.RTVFormats[0] = uiFormat;
        result = ID3D12Device_CreateGraphicsPipelineState(
            device,
            &pipelineDescription,
            &SpvrIidPipelineState,
            (void**)&g_uiDifferencePipelineState);
        if (SUCCEEDED(result))
            g_uiDifferenceFormat = uiFormat;
    }

cleanup:
    if (errors != NULL)
        ID3D10Blob_Release(errors);
    if (pixelBlob != NULL)
        ID3D10Blob_Release(pixelBlob);
    if (uiPixelBlob != NULL)
        ID3D10Blob_Release(uiPixelBlob);
    if (vertexBlob != NULL)
        ID3D10Blob_Release(vertexBlob);
    if (rootBlob != NULL)
        ID3D10Blob_Release(rootBlob);
    if (compilerLibrary != NULL)
        FreeLibrary(compilerLibrary);
    if (d3d12Library != NULL)
        FreeLibrary(d3d12Library);
    if (FAILED(result))
        ReleaseEyeBlitPipeline();
    return result;
}

static HRESULT CreateEyeBlitDescriptors(
    ID3D12Device* device,
    DXGI_FORMAT format,
    ID3D12Resource* sourceLeft,
    ID3D12Resource* sourceRight,
    ID3D12Resource* destinationLeft,
    ID3D12Resource* destinationRight,
    ID3D12Resource* uiCompositeSource,
    DXGI_FORMAT uiCompositeFormat,
    ID3D12Resource* uiWorldSource,
    DXGI_FORMAT uiWorldFormat,
    ID3D12Resource* uiDestination,
    DXGI_FORMAT uiDestinationFormat)
{
    D3D12_DESCRIPTOR_HEAP_DESC heapDescription;
    D3D12_SHADER_RESOURCE_VIEW_DESC sourceDescription;
    D3D12_RENDER_TARGET_VIEW_DESC destinationDescription;
    D3D12_CPU_DESCRIPTOR_HANDLE sourceHandle;
    D3D12_CPU_DESCRIPTOR_HANDLE destinationHandle;
    UINT sourceStride;
    UINT destinationStride;
    HRESULT result;

    ZeroMemory(&heapDescription, sizeof(heapDescription));
    heapDescription.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
    heapDescription.NumDescriptors = uiDestination != NULL ? 6 : 2;
    heapDescription.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
    result = ID3D12Device_CreateDescriptorHeap(
        device,
        &heapDescription,
        &SpvrIidDescriptorHeap,
        (void**)&g_eyeBlitSrvHeap);
    if (FAILED(result))
        return result;

    heapDescription.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
    heapDescription.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
    result = ID3D12Device_CreateDescriptorHeap(
        device,
        &heapDescription,
        &SpvrIidDescriptorHeap,
        (void**)&g_eyeBlitRtvHeap);
    if (FAILED(result))
        return result;

    ZeroMemory(&sourceDescription, sizeof(sourceDescription));
    sourceDescription.Format = format;
    sourceDescription.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
    sourceDescription.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
    sourceDescription.Texture2D.MipLevels = 1;
    sourceHandle = GetCpuDescriptorStart(g_eyeBlitSrvHeap);
    sourceStride = ID3D12Device_GetDescriptorHandleIncrementSize(
        device,
        D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
    ID3D12Device_CreateShaderResourceView(
        device,
        sourceLeft,
        &sourceDescription,
        sourceHandle);
    if (uiDestination != NULL)
    {
        sourceHandle.ptr += sourceStride;
        ID3D12Device_CreateShaderResourceView(
            device,
            sourceLeft,
            &sourceDescription,
            sourceHandle);
    }
    sourceHandle.ptr += sourceStride;
    ID3D12Device_CreateShaderResourceView(
        device,
        sourceRight,
        &sourceDescription,
        sourceHandle);
    if (uiDestination != NULL)
    {
        sourceHandle.ptr += sourceStride;
        ID3D12Device_CreateShaderResourceView(
            device,
            sourceRight,
            &sourceDescription,
            sourceHandle);
        sourceHandle.ptr += sourceStride;
        sourceDescription.Format = uiCompositeFormat;
        ID3D12Device_CreateShaderResourceView(
            device,
            uiCompositeSource,
            &sourceDescription,
            sourceHandle);
        sourceHandle.ptr += sourceStride;
        sourceDescription.Format = uiWorldFormat;
        ID3D12Device_CreateShaderResourceView(
            device,
            uiWorldSource,
            &sourceDescription,
            sourceHandle);
    }

    ZeroMemory(&destinationDescription, sizeof(destinationDescription));
    destinationDescription.Format = format;
    destinationDescription.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2D;
    destinationHandle = GetCpuDescriptorStart(g_eyeBlitRtvHeap);
    destinationStride = ID3D12Device_GetDescriptorHandleIncrementSize(
        device,
        D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    ID3D12Device_CreateRenderTargetView(
        device,
        destinationLeft,
        &destinationDescription,
        destinationHandle);
    destinationHandle.ptr += destinationStride;
    ID3D12Device_CreateRenderTargetView(
        device,
        destinationRight,
        &destinationDescription,
        destinationHandle);
    if (uiDestination != NULL)
    {
        destinationHandle.ptr += destinationStride;
        destinationDescription.Format = uiDestinationFormat;
        ID3D12Device_CreateRenderTargetView(
            device,
            uiDestination,
            &destinationDescription,
            destinationHandle);
    }
    return S_OK;
}

static void RecordEyeVerticalBlits(
    ID3D12Device* device,
    ID3D12GraphicsCommandList* commandList,
    uint64_t width,
    uint32_t height,
    ID3D12Resource* uiCompositeSource,
    ID3D12Resource* uiDestination,
    uint64_t uiWidth,
    uint32_t uiHeight)
{
    ID3D12DescriptorHeap* heaps[1];
    D3D12_GPU_DESCRIPTOR_HANDLE sourceHandle;
    D3D12_CPU_DESCRIPTOR_HANDLE destinationHandle;
    D3D12_VIEWPORT viewport;
    D3D12_RECT scissor;
    UINT sourceStride;
    UINT destinationStride;
    uint32_t eye;
    D3D12_RESOURCE_BARRIER barrier;
    heaps[0] = g_eyeBlitSrvHeap;
    ID3D12GraphicsCommandList_SetDescriptorHeaps(commandList, 1, heaps);
    ID3D12GraphicsCommandList_SetGraphicsRootSignature(
        commandList,
        g_eyeBlitRootSignature);
    ID3D12GraphicsCommandList_SetPipelineState(commandList, g_eyeBlitPipelineState);
    ID3D12GraphicsCommandList_IASetPrimitiveTopology(
        commandList,
        D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    ZeroMemory(&viewport, sizeof(viewport));
    viewport.Width = (float)width;
    viewport.Height = (float)height;
    viewport.MaxDepth = 1.0f;
    scissor.left = 0;
    scissor.top = 0;
    scissor.right = (LONG)width;
    scissor.bottom = (LONG)height;
    ID3D12GraphicsCommandList_RSSetViewports(commandList, 1, &viewport);
    ID3D12GraphicsCommandList_RSSetScissorRects(commandList, 1, &scissor);

    sourceHandle = GetGpuDescriptorStart(g_eyeBlitSrvHeap);
    destinationHandle = GetCpuDescriptorStart(g_eyeBlitRtvHeap);
    sourceStride = ID3D12Device_GetDescriptorHandleIncrementSize(
        device,
        D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
    destinationStride = ID3D12Device_GetDescriptorHandleIncrementSize(
        device,
        D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    for (eye = 0; eye < 2; ++eye)
    {
        ID3D12GraphicsCommandList_SetGraphicsRootDescriptorTable(
            commandList,
            0,
            sourceHandle);
        ID3D12GraphicsCommandList_OMSetRenderTargets(
            commandList,
            1,
            &destinationHandle,
            FALSE,
            NULL);
        ID3D12GraphicsCommandList_DrawInstanced(commandList, 3, 1, 0, 0);
        sourceHandle.ptr += (uiDestination != NULL ? 2u : 1u) * sourceStride;
        destinationHandle.ptr += destinationStride;
    }

    if (uiDestination == NULL)
        return;

    ZeroMemory(&barrier, sizeof(barrier));
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition.pResource = uiCompositeSource;
    barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_PRESENT;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    ID3D12GraphicsCommandList_ResourceBarrier(commandList, 1, &barrier);

    viewport.Width = (float)uiWidth;
    viewport.Height = (float)uiHeight;
    scissor.right = (LONG)uiWidth;
    scissor.bottom = (LONG)uiHeight;
    ID3D12GraphicsCommandList_RSSetViewports(commandList, 1, &viewport);
    ID3D12GraphicsCommandList_RSSetScissorRects(commandList, 1, &scissor);
    ID3D12GraphicsCommandList_SetPipelineState(
        commandList,
        g_uiDifferencePipelineState);
    ID3D12GraphicsCommandList_SetGraphicsRootDescriptorTable(
        commandList,
        0,
        sourceHandle);
    ID3D12GraphicsCommandList_OMSetRenderTargets(
        commandList,
        1,
        &destinationHandle,
        FALSE,
        NULL);
    ID3D12GraphicsCommandList_DrawInstanced(commandList, 3, 1, 0, 0);

    barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_PRESENT;
    ID3D12GraphicsCommandList_ResourceBarrier(commandList, 1, &barrier);
}

static void ReleaseEyeCopyResources(void)
{
    ID3D12Resource* resources[7];
    size_t index;

    resources[0] = (ID3D12Resource*)g_eyeCopyRequest.sourceLeft;
    resources[1] = (ID3D12Resource*)g_eyeCopyRequest.sourceRight;
    resources[2] = (ID3D12Resource*)g_eyeCopyRequest.destinationLeft;
    resources[3] = (ID3D12Resource*)g_eyeCopyRequest.destinationRight;
    resources[4] = (ID3D12Resource*)g_eyeCopyRequest.uiCompositeSource;
    resources[5] = (ID3D12Resource*)g_eyeCopyRequest.uiWorldSource;
    resources[6] = (ID3D12Resource*)g_eyeCopyRequest.uiDestination;
    for (index = 0; index < 7; index++)
    {
        if (resources[index] != NULL)
            ID3D12Resource_Release(resources[index]);
    }
    if (g_eyeCopyCommandList != NULL)
    {
        ID3D12GraphicsCommandList_Release(g_eyeCopyCommandList);
        g_eyeCopyCommandList = NULL;
    }
    if (g_eyeCopyAllocator != NULL)
    {
        ID3D12CommandAllocator_Release(g_eyeCopyAllocator);
        g_eyeCopyAllocator = NULL;
    }
    if (g_eyeCopyCompletionFence != NULL)
    {
        ID3D12Fence_Release(g_eyeCopyCompletionFence);
        g_eyeCopyCompletionFence = NULL;
    }
    if (g_eyeBlitRtvHeap != NULL)
    {
        ID3D12DescriptorHeap_Release(g_eyeBlitRtvHeap);
        g_eyeBlitRtvHeap = NULL;
    }
    if (g_eyeBlitSrvHeap != NULL)
    {
        ID3D12DescriptorHeap_Release(g_eyeBlitSrvHeap);
        g_eyeBlitSrvHeap = NULL;
    }
    ZeroMemory(&g_eyeCopyRequest, sizeof(g_eyeCopyRequest));
}

static void TraceEyeCopyStage(uint32_t flag, uint32_t stage)
{
    g_eyeCopyTelemetryFlags |= flag;
    g_eyeCopyTelemetryLastStage = stage;
}

static void TraceEyeCopyFailure(uint32_t stage, HRESULT result)
{
    g_eyeCopyTelemetryFlags |= SPVR_EYE_TRACE_FAILURE_OR_QUARANTINE;
    g_eyeCopyTelemetryLastStage = stage;
    g_eyeCopyTelemetryFailureStage = stage;
    g_eyeCopyTelemetryHresult = result;
}

static void FailEyeCopy(uint32_t stage, HRESULT result)
{
    TraceEyeCopyFailure(stage, result);
    g_eyeCopyHresult = result;
    g_eyeCopyState = SPVR_EYE_COPY_FAILED;
    g_eyeCopyTelemetryState = SPVR_EYE_COPY_FAILED;
}

static void SubmitPendingEyeCopy(void)
{
    ID3D12Device* device = (ID3D12Device*)g_unityDevice;
    ID3D12CommandQueue* commandQueue = (ID3D12CommandQueue*)g_unityCommandQueue;
    ID3D12Resource* sourceLeft;
    ID3D12Resource* sourceRight;
    ID3D12Resource* destinationLeft;
    ID3D12Resource* destinationRight;
    ID3D12Resource* uiCompositeSource;
    ID3D12Resource* uiWorldSource;
    ID3D12Resource* uiDestination;
    D3D12_RESOURCE_DESC sourceDescription;
    D3D12_RESOURCE_DESC sourceRightDescription;
    D3D12_RESOURCE_DESC destinationDescription;
    D3D12_RESOURCE_DESC destinationRightDescription;
    D3D12_RESOURCE_DESC uiCompositeDescription;
    D3D12_RESOURCE_DESC uiWorldDescription;
    D3D12_RESOURCE_DESC uiDestinationDescription;
    DXGI_FORMAT viewFormat;
    DXGI_FORMAT uiCompositeFormat = DXGI_FORMAT_UNKNOWN;
    DXGI_FORMAT uiWorldFormat = DXGI_FORMAT_UNKNOWN;
    DXGI_FORMAT uiDestinationFormat = DXGI_FORMAT_UNKNOWN;
    int requiresUi;
    ID3D12CommandList* commandLists[1];
    HRESULT result;

    AcquireSRWLockExclusive(&g_eyeCopyLock);
    if (g_eyeCopyState != SPVR_EYE_COPY_PENDING)
    {
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return;
    }
    TraceEyeCopyStage(SPVR_EYE_TRACE_CALLBACK_ACQUIRED, SPVR_EYE_STAGE_CALLBACK_ACQUIRED);
    if (device == NULL || commandQueue == NULL)
    {
        FailEyeCopy(SPVR_EYE_STAGE_VALIDATE_RESOURCES, E_POINTER);
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return;
    }

    sourceLeft = (ID3D12Resource*)g_eyeCopyRequest.sourceLeft;
    sourceRight = (ID3D12Resource*)g_eyeCopyRequest.sourceRight;
    destinationLeft = (ID3D12Resource*)g_eyeCopyRequest.destinationLeft;
    destinationRight = (ID3D12Resource*)g_eyeCopyRequest.destinationRight;
    uiCompositeSource = (ID3D12Resource*)g_eyeCopyRequest.uiCompositeSource;
    uiWorldSource = (ID3D12Resource*)g_eyeCopyRequest.uiWorldSource;
    uiDestination = (ID3D12Resource*)g_eyeCopyRequest.uiDestination;
    requiresUi = uiCompositeSource != NULL && uiWorldSource != NULL && uiDestination != NULL;
    sourceDescription = GetResourceDescription(sourceLeft);
    sourceRightDescription = GetResourceDescription(sourceRight);
    destinationDescription = GetResourceDescription(destinationLeft);
    destinationRightDescription = GetResourceDescription(destinationRight);
    viewFormat = EyeBlitViewFormat(sourceDescription.Format);
    if (viewFormat == DXGI_FORMAT_UNKNOWN ||
        EyeBlitViewFormat(sourceRightDescription.Format) != viewFormat ||
        EyeBlitViewFormat(destinationDescription.Format) != viewFormat ||
        EyeBlitViewFormat(destinationRightDescription.Format) != viewFormat ||
        sourceDescription.Dimension != D3D12_RESOURCE_DIMENSION_TEXTURE2D ||
        sourceRightDescription.Dimension != sourceDescription.Dimension ||
        destinationDescription.Dimension != sourceDescription.Dimension ||
        destinationRightDescription.Dimension != sourceDescription.Dimension ||
        sourceDescription.Width != sourceRightDescription.Width ||
        sourceDescription.Width != destinationDescription.Width ||
        sourceDescription.Width != destinationRightDescription.Width ||
        sourceDescription.Height != sourceRightDescription.Height ||
        sourceDescription.Height != destinationDescription.Height ||
        sourceDescription.Height != destinationRightDescription.Height ||
        sourceDescription.SampleDesc.Count != 1 ||
        sourceRightDescription.SampleDesc.Count != 1 ||
        destinationDescription.SampleDesc.Count != 1 ||
        destinationRightDescription.SampleDesc.Count != 1 ||
        (sourceDescription.Flags & D3D12_RESOURCE_FLAG_DENY_SHADER_RESOURCE) != 0 ||
        (sourceRightDescription.Flags & D3D12_RESOURCE_FLAG_DENY_SHADER_RESOURCE) != 0 ||
        (destinationDescription.Flags & D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET) == 0 ||
        (destinationRightDescription.Flags & D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET) == 0)
    {
        FailEyeCopy(SPVR_EYE_STAGE_VALIDATE_RESOURCES, E_INVALIDARG);
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return;
    }
    if (requiresUi)
    {
        uiCompositeDescription = GetResourceDescription(uiCompositeSource);
        uiWorldDescription = GetResourceDescription(uiWorldSource);
        uiDestinationDescription = GetResourceDescription(uiDestination);
        uiCompositeFormat = EyeBlitViewFormat(uiCompositeDescription.Format);
        uiWorldFormat = EyeBlitViewFormat(uiWorldDescription.Format);
        uiDestinationFormat = EyeBlitViewFormat(uiDestinationDescription.Format);
        if (uiCompositeFormat == DXGI_FORMAT_UNKNOWN ||
            uiWorldFormat == DXGI_FORMAT_UNKNOWN ||
            uiDestinationFormat != uiCompositeFormat ||
            uiCompositeDescription.Dimension != D3D12_RESOURCE_DIMENSION_TEXTURE2D ||
            uiWorldDescription.Dimension != D3D12_RESOURCE_DIMENSION_TEXTURE2D ||
            uiDestinationDescription.Dimension != D3D12_RESOURCE_DIMENSION_TEXTURE2D ||
            uiCompositeDescription.Width != uiDestinationDescription.Width ||
            uiCompositeDescription.Height != uiDestinationDescription.Height ||
            uiCompositeDescription.SampleDesc.Count != 1 ||
            uiWorldDescription.SampleDesc.Count != 1 ||
            uiDestinationDescription.SampleDesc.Count != 1 ||
            (uiCompositeDescription.Flags & D3D12_RESOURCE_FLAG_DENY_SHADER_RESOURCE) != 0 ||
            (uiWorldDescription.Flags & D3D12_RESOURCE_FLAG_DENY_SHADER_RESOURCE) != 0 ||
            (uiDestinationDescription.Flags & D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET) == 0)
        {
            FailEyeCopy(SPVR_EYE_STAGE_VALIDATE_RESOURCES, E_INVALIDARG);
            ReleaseSRWLockExclusive(&g_eyeCopyLock);
            return;
        }
    }
    result = EnsureEyeBlitPipeline(
        device,
        viewFormat,
        requiresUi,
        uiDestinationFormat);
    if (FAILED(result))
    {
        FailEyeCopy(SPVR_EYE_STAGE_CREATE_PIPELINE, result);
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return;
    }
    result = CreateEyeBlitDescriptors(
        device,
        viewFormat,
        sourceLeft,
        sourceRight,
        destinationLeft,
        destinationRight,
        uiCompositeSource,
        uiCompositeFormat,
        uiWorldSource,
        uiWorldFormat,
        uiDestination,
        uiDestinationFormat);
    if (FAILED(result))
    {
        FailEyeCopy(SPVR_EYE_STAGE_CREATE_DESCRIPTORS, result);
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return;
    }

    result = ID3D12Device_CreateCommandAllocator(
        device,
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        &SpvrIidCommandAllocator,
        (void**)&g_eyeCopyAllocator);
    if (FAILED(result))
    {
        FailEyeCopy(SPVR_EYE_STAGE_CREATE_COMMAND_ALLOCATOR, result);
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return;
    }
    result = ID3D12Device_CreateCommandList(
        device,
        0,
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        g_eyeCopyAllocator,
        NULL,
        &SpvrIidGraphicsCommandList,
        (void**)&g_eyeCopyCommandList);
    if (FAILED(result))
    {
        FailEyeCopy(SPVR_EYE_STAGE_CREATE_COMMAND_LIST, result);
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return;
    }

    RecordEyeVerticalBlits(
        device,
        g_eyeCopyCommandList,
        sourceDescription.Width,
        sourceDescription.Height,
        uiCompositeSource,
        uiDestination,
        requiresUi ? uiDestinationDescription.Width : 0,
        requiresUi ? uiDestinationDescription.Height : 0);
    result = ID3D12GraphicsCommandList_Close(g_eyeCopyCommandList);
    if (FAILED(result))
    {
        FailEyeCopy(SPVR_EYE_STAGE_COMMAND_LIST_CLOSE_READY, result);
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return;
    }
    TraceEyeCopyStage(SPVR_EYE_TRACE_COMMAND_READY, SPVR_EYE_STAGE_COMMAND_LIST_CLOSE_READY);

    result = ID3D12Device_CreateFence(
        device,
        0,
        D3D12_FENCE_FLAG_NONE,
        &SpvrIidFence,
        (void**)&g_eyeCopyCompletionFence);
    if (FAILED(result))
    {
        FailEyeCopy(SPVR_EYE_STAGE_CREATE_FENCE, result);
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return;
    }

    commandLists[0] = (ID3D12CommandList*)g_eyeCopyCommandList;
    TraceEyeCopyStage(SPVR_EYE_TRACE_EXECUTE_BEFORE, SPVR_EYE_STAGE_EXECUTE_BEFORE);
    ID3D12CommandQueue_ExecuteCommandLists(commandQueue, 1, commandLists);
    TraceEyeCopyStage(SPVR_EYE_TRACE_EXECUTE_RETURNED, SPVR_EYE_STAGE_EXECUTE_RETURNED);
    g_eyeCopyFenceValue = 1;
    result = ID3D12CommandQueue_Signal(
        commandQueue,
        g_eyeCopyCompletionFence,
        g_eyeCopyFenceValue);
    TraceEyeCopyStage(SPVR_EYE_TRACE_SIGNAL_RESULT, SPVR_EYE_STAGE_SIGNAL_RESULT);
    g_eyeCopyTelemetryHresult = result;
    if (FAILED(result))
    {
        /* The command list may already be in flight. Keep every retained resource
         * behind the incomplete completion fence instead of releasing it early. */
        TraceEyeCopyFailure(SPVR_EYE_STAGE_SIGNAL_RESULT, result);
        g_eyeCopyHresult = result;
        g_eyeCopyState = SPVR_EYE_COPY_SUBMITTED;
        g_eyeCopyTelemetryState = SPVR_EYE_COPY_SUBMITTED;
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return;
    }
    g_eyeCopyHresult = S_OK;
    g_eyeCopyState = SPVR_EYE_COPY_SUBMITTED;
    g_eyeCopyTelemetryState = SPVR_EYE_COPY_SUBMITTED;
    ReleaseSRWLockExclusive(&g_eyeCopyLock);
}

static void* FindHighestD3D12Interface(IUnityInterfaces* interfaces, LONG* version)
{
    static const struct
    {
        uint64_t high;
        uint64_t low;
        LONG version;
    } candidates[] =
    {
        { 0x4624B0DA41B64AACULL, 0x915AABCB9BC3F0D3ULL, 7 },
        { 0xA396DCE58CAC4D78ULL, 0xAFDD9B281F20B840ULL, 6 },
        { 0xF5C8D8A37D37BC42ULL, 0xB02DFE93B5064A27ULL, 5 },
        { 0x498FFCC13EC94006ULL, 0xB18F8B0FF67778C8ULL, 4 },
        { 0x57C3FAFE59E5E843ULL, 0xBF4F5998474BB600ULL, 3 },
        { 0xEC39D2F18446C745ULL, 0xB1A2626641D6B11FULL, 2 }
    };
    size_t index;

    if (interfaces == NULL || interfaces->GetInterfaceSplit == NULL)
        return NULL;

    for (index = 0; index < sizeof(candidates) / sizeof(candidates[0]); ++index)
    {
        void* value = interfaces->GetInterfaceSplit(candidates[index].high, candidates[index].low);
        if (value != NULL)
        {
            *version = candidates[index].version;
            return value;
        }
    }
    return NULL;
}

__declspec(dllexport) void __stdcall UnityPluginLoad(IUnityInterfaces* unityInterfaces)
{
    LONG version = 0;
    void* d3d12Interface;
    ConfigureEventFn configureEvent = NULL;
    UnityD3D12PluginEventConfig eventConfig;

    g_unityInterfaces = unityInterfaces;
    InterlockedExchange(&g_pluginLoadThreadId, (LONG)GetCurrentThreadId());

    d3d12Interface = FindHighestD3D12Interface(unityInterfaces, &version);
    g_unityD3D12 = d3d12Interface;
    InterlockedExchange(&g_highestInterfaceVersion, version);
    if (d3d12Interface != NULL)
    {
        if (version == 7)
            configureEvent = ((UnityD3D12V7Prefix*)d3d12Interface)->ConfigureEvent;
        else if (version == 6)
            configureEvent = ((UnityD3D12V6Prefix*)d3d12Interface)->ConfigureEvent;

        if (configureEvent != NULL)
        {
            ZeroMemory(&eventConfig, sizeof(eventConfig));
            eventConfig.graphicsQueueAccess = 1;
            eventConfig.flags =
                SPVR_D3D12_EVENT_ENSURE_PREVIOUS_FRAME_SUBMISSION |
                SPVR_D3D12_EVENT_FLUSH_COMMAND_BUFFERS |
                SPVR_D3D12_EVENT_SYNC_WORKER_THREADS |
                SPVR_D3D12_EVENT_MODIFIES_COMMAND_BUFFER_STATE;
            eventConfig.ensureActiveRenderTextureIsBound = FALSE;
            configureEvent(SPVR_RENDER_EVENT_ID, &eventConfig);
        }
        InterlockedOr(&g_flags, SPVR_FLAG_INTERFACE_FOUND);
    }
    InterlockedOr(&g_flags, SPVR_FLAG_PLUGIN_LOAD);
}

__declspec(dllexport) void __stdcall UnityPluginUnload(void)
{
    AcquireSRWLockExclusive(&g_eyeCopyLock);
    if (g_eyeCopyState == SPVR_EYE_COPY_IDLE)
        ReleaseEyeBlitPipeline();
    ReleaseSRWLockExclusive(&g_eyeCopyLock);
    AcquireSRWLockExclusive(&g_cursorDrawLock);
    ReleaseCursorDrawPipeline();
    ReleaseSRWLockExclusive(&g_cursorDrawLock);
}

static void __stdcall SpvrRenderEvent(int eventId)
{
    LONG version;

    (void)eventId;
    InterlockedExchange(&g_renderEventThreadId, (LONG)GetCurrentThreadId());

    version = InterlockedCompareExchange(&g_highestInterfaceVersion, 0, 0);
    if (g_unityD3D12 == NULL)
    {
        InterlockedOr(&g_flags, SPVR_FLAG_RENDER_EVENT);
        return;
    }

    if (version == 7)
    {
        UnityD3D12V7Prefix* v7 = (UnityD3D12V7Prefix*)g_unityD3D12;
        if (v7->GetDevice != NULL)
            g_unityDevice = v7->GetDevice();
        if (v7->GetFrameFence != NULL)
            g_unityFrameFence = v7->GetFrameFence();
        if (v7->GetNextFrameFenceValue != NULL)
            g_nextFrameFenceValue = v7->GetNextFrameFenceValue();
        if (v7->GetCommandQueue != NULL)
            g_unityCommandQueue = v7->GetCommandQueue();
    }
    else
    {
        UnityD3D12V2Prefix* v2 = (UnityD3D12V2Prefix*)g_unityD3D12;
        if (v2->GetDevice != NULL)
            g_unityDevice = v2->GetDevice();
        if (v2->GetFrameFence != NULL)
            g_unityFrameFence = v2->GetFrameFence();
        if (v2->GetNextFrameFenceValue != NULL)
            g_nextFrameFenceValue = v2->GetNextFrameFenceValue();
        if (version >= 4)
        {
            UnityD3D12V4Prefix* v4 = (UnityD3D12V4Prefix*)g_unityD3D12;
            if (v4->GetCommandQueue != NULL)
                g_unityCommandQueue = v4->GetCommandQueue();
        }
    }
    SubmitPendingEyeCopy();
    InterlockedOr(&g_flags, SPVR_FLAG_RENDER_EVENT);
}

__declspec(dllexport) void* __stdcall spvr_get_render_event_func(void)
{
    return (void*)&SpvrRenderEvent;
}

__declspec(dllexport) int __stdcall spvr_get_probe_snapshot(SpvrUnityD3D12ProbeSnapshot* snapshot)
{
    if (snapshot == NULL)
        return 0;

    snapshot->schema = SPVR_PROBE_SCHEMA;
    snapshot->flags = (uint32_t)InterlockedCompareExchange(&g_flags, 0, 0);
    snapshot->pluginLoadThreadId = (uint32_t)InterlockedCompareExchange(&g_pluginLoadThreadId, 0, 0);
    snapshot->renderEventThreadId = (uint32_t)InterlockedCompareExchange(&g_renderEventThreadId, 0, 0);
    snapshot->highestInterfaceVersion = (uint32_t)InterlockedCompareExchange(&g_highestInterfaceVersion, 0, 0);
    snapshot->reserved = 0;
    snapshot->unityInterfaces = (uintptr_t)g_unityInterfaces;
    snapshot->unityD3D12Interface = (uintptr_t)g_unityD3D12;
    snapshot->unityDevice = (uintptr_t)g_unityDevice;
    snapshot->unityCommandQueue = (uintptr_t)g_unityCommandQueue;
    snapshot->unityFrameFence = (uintptr_t)g_unityFrameFence;
    snapshot->nextFrameFenceValue = g_nextFrameFenceValue;
    return 1;
}

__declspec(dllexport) int __stdcall spvr_queue_eye_copy(const SpvrEyeCopyRequest* request)
{
    ID3D12Resource* resources[7];
    size_t index;

    if (request == NULL || request->schema != SPVR_EYE_COPY_SCHEMA ||
        request->presentationGeneration <= 0 || request->sequence == 0 ||
        request->sourceLeft == 0 || request->sourceRight == 0 ||
        request->destinationLeft == 0 || request->destinationRight == 0 ||
        ((request->uiCompositeSource != 0 || request->uiWorldSource != 0 ||
          request->uiDestination != 0) &&
         (request->uiCompositeSource == 0 || request->uiWorldSource == 0 ||
          request->uiDestination == 0)))
        return 0;

    AcquireSRWLockExclusive(&g_eyeCopyLock);
    if (g_eyeCopyState != SPVR_EYE_COPY_IDLE)
    {
        ReleaseSRWLockExclusive(&g_eyeCopyLock);
        return 0;
    }
    g_eyeCopyRequest = *request;
    resources[0] = (ID3D12Resource*)request->sourceLeft;
    resources[1] = (ID3D12Resource*)request->sourceRight;
    resources[2] = (ID3D12Resource*)request->destinationLeft;
    resources[3] = (ID3D12Resource*)request->destinationRight;
    resources[4] = (ID3D12Resource*)request->uiCompositeSource;
    resources[5] = (ID3D12Resource*)request->uiWorldSource;
    resources[6] = (ID3D12Resource*)request->uiDestination;
    for (index = 0; index < 7; index++)
    {
        if (resources[index] != NULL)
            ID3D12Resource_AddRef(resources[index]);
    }
    g_eyeCopyHresult = S_OK;
    g_eyeCopyFenceValue = 0;
    g_eyeCopyCompletedFenceValue = 0;
    g_eyeCopyTelemetryFlags = 0;
    g_eyeCopyTelemetryLastStage = 0;
    g_eyeCopyTelemetryFailureStage = 0;
    g_eyeCopyTelemetryHresult = S_OK;
    g_eyeCopyTelemetryFirstCompletedFenceValue = 0;
    TraceEyeCopyStage(SPVR_EYE_TRACE_DELIVERED, SPVR_EYE_STAGE_NATIVE_DELIVERED);
    g_eyeCopyState = SPVR_EYE_COPY_PENDING;
    g_eyeCopyTelemetryState = SPVR_EYE_COPY_PENDING;
    ReleaseSRWLockExclusive(&g_eyeCopyLock);
    return 1;
}

__declspec(dllexport) int __stdcall spvr_eye_copy_needs_render_event(void)
{
    uint32_t state;
    AcquireSRWLockShared(&g_eyeCopyLock);
    state = g_eyeCopyState;
    ReleaseSRWLockShared(&g_eyeCopyLock);
    return state == SPVR_EYE_COPY_PENDING;
}

__declspec(dllexport) int __stdcall spvr_cancel_eye_copy(
    int64_t presentationGeneration,
    uint64_t sequence)
{
    int canceled = 0;
    AcquireSRWLockExclusive(&g_eyeCopyLock);
    if (g_eyeCopyState == SPVR_EYE_COPY_PENDING &&
        g_eyeCopyRequest.presentationGeneration == presentationGeneration &&
        g_eyeCopyRequest.sequence == sequence)
    {
        TraceEyeCopyFailure(SPVR_EYE_STAGE_CANCEL, S_OK);
        g_eyeCopyState = SPVR_EYE_COPY_CANCELED;
        g_eyeCopyTelemetryState = SPVR_EYE_COPY_CANCELED;
        canceled = 1;
    }
    ReleaseSRWLockExclusive(&g_eyeCopyLock);
    return canceled;
}

__declspec(dllexport) int __stdcall spvr_poll_eye_copy(SpvrEyeCopyStatus* status)
{
    uint32_t terminal;
    if (status == NULL)
        return 0;

    AcquireSRWLockExclusive(&g_eyeCopyLock);
    if (g_eyeCopyState == SPVR_EYE_COPY_SUBMITTED && g_eyeCopyCompletionFence != NULL)
    {
        g_eyeCopyCompletedFenceValue =
            ID3D12Fence_GetCompletedValue(g_eyeCopyCompletionFence);
        if ((g_eyeCopyTelemetryFlags & SPVR_EYE_TRACE_FENCE_FIRST_OBSERVED) == 0)
        {
            g_eyeCopyTelemetryFirstCompletedFenceValue = g_eyeCopyCompletedFenceValue;
            TraceEyeCopyStage(
                SPVR_EYE_TRACE_FENCE_FIRST_OBSERVED,
                SPVR_EYE_STAGE_FENCE_POLL_FIRST);
        }
        if (g_eyeCopyCompletedFenceValue == UINT64_MAX)
        {
            FailEyeCopy(SPVR_EYE_STAGE_DEVICE_REMOVED, DXGI_ERROR_DEVICE_REMOVED);
        }
        else if (g_eyeCopyCompletedFenceValue >= g_eyeCopyFenceValue)
        {
            TraceEyeCopyStage(SPVR_EYE_TRACE_FENCE_TERMINAL, SPVR_EYE_STAGE_FENCE_TERMINAL);
            g_eyeCopyState = SPVR_EYE_COPY_COMPLETED;
            g_eyeCopyTelemetryState = SPVR_EYE_COPY_COMPLETED;
        }
    }

    status->schema = SPVR_EYE_COPY_SCHEMA;
    status->state = g_eyeCopyState;
    status->hresult = (uint32_t)g_eyeCopyHresult;
    status->reserved = 0;
    status->presentationGeneration = g_eyeCopyRequest.presentationGeneration;
    status->sequence = g_eyeCopyRequest.sequence;
    status->frameFenceValue = g_eyeCopyFenceValue;
    status->completedFenceValue = g_eyeCopyCompletedFenceValue;
    terminal = g_eyeCopyState == SPVR_EYE_COPY_COMPLETED ||
        g_eyeCopyState == SPVR_EYE_COPY_FAILED ||
        g_eyeCopyState == SPVR_EYE_COPY_CANCELED;
    if (terminal)
    {
        ReleaseEyeCopyResources();
        g_eyeCopyState = SPVR_EYE_COPY_IDLE;
    }
    ReleaseSRWLockExclusive(&g_eyeCopyLock);
    return 1;
}

__declspec(dllexport) int __stdcall spvr_get_eye_copy_telemetry(
    SpvrEyeCopyTelemetry* telemetry)
{
    if (telemetry == NULL)
        return 0;

    AcquireSRWLockShared(&g_eyeCopyLock);
    telemetry->schema = 1u;
    telemetry->flags = g_eyeCopyTelemetryFlags;
    telemetry->state = g_eyeCopyTelemetryState;
    telemetry->lastStage = g_eyeCopyTelemetryLastStage;
    telemetry->hresult = (uint32_t)g_eyeCopyTelemetryHresult;
    telemetry->failureStage = g_eyeCopyTelemetryFailureStage;
    telemetry->reserved0 = 0;
    telemetry->reserved1 = 0;
    telemetry->presentationGeneration = g_eyeCopyRequest.presentationGeneration;
    telemetry->sequence = g_eyeCopyRequest.sequence;
    telemetry->targetFenceValue = g_eyeCopyFenceValue;
    telemetry->firstCompletedFenceValue = g_eyeCopyTelemetryFirstCompletedFenceValue;
    telemetry->latestCompletedFenceValue = g_eyeCopyCompletedFenceValue;
    ReleaseSRWLockShared(&g_eyeCopyLock);
    return 1;
}

/* Alpha-blends the accepted ring-and-dot pointer directly into an acquired
 * panel swapchain image.  The caller owns acquisition/release and guarantees
 * that destination is in D3D12_RESOURCE_STATE_RENDER_TARGET.  This function
 * uses the authoritative direct queue and leaves destination in that state. */
__declspec(dllexport) int __stdcall spvr_draw_panel_cursor(
    void* deviceVoid,
    void* commandQueueVoid,
    void* destinationVoid,
    float u,
    float v,
    float relativeSize)
{
    ID3D12Device* device = (ID3D12Device*)deviceVoid;
    ID3D12CommandQueue* commandQueue = (ID3D12CommandQueue*)commandQueueVoid;
    ID3D12Resource* destination = (ID3D12Resource*)destinationVoid;
    ID3D12CommandAllocator* allocator = NULL;
    ID3D12GraphicsCommandList* commandList = NULL;
    ID3D12DescriptorHeap* rtvHeap = NULL;
    ID3D12Fence* fence = NULL;
    HANDLE completionEvent = NULL;
    ID3D12CommandList* commandLists[1];
    D3D12_RESOURCE_DESC resourceDescription;
    D3D12_DESCRIPTOR_HEAP_DESC heapDescription;
    D3D12_RENDER_TARGET_VIEW_DESC viewDescription;
    D3D12_CPU_DESCRIPTOR_HANDLE rtvHandle;
    D3D12_VIEWPORT viewport;
    D3D12_RECT scissor;
    DXGI_FORMAT viewFormat;
    float cursorPixels;
    float left;
    float top;
    HRESULT result = E_FAIL;

    if (device == NULL || commandQueue == NULL || destination == NULL ||
        !isfinite(u) || !isfinite(v) || !isfinite(relativeSize) ||
        u < 0.0f || u > 1.0f || v < 0.0f || v > 1.0f ||
        relativeSize <= 0.0f || relativeSize > 0.25f)
        return E_INVALIDARG;

    resourceDescription = GetResourceDescription(destination);
    if (resourceDescription.Dimension != D3D12_RESOURCE_DIMENSION_TEXTURE2D ||
        resourceDescription.Width == 0 || resourceDescription.Height == 0 ||
        resourceDescription.SampleDesc.Count != 1)
        return E_INVALIDARG;
    viewFormat = CursorDrawViewFormat(resourceDescription.Format);
    if (viewFormat == DXGI_FORMAT_UNKNOWN)
        return E_INVALIDARG;

    AcquireSRWLockExclusive(&g_cursorDrawLock);
    result = EnsureCursorDrawPipeline(device, viewFormat);
    if (FAILED(result))
        goto cleanup;

    ZeroMemory(&heapDescription, sizeof(heapDescription));
    heapDescription.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
    heapDescription.NumDescriptors = 1;
    result = ID3D12Device_CreateDescriptorHeap(
        device,
        &heapDescription,
        &SpvrIidDescriptorHeap,
        (void**)&rtvHeap);
    if (FAILED(result))
        goto cleanup;
    ZeroMemory(&viewDescription, sizeof(viewDescription));
    viewDescription.Format = viewFormat;
    viewDescription.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2D;
    rtvHandle = GetCpuDescriptorStart(rtvHeap);
    ID3D12Device_CreateRenderTargetView(
        device,
        destination,
        &viewDescription,
        rtvHandle);

    result = ID3D12Device_CreateCommandAllocator(
        device,
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        &SpvrIidCommandAllocator,
        (void**)&allocator);
    if (FAILED(result))
        goto cleanup;
    result = ID3D12Device_CreateCommandList(
        device,
        0,
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        allocator,
        g_cursorDrawPipelineState,
        &SpvrIidGraphicsCommandList,
        (void**)&commandList);
    if (FAILED(result))
        goto cleanup;

    cursorPixels = (float)(resourceDescription.Width < resourceDescription.Height
        ? resourceDescription.Width
        : resourceDescription.Height) * relativeSize;
    if (cursorPixels < 1.0f)
        cursorPixels = 1.0f;
    left = u * (float)resourceDescription.Width - cursorPixels * 0.5f;
    top = v * (float)resourceDescription.Height - cursorPixels * 0.5f;
    ZeroMemory(&viewport, sizeof(viewport));
    viewport.TopLeftX = left;
    viewport.TopLeftY = top;
    viewport.Width = cursorPixels;
    viewport.Height = cursorPixels;
    viewport.MaxDepth = 1.0f;
    scissor.left = (LONG)floorf(left);
    scissor.top = (LONG)floorf(top);
    scissor.right = (LONG)ceilf(left + cursorPixels);
    scissor.bottom = (LONG)ceilf(top + cursorPixels);
    if (scissor.left < 0)
        scissor.left = 0;
    if (scissor.top < 0)
        scissor.top = 0;
    if (scissor.right > (LONG)resourceDescription.Width)
        scissor.right = (LONG)resourceDescription.Width;
    if (scissor.bottom > (LONG)resourceDescription.Height)
        scissor.bottom = (LONG)resourceDescription.Height;

    ID3D12GraphicsCommandList_SetGraphicsRootSignature(
        commandList,
        g_cursorDrawRootSignature);
    ID3D12GraphicsCommandList_SetPipelineState(
        commandList,
        g_cursorDrawPipelineState);
    ID3D12GraphicsCommandList_IASetPrimitiveTopology(
        commandList,
        D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    ID3D12GraphicsCommandList_RSSetViewports(commandList, 1, &viewport);
    ID3D12GraphicsCommandList_RSSetScissorRects(commandList, 1, &scissor);
    ID3D12GraphicsCommandList_OMSetRenderTargets(
        commandList,
        1,
        &rtvHandle,
        FALSE,
        NULL);
    ID3D12GraphicsCommandList_DrawInstanced(commandList, 3, 1, 0, 0);
    result = ID3D12GraphicsCommandList_Close(commandList);
    if (FAILED(result))
        goto cleanup;
    commandLists[0] = (ID3D12CommandList*)commandList;
    ID3D12CommandQueue_ExecuteCommandLists(commandQueue, 1, commandLists);

    result = ID3D12Device_CreateFence(
        device,
        0,
        D3D12_FENCE_FLAG_NONE,
        &SpvrIidFence,
        (void**)&fence);
    if (FAILED(result))
        goto cleanup;
    result = ID3D12CommandQueue_Signal(commandQueue, fence, 1);
    if (FAILED(result))
        goto cleanup;
    completionEvent = CreateEventW(NULL, FALSE, FALSE, NULL);
    if (completionEvent == NULL)
    {
        result = HRESULT_FROM_WIN32(GetLastError());
        goto cleanup;
    }
    result = ID3D12Fence_SetEventOnCompletion(fence, 1, completionEvent);
    if (FAILED(result))
        goto cleanup;
    if (WaitForSingleObject(completionEvent, 5000) != WAIT_OBJECT_0)
    {
        result = HRESULT_FROM_WIN32(WAIT_TIMEOUT);
        goto cleanup;
    }
    result = S_OK;

cleanup:
    if (completionEvent != NULL)
        CloseHandle(completionEvent);
    if (fence != NULL)
        ID3D12Fence_Release(fence);
    if (commandList != NULL)
        ID3D12GraphicsCommandList_Release(commandList);
    if (allocator != NULL)
        ID3D12CommandAllocator_Release(allocator);
    if (rtvHeap != NULL)
        ID3D12DescriptorHeap_Release(rtvHeap);
    ReleaseSRWLockExclusive(&g_cursorDrawLock);
    return (int)result;
}

/* Creates one self-owned RGBA8 ring-and-dot pointer texture in
 * D3D12_RESOURCE_STATE_COMMON and waits for GPU completion before returning.
 * The caller owns the returned ID3D12Resource and may use it as a COPY_SOURCE
 * from any thread without touching Unity-managed resource state. */
__declspec(dllexport) int __stdcall spvr_create_ring_texture(
    void* deviceVoid,
    void* commandQueueVoid,
    uint32_t size,
    void** outResource)
{
    ID3D12Device* device = (ID3D12Device*)deviceVoid;
    ID3D12CommandQueue* commandQueue = (ID3D12CommandQueue*)commandQueueVoid;
    ID3D12Resource* uploadBuffer = NULL;
    ID3D12Resource* ringTexture = NULL;
    ID3D12CommandAllocator* allocator = NULL;
    ID3D12GraphicsCommandList* commandList = NULL;
    ID3D12Fence* fence = NULL;
    HANDLE completionEvent = NULL;
    D3D12_HEAP_PROPERTIES uploadHeapProperties;
    D3D12_HEAP_PROPERTIES defaultHeapProperties;
    D3D12_RESOURCE_DESC bufferDescription;
    D3D12_RESOURCE_DESC textureDescription;
    D3D12_RESOURCE_BARRIER barriers[2];
    D3D12_TEXTURE_COPY_LOCATION destinationLocation;
    D3D12_TEXTURE_COPY_LOCATION sourceLocation;
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint;
    D3D12_RANGE mapRange;
    void* mappedData = NULL;
    ID3D12CommandList* commandLists[1];
    uint8_t* pixels = NULL;
    HRESULT result;
    UINT pixelBytes;

    if (outResource != NULL)
        *outResource = NULL;
    if (device == NULL || commandQueue == NULL || outResource == NULL)
        return E_INVALIDARG;
    if (size == 0 || size > 4096 || (size * 4) % 256 != 0)
        return E_INVALIDARG; /* buffer-to-texture copies need 256-byte row pitch */

    pixelBytes = size * size * 4u;
    pixels = (uint8_t*)HeapAlloc(GetProcessHeap(), 0, pixelBytes);
    if (pixels == NULL)
        return E_OUTOFMEMORY;
    {
        float center = ((float)size - 1.0f) * 0.5f;
        float radius = (float)size * 0.38f;
        float thickness = (float)size * 0.055f;
        float dotRadius = (float)size * 0.07f;
        for (uint32_t y = 0; y < size; ++y)
        {
            for (uint32_t x = 0; x < size; ++x)
            {
                float dx = (float)x - center;
                float dy = (float)y - center;
                float distance = sqrtf(dx * dx + dy * dy);
                uint8_t alpha = 0;
                if (fabsf(distance - radius) <= thickness || distance <= dotRadius)
                    alpha = 255;
                /* Premultiplied-alpha safe: transparent pixels carry zero RGB so
                 * compositors that interpret the layer as premultiplied render a
                 * ring instead of an opaque box. */
                pixels[((y * size) + x) * 4 + 0] = alpha;
                pixels[((y * size) + x) * 4 + 1] = alpha;
                pixels[((y * size) + x) * 4 + 2] = alpha;
                pixels[((y * size) + x) * 4 + 3] = alpha;
            }
        }
    }

    ZeroMemory(&uploadHeapProperties, sizeof(uploadHeapProperties));
    uploadHeapProperties.Type = D3D12_HEAP_TYPE_UPLOAD;
    ZeroMemory(&bufferDescription, sizeof(bufferDescription));
    bufferDescription.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    bufferDescription.Width = pixelBytes;
    bufferDescription.Height = 1;
    bufferDescription.DepthOrArraySize = 1;
    bufferDescription.MipLevels = 1;
    bufferDescription.Format = DXGI_FORMAT_UNKNOWN;
    bufferDescription.SampleDesc.Count = 1;
    bufferDescription.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    result = ID3D12Device_CreateCommittedResource(
        device,
        &uploadHeapProperties,
        D3D12_HEAP_FLAG_NONE,
        &bufferDescription,
        D3D12_RESOURCE_STATE_GENERIC_READ,
        NULL,
        &SpvrIidResource,
        (void**)&uploadBuffer);
    if (FAILED(result))
        goto cleanup;

    mapRange.Begin = 0;
    mapRange.End = 0;
    result = ID3D12Resource_Map(uploadBuffer, 0, &mapRange, &mappedData);
    if (FAILED(result))
        goto cleanup;
    memcpy(mappedData, pixels, pixelBytes);
    ID3D12Resource_Unmap(uploadBuffer, 0, NULL);

    ZeroMemory(&defaultHeapProperties, sizeof(defaultHeapProperties));
    defaultHeapProperties.Type = D3D12_HEAP_TYPE_DEFAULT;
    ZeroMemory(&textureDescription, sizeof(textureDescription));
    textureDescription.Dimension = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
    textureDescription.Width = size;
    textureDescription.Height = size;
    textureDescription.DepthOrArraySize = 1;
    textureDescription.MipLevels = 1;
    textureDescription.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    textureDescription.SampleDesc.Count = 1;
    result = ID3D12Device_CreateCommittedResource(
        device,
        &defaultHeapProperties,
        D3D12_HEAP_FLAG_NONE,
        &textureDescription,
        D3D12_RESOURCE_STATE_COMMON,
        NULL,
        &SpvrIidResource,
        (void**)&ringTexture);
    if (FAILED(result))
        goto cleanup;

    result = ID3D12Device_CreateCommandAllocator(
        device,
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        &SpvrIidCommandAllocator,
        (void**)&allocator);
    if (FAILED(result))
        goto cleanup;
    result = ID3D12Device_CreateCommandList(
        device,
        0,
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        allocator,
        NULL,
        &SpvrIidGraphicsCommandList,
        (void**)&commandList);
    if (FAILED(result))
        goto cleanup;

    barriers[0].Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barriers[0].Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
    barriers[0].Transition.pResource = ringTexture;
    barriers[0].Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON;
    barriers[0].Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
    barriers[0].Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    ID3D12GraphicsCommandList_ResourceBarrier(commandList, 1, barriers);

    destinationLocation.pResource = ringTexture;
    destinationLocation.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    destinationLocation.SubresourceIndex = 0;
    sourceLocation.pResource = uploadBuffer;
    sourceLocation.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
    footprint.Footprint.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    footprint.Footprint.Width = size;
    footprint.Footprint.Height = size;
    footprint.Footprint.Depth = 1;
    footprint.Footprint.RowPitch = size * 4u;
    footprint.Offset = 0;
    sourceLocation.PlacedFootprint = footprint;
    ID3D12GraphicsCommandList_CopyTextureRegion(
        commandList,
        &destinationLocation,
        0,
        0,
        0,
        &sourceLocation,
        NULL);

    barriers[1].Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barriers[1].Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
    barriers[1].Transition.pResource = ringTexture;
    barriers[1].Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
    barriers[1].Transition.StateAfter = D3D12_RESOURCE_STATE_COMMON;
    barriers[1].Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
    ID3D12GraphicsCommandList_ResourceBarrier(commandList, 1, barriers);

    result = ID3D12GraphicsCommandList_Close(commandList);
    if (FAILED(result))
        goto cleanup;

    commandLists[0] = (ID3D12CommandList*)commandList;
    ID3D12CommandQueue_ExecuteCommandLists(commandQueue, 1, commandLists);

    result = ID3D12Device_CreateFence(
        device,
        0,
        D3D12_FENCE_FLAG_NONE,
        &SpvrIidFence,
        (void**)&fence);
    if (FAILED(result))
        goto cleanup;
    result = ID3D12CommandQueue_Signal(commandQueue, fence, 1);
    if (FAILED(result))
        goto cleanup;
    completionEvent = CreateEventW(NULL, FALSE, FALSE, NULL);
    if (completionEvent == NULL)
    {
        result = HRESULT_FROM_WIN32(GetLastError());
        goto cleanup;
    }
    result = ID3D12Fence_SetEventOnCompletion(fence, 1, completionEvent);
    if (FAILED(result))
        goto cleanup;
    if (WaitForSingleObject(completionEvent, 5000) != WAIT_OBJECT_0)
    {
        result = HRESULT_FROM_WIN32(WAIT_TIMEOUT);
        goto cleanup;
    }

    *outResource = ringTexture;
    ringTexture = NULL;
    result = S_OK;

cleanup:
    if (completionEvent != NULL)
        CloseHandle(completionEvent);
    if (pixels != NULL)
        HeapFree(GetProcessHeap(), 0, pixels);
    if (uploadBuffer != NULL)
        ID3D12Resource_Release(uploadBuffer);
    if (commandList != NULL)
        ID3D12GraphicsCommandList_Release(commandList);
    if (allocator != NULL)
        ID3D12CommandAllocator_Release(allocator);
    if (fence != NULL)
        ID3D12Fence_Release(fence);
    if (ringTexture != NULL)
        ID3D12Resource_Release(ringTexture);
    return (int)result;
}
