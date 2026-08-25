param(
    [string]$LoaderPath = (Join-Path $PSScriptRoot '..\runtime\openxr_loader.dll')
)

$ErrorActionPreference = 'Stop'
$env:SONGPRISM_VR_OPENXR_LOADER = [System.IO.Path]::GetFullPath($LoaderPath)

$python = Get-Command python -ErrorAction SilentlyContinue
if ($null -eq $python) {
    throw 'python was not found on PATH.'
}

@'
import ctypes
import os
from ctypes import *

class XrApplicationInfo(Structure):
    _fields_ = [
        ("applicationName", c_char * 128),
        ("applicationVersion", c_uint32),
        ("engineName", c_char * 128),
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

class XrSessionCreateInfo(Structure):
    _fields_ = [
        ("type", c_int32),
        ("next", c_void_p),
        ("createFlags", c_uint64),
        ("systemId", c_uint64),
    ]

loader_path = os.environ["SONGPRISM_VR_OPENXR_LOADER"]
loader = WinDLL(loader_path)
loader.xrCreateInstance.argtypes = [POINTER(XrInstanceCreateInfo), POINTER(c_void_p)]
loader.xrCreateInstance.restype = c_long
loader.xrDestroyInstance.argtypes = [c_void_p]
loader.xrDestroyInstance.restype = c_long
loader.xrGetSystem.argtypes = [c_void_p, POINTER(XrSystemGetInfo), POINTER(c_uint64)]
loader.xrGetSystem.restype = c_long
loader.xrGetInstanceProcAddr.argtypes = [c_void_p, c_char_p, POINTER(c_void_p)]
loader.xrGetInstanceProcAddr.restype = c_long

app = XrApplicationInfo(b"SongPrismVROpenXrProbe", 1, b"Python", 1, (1 << 48))
create_info = XrInstanceCreateInfo(3, None, 0, app, 0, None, 0, None)
instance = c_void_p()
create_result = loader.xrCreateInstance(byref(create_info), byref(instance))
print("xrCreateInstance", create_result)

if create_result == 0 and instance.value:
    try:
        system_info = XrSystemGetInfo(4, None, 1)
        system_id = c_uint64()
        system_result = loader.xrGetSystem(instance, byref(system_info), byref(system_id))
        print("xrGetSystem", system_result, "systemId", system_id.value)
        if system_result == 0:
            function = c_void_p()
            proc_result = loader.xrGetInstanceProcAddr(instance, b"xrCreateSession", byref(function))
            print("xrGetInstanceProcAddr", proc_result, "function", hex(function.value or 0))
            if proc_result == 0 and function.value:
                CreateSession = CFUNCTYPE(c_long, c_void_p, POINTER(XrSessionCreateInfo), POINTER(c_void_p))
                session_info = XrSessionCreateInfo(8, None, 0, system_id.value)
                session = c_void_p()
                session_result = CreateSession(function.value)(instance, byref(session_info), byref(session))
                print("xrCreateSession(bare,no-extension)", session_result, "session", hex(session.value or 0))
    finally:
        loader.xrDestroyInstance(instance)
else:
    print("VR runtime may not be connected. Run this while Virtual Desktop is connected to the headset.")
'@ | & $python.Source -
