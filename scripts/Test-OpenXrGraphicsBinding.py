import argparse
import ctypes
from pathlib import Path
from ctypes import (
    CFUNCTYPE,
    POINTER,
    Structure,
    WinDLL,
    byref,
    c_char,
    c_char_p,
    c_int,
    c_int32,
    c_long,
    c_uint,
    c_uint32,
    c_uint64,
    c_void_p,
    cast,
    sizeof,
    string_at,
)

XR_SUCCESS = 0
XR_TYPE_EXTENSION_PROPERTIES = 2
XR_TYPE_INSTANCE_CREATE_INFO = 3
XR_TYPE_SYSTEM_GET_INFO = 4
XR_TYPE_INSTANCE_PROPERTIES = 32
XR_TYPE_VIEW_CONFIGURATION_VIEW = 41
XR_TYPE_SESSION_CREATE_INFO = 8
XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR = 1000027002
XR_TYPE_GRAPHICS_BINDING_D3D11_KHR = 1000027000
XR_TYPE_GRAPHICS_REQUIREMENTS_D3D12_KHR = 1000028002
XR_TYPE_GRAPHICS_BINDING_D3D12_KHR = 1000028000
XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY = 1
XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO = 2
MAX_EXTENSION_NAME_SIZE = 128
MAX_APPLICATION_NAME_SIZE = 128
MAX_ENGINE_NAME_SIZE = 128
MAX_RUNTIME_NAME_SIZE = 128

D3D_FEATURE_LEVEL_11_0 = 0xB000
D3D_FEATURE_LEVEL_12_0 = 0xC000
D3D_DRIVER_TYPE_HARDWARE = 1
D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20
D3D11_SDK_VERSION = 7
D3D12_COMMAND_LIST_TYPE_DIRECT = 0
IID_ID3D12Device = "189819f1-1db6-4b57-be54-1821339b85f7"
IID_ID3D12CommandQueue = "0ec870a6-5d7e-4c22-8cfc-5baae07616ed"

XR_RESULT_NAMES = {
    0: "XR_SUCCESS",
    -13: "XR_ERROR_INSTANCE_LOST",
    -35: "XR_ERROR_FORM_FACTOR_UNAVAILABLE",
    -38: "XR_ERROR_GRAPHICS_DEVICE_INVALID",
}


def result_name(value):
    return XR_RESULT_NAMES.get(value, f"UNKNOWN({value})")


def hresult_succeeded(value):
    return value >= 0


def guid_to_bytes(guid):
    from uuid import UUID
    return UUID(guid).bytes_le


class XrApplicationInfo(Structure):
    _fields_ = [
        ("applicationName", c_char * MAX_APPLICATION_NAME_SIZE),
        ("applicationVersion", c_uint32),
        ("engineName", c_char * MAX_ENGINE_NAME_SIZE),
        ("engineVersion", c_uint32),
        ("apiVersion", c_uint64),
    ]


class XrInstanceCreateInfo(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("createFlags", c_uint64),
        ("applicationInfo", XrApplicationInfo),
        ("enabledApiLayerCount", c_uint32),
        ("enabledApiLayerNames", c_void_p),
        ("enabledExtensionCount", c_uint32),
        ("enabledExtensionNames", c_void_p),
    ]


class XrSystemGetInfo(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("formFactor", c_int32),
    ]


class XrInstanceProperties(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("runtimeVersion", c_uint64),
        ("runtimeName", c_char * MAX_RUNTIME_NAME_SIZE),
    ]


class XrSessionCreateInfo(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("createFlags", c_uint64),
        ("systemId", c_uint64),
    ]


class XrExtensionProperties(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("extensionName", c_char * MAX_EXTENSION_NAME_SIZE),
        ("extensionVersion", c_uint32),
    ]


class XrViewConfigurationView(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("recommendedImageRectWidth", c_uint32),
        ("maxImageRectWidth", c_uint32),
        ("recommendedImageRectHeight", c_uint32),
        ("maxImageRectHeight", c_uint32),
        ("recommendedSwapchainSampleCount", c_uint32),
        ("maxSwapchainSampleCount", c_uint32),
    ]


class Luid(Structure):
    _fields_ = [
        ("lowPart", c_uint32),
        ("highPart", c_int32),
    ]


class XrGraphicsRequirementsD3D11(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("adapterLuid", Luid),
        ("minFeatureLevel", c_int32),
    ]


class XrGraphicsBindingD3D11(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("device", c_void_p),
    ]


class XrGraphicsRequirementsD3D12(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("adapterLuid", Luid),
        ("minFeatureLevel", c_int32),
    ]


class XrGraphicsBindingD3D12(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("device", c_void_p),
        ("queue", c_void_p),
    ]


class D3D12CommandQueueDescription(Structure):
    _fields_ = [
        ("type", c_int32),
        ("priority", c_int32),
        ("flags", c_int32),
        ("nodeMask", c_uint32),
    ]


def load_loader(path):
    loader = WinDLL(path)
    loader.xrEnumerateInstanceExtensionProperties.argtypes = [
        c_void_p,
        c_uint32,
        POINTER(c_uint32),
        c_void_p,
    ]
    loader.xrEnumerateInstanceExtensionProperties.restype = c_int32
    loader.xrCreateInstance.argtypes = [POINTER(XrInstanceCreateInfo), POINTER(c_void_p)]
    loader.xrCreateInstance.restype = c_int32
    loader.xrDestroyInstance.argtypes = [c_void_p]
    loader.xrDestroyInstance.restype = c_int32
    loader.xrGetSystem.argtypes = [c_void_p, POINTER(XrSystemGetInfo), POINTER(c_uint64)]
    loader.xrGetSystem.restype = c_int32
    loader.xrGetInstanceProcAddr.argtypes = [c_void_p, c_char_p, POINTER(c_void_p)]
    loader.xrGetInstanceProcAddr.restype = c_int32
    loader.xrGetInstanceProperties.argtypes = [c_void_p, POINTER(XrInstanceProperties)]
    loader.xrGetInstanceProperties.restype = c_int32
    loader.xrEnumerateViewConfigurationViews.argtypes = [
        c_void_p,
        c_uint64,
        c_int32,
        c_uint32,
        POINTER(c_uint32),
        c_void_p,
    ]
    loader.xrEnumerateViewConfigurationViews.restype = c_int32
    return loader


def enumerate_extensions(loader):
    loader.xrEnumerateInstanceExtensionProperties(None, 0, byref(c_uint32()), None)
    count = c_uint32()
    loader.xrEnumerateInstanceExtensionProperties(None, 0, byref(count), None)
    if count.value == 0:
        return []
    array_type = XrExtensionProperties * count.value
    props = array_type()
    for prop in props:
        prop.type = XR_TYPE_EXTENSION_PROPERTIES
    written = c_uint32()
    result = loader.xrEnumerateInstanceExtensionProperties(None, count, byref(written), props)
    if result != XR_SUCCESS:
        raise RuntimeError(f"xrEnumerateInstanceExtensionProperties failed: {result}")
    return [prop.extensionName.decode("utf-8", "replace").rstrip("\x00") for prop in props]


def create_instance(loader, extension_name):
    ext = extension_name.encode("utf-8") + b"\x00"
    ext_buffer = ctypes.create_string_buffer(ext)
    ext_array = (c_char_p * 1)(cast(ext_buffer, c_char_p))
    app = XrApplicationInfo(
        b"SongPrismVROpenXrGraphicsProbe",
        1,
        b"Python",
        1,
        (1 << 48),
    )
    create_info = XrInstanceCreateInfo(
        XR_TYPE_INSTANCE_CREATE_INFO,
        None,
        0,
        app,
        0,
        None,
        1,
        cast(ext_array, c_void_p),
    )
    instance = c_void_p()
    result = loader.xrCreateInstance(byref(create_info), byref(instance))
    if result != XR_SUCCESS or not instance.value:
        raise RuntimeError(f"xrCreateInstance failed: {result}")
    return instance


def get_system(loader, instance):
    info = XrSystemGetInfo(XR_TYPE_SYSTEM_GET_INFO, None, XR_FORM_FACTOR_HEAD_MOUNTED_DISPLAY)
    system_id = c_uint64()
    result = loader.xrGetSystem(instance, byref(info), byref(system_id))
    if result != XR_SUCCESS:
        raise RuntimeError(f"xrGetSystem failed: {result} ({result_name(result)})")
    return system_id.value


def resolve_function(loader, instance, name):
    function = c_void_p()
    result = loader.xrGetInstanceProcAddr(instance, name.encode("ascii"), byref(function))
    if result != XR_SUCCESS or not function.value:
        raise RuntimeError(f"xrGetInstanceProcAddr({name}) failed: {result}")
    return function.value


def enumerate_views(loader, instance, system_id):
    count = c_uint32()
    result = loader.xrEnumerateViewConfigurationViews(
        instance,
        system_id,
        XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,
        0,
        byref(count),
        None,
    )
    if result != XR_SUCCESS:
        raise RuntimeError(f"xrEnumerateViewConfigurationViews count failed: {result}")
    if count.value == 0:
        raise RuntimeError("no primary stereo view configuration")
    views = (XrViewConfigurationView * count.value)()
    for view in views:
        view.type = XR_TYPE_VIEW_CONFIGURATION_VIEW
    written = c_uint32()
    result = loader.xrEnumerateViewConfigurationViews(
        instance,
        system_id,
        XR_VIEW_CONFIGURATION_TYPE_PRIMARY_STEREO,
        count,
        byref(written),
        views,
    )
    if result != XR_SUCCESS:
        raise RuntimeError(f"xrEnumerateViewConfigurationViews failed: {result}")
    return views


def create_d3d11_device():
    d3d11 = WinDLL("d3d11.dll")
    d3d11.D3D11CreateDevice.argtypes = [
        c_void_p,
        c_int,
        c_void_p,
        c_uint,
        POINTER(c_int),
        c_uint,
        c_uint,
        POINTER(c_void_p),
        POINTER(c_int),
        POINTER(c_void_p),
    ]
    d3d11.D3D11CreateDevice.restype = c_long
    levels = (c_int * 2)(D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_11_0)
    device = c_void_p()
    context = c_void_p()
    feature_level = c_int()
    result = d3d11.D3D11CreateDevice(
        None,
        D3D_DRIVER_TYPE_HARDWARE,
        None,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        levels,
        2,
        D3D11_SDK_VERSION,
        byref(device),
        byref(feature_level),
        byref(context),
    )
    if not hresult_succeeded(result) or not device.value:
        raise RuntimeError(f"D3D11CreateDevice failed: 0x{result & 0xffffffff:08x}")
    return device.value


def create_d3d12_device_and_queue():
    d3d12 = WinDLL("d3d12.dll")
    d3d12.D3D12CreateDevice.argtypes = [c_void_p, c_int, c_void_p, POINTER(c_void_p)]
    d3d12.D3D12CreateDevice.restype = c_long
    device_iid = guid_to_bytes(IID_ID3D12Device)
    device = c_void_p()
    result = d3d12.D3D12CreateDevice(
        None,
        D3D_FEATURE_LEVEL_12_0,
        cast(ctypes.create_string_buffer(device_iid, 16), c_void_p),
        byref(device),
    )
    if not hresult_succeeded(result) or not device.value:
        raise RuntimeError(f"D3D12CreateDevice failed: 0x{result & 0xffffffff:08x}")

    vtable = cast(device, POINTER(POINTER(c_void_p))).contents
    create_command_queue = CFUNCTYPE(c_long, c_void_p, POINTER(D3D12CommandQueueDescription), c_void_p, POINTER(c_void_p))
    create_queue_fn = create_command_queue(vtable[8])
    desc = D3D12CommandQueueDescription(
        D3D12_COMMAND_LIST_TYPE_DIRECT,
        0,
        0,
        0,
    )
    queue_iid = guid_to_bytes(IID_ID3D12CommandQueue)
    queue = c_void_p()
    result = create_queue_fn(
        device,
        byref(desc),
        cast(ctypes.create_string_buffer(queue_iid, 16), c_void_p),
        byref(queue),
    )
    if not hresult_succeeded(result) or not queue.value:
        raise RuntimeError(f"CreateCommandQueue failed: 0x{result & 0xffffffff:08x}")
    return device.value, queue.value


def query_requirements(loader, instance, system_id, api):
    if api == "d3d11":
        function = resolve_function(loader, instance, "xrGetD3D11GraphicsRequirementsKHR")
        prototype = CFUNCTYPE(c_int32, c_void_p, c_uint64, POINTER(XrGraphicsRequirementsD3D11))
        req = XrGraphicsRequirementsD3D11(type=XR_TYPE_GRAPHICS_REQUIREMENTS_D3D11_KHR)
        result = prototype(function)(instance, system_id, byref(req))
        if result != XR_SUCCESS:
            raise RuntimeError(f"xrGetD3D11GraphicsRequirementsKHR failed: {result}")
        return req.adapterLuid, req.minFeatureLevel
    if api == "d3d12":
        function = resolve_function(loader, instance, "xrGetD3D12GraphicsRequirementsKHR")
        prototype = CFUNCTYPE(c_int32, c_void_p, c_uint64, POINTER(XrGraphicsRequirementsD3D12))
        req = XrGraphicsRequirementsD3D12(type=XR_TYPE_GRAPHICS_REQUIREMENTS_D3D12_KHR)
        result = prototype(function)(instance, system_id, byref(req))
        if result != XR_SUCCESS:
            raise RuntimeError(f"xrGetD3D12GraphicsRequirementsKHR failed: {result}")
        return req.adapterLuid, req.minFeatureLevel
    raise ValueError(api)


def create_session(loader, instance, system_id, api):
    create_session_addr = resolve_function(loader, instance, "xrCreateSession")
    create_session_prototype = CFUNCTYPE(
        c_int32,
        c_void_p,
        POINTER(XrSessionCreateInfo),
        POINTER(c_void_p),
    )
    create_session_fn = create_session_prototype(create_session_addr)

    session = c_void_p()
    if api == "d3d11":
        device = c_void_p(create_d3d11_device())
        binding = XrGraphicsBindingD3D11(
            XR_TYPE_GRAPHICS_BINDING_D3D11_KHR,
            None,
            device,
        )
        session_info = XrSessionCreateInfo(
            XR_TYPE_SESSION_CREATE_INFO,
            cast(byref(binding), c_void_p),
            0,
            system_id,
        )
        result = create_session_fn(instance, byref(session_info), byref(session))
        return result, session.value, device.value, None
    if api == "d3d12":
        device, queue = create_d3d12_device_and_queue()
        binding = XrGraphicsBindingD3D12(
            XR_TYPE_GRAPHICS_BINDING_D3D12_KHR,
            None,
            c_void_p(device),
            c_void_p(queue),
        )
        session_info = XrSessionCreateInfo(
            XR_TYPE_SESSION_CREATE_INFO,
            cast(byref(binding), c_void_p),
            0,
            system_id,
        )
        result = create_session_fn(instance, byref(session_info), byref(session))
        return result, session.value, device, queue
    raise ValueError(api)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--api", choices=["d3d11", "d3d12"], required=True)
    args = parser.parse_args()

    loader_path = str((Path(__file__).resolve().parent.parent / "runtime" / "openxr_loader.dll"))
    loader = load_loader(loader_path)
    print("loader", loader_path)
    extensions = enumerate_extensions(loader)
    print("extensionCount", len(extensions))
    print("hasD3D11", "XR_KHR_D3D11_enable" in extensions)
    print("hasD3D12", "XR_KHR_D3D12_enable" in extensions)

    extension = "XR_KHR_D3D11_enable" if args.api == "d3d11" else "XR_KHR_D3D12_enable"
    if extension not in extensions:
        print(f"result=missing {extension}")
        return

    instance = create_instance(loader, extension)
    try:
        system_id = get_system(loader, instance)
        print("xrGetSystem 0 systemId", system_id)
        enumerate_views(loader, instance, system_id)
        luid, feature_level = query_requirements(loader, instance, system_id, args.api)
        print(
            "requiredAdapterLuid",
            f"0x{luid.highPart & 0xffffffff:08x}:{luid.lowPart & 0xffffffff:08x}",
            "requiredMinFeatureLevel",
            hex(feature_level & 0xffffffff),
        )
        result, session, device, queue = create_session(loader, instance, system_id, args.api)
        print("xrCreateSession", result, f"({result_name(result)})", "session", hex(session or 0))
        if session:
            destroy_session_addr = resolve_function(loader, instance, "xrDestroySession")
            destroy_session = CFUNCTYPE(c_int32, c_void_p, c_void_p)
            destroy_session(session)
    finally:
        loader.xrDestroyInstance(instance)


if __name__ == "__main__":
    main()
