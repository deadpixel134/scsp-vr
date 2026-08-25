using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SongPrismVR.Core;

namespace Doorstop;

internal readonly record struct OpenXrControllerPose(
    float OrientationX,
    float OrientationY,
    float OrientationZ,
    float OrientationW,
    float PositionX,
    float PositionY,
    float PositionZ);

internal readonly record struct OpenXrControllerFrame(
    bool PanelEnabled,
    bool PanelPoseTracked,
    OpenXrControllerPose PanelPose,
    bool PointerAimTracked,
    OpenXrControllerPose PointerAimPose,
    float PointerTriggerValue,
    bool PointerPrimaryPressed,
    bool PointerBackPressed,
    float PointerThumbstickX,
    float PointerThumbstickY,
    bool LocomotionThumbstickActive,
    float LocomotionThumbstickX,
    float LocomotionThumbstickY,
    bool ViewTurnThumbstickActive,
    float ViewTurnThumbstickX,
    float ViewTurnThumbstickY);

internal sealed class OpenXrControllerActions : IDisposable
{
    private const int XrSuccess = 0;
    private const int XrSessionNotFocused = 8;
    private const int XrTypeActionStateBoolean = 23;
    private const int XrTypeActionStateFloat = 24;
    private const int XrTypeActionStateVector2f = 25;
    private const int XrTypeActionStatePose = 27;
    private const int XrTypeActionSetCreateInfo = 28;
    private const int XrTypeActionCreateInfo = 29;
    private const int XrTypeActionStateGetInfo = 58;
    private const int XrTypeActionSpaceCreateInfo = 38;
    private const int XrTypeSpaceLocation = 42;
    private const int XrTypeInteractionProfileSuggestedBinding = 51;
    private const int XrTypeSessionActionSetsAttachInfo = 60;
    private const int XrTypeActionsSyncInfo = 61;
    private const int XrActionTypeBooleanInput = 1;
    private const int XrActionTypeFloatInput = 2;
    private const int XrActionTypeVector2fInput = 3;
    private const int XrActionTypePoseInput = 4;
    private const ulong XrSpaceLocationPoseValidAndTracked = 0xFUL;
    private const float GripPressThreshold = 0.72f;
    private const float GripReleaseThreshold = 0.25f;

    private readonly IntPtr _actionSet;
    private readonly IntPtr _session;
    private readonly IntPtr _gripPoseAction;
    private readonly IntPtr _aimPoseAction;
    private readonly IntPtr _squeezeValueAction;
    private readonly IntPtr _triggerValueAction;
    private readonly IntPtr _primaryClickAction;
    private readonly IntPtr _secondaryClickAction;
    private readonly IntPtr _thumbstickAction;
    private readonly ulong _leftHandPath;
    private readonly ulong _rightHandPath;
    private readonly LocateSpaceDelegate _locateSpace;
    private readonly SyncActionsDelegate _syncActions;
    private readonly GetActionStateBooleanDelegate _getBoolean;
    private readonly GetActionStateFloatDelegate _getFloat;
    private readonly GetActionStateVector2fDelegate _getVector2f;
    private readonly GetActionStatePoseDelegate _getPose;
    private readonly DestroySpaceDelegate _destroySpace;
    private readonly DestroyActionDelegate _destroyAction;
    private readonly DestroyActionSetDelegate _destroyActionSet;
    private readonly IntPtr _activeActionSetPointer;
    private readonly VrPanelSettings _panelSettings;
    private readonly VrInputSettings _inputSettings;
    private readonly VrTrackingSettings _trackingSettings;
    private readonly GripToggleLatch _panelToggle;
    private long _nextSqueezeReadFailureLogTimestamp;
    private long _nextTriggerReadFailureLogTimestamp;
    private long _nextPrimaryReadFailureLogTimestamp;
    private long _nextSecondaryReadFailureLogTimestamp;
    private long _nextLocomotionThumbstickReadFailureLogTimestamp;
    private long _nextViewTurnThumbstickReadFailureLogTimestamp;
    private bool _disposed;

    private OpenXrControllerActions(
        IntPtr session,
        IntPtr actionSet,
        IntPtr gripPoseAction,
        IntPtr aimPoseAction,
        IntPtr squeezeValueAction,
        IntPtr triggerValueAction,
        IntPtr primaryClickAction,
        IntPtr secondaryClickAction,
        IntPtr thumbstickAction,
        ulong leftHandPath,
        ulong rightHandPath,
        IntPtr leftGripSpace,
        IntPtr rightGripSpace,
        IntPtr leftAimSpace,
        IntPtr rightAimSpace,
        LocateSpaceDelegate locateSpace,
        SyncActionsDelegate syncActions,
        GetActionStateBooleanDelegate getBoolean,
        GetActionStateFloatDelegate getFloat,
        GetActionStateVector2fDelegate getVector2f,
        GetActionStatePoseDelegate getPose,
        DestroySpaceDelegate destroySpace,
        DestroyActionDelegate destroyAction,
        DestroyActionSetDelegate destroyActionSet,
        VrSettings settings)
    {
        _session = session;
        _actionSet = actionSet;
        _gripPoseAction = gripPoseAction;
        _aimPoseAction = aimPoseAction;
        _squeezeValueAction = squeezeValueAction;
        _triggerValueAction = triggerValueAction;
        _primaryClickAction = primaryClickAction;
        _secondaryClickAction = secondaryClickAction;
        _thumbstickAction = thumbstickAction;
        _leftHandPath = leftHandPath;
        _rightHandPath = rightHandPath;
        LeftGripSpace = leftGripSpace;
        RightGripSpace = rightGripSpace;
        LeftAimSpace = leftAimSpace;
        RightAimSpace = rightAimSpace;
        _locateSpace = locateSpace;
        _syncActions = syncActions;
        _getBoolean = getBoolean;
        _getFloat = getFloat;
        _getVector2f = getVector2f;
        _getPose = getPose;
        _destroySpace = destroySpace;
        _destroyAction = destroyAction;
        _destroyActionSet = destroyActionSet;
        _panelSettings = settings.Panel;
        _inputSettings = settings.Input;
        _trackingSettings = settings.Tracking;
        _panelToggle = new GripToggleLatch(
            GripPressThreshold,
            GripReleaseThreshold,
            Stopwatch.Frequency / 4,
            settings.Panel.StartEnabled);
        _activeActionSetPointer = Marshal.AllocHGlobal(Marshal.SizeOf<XrActiveActionSet>());
        Marshal.StructureToPtr(
            new XrActiveActionSet { ActionSet = actionSet },
            _activeActionSetPointer,
            fDeleteOld: false);
    }

    public IntPtr LeftGripSpace { get; }

    public IntPtr RightGripSpace { get; }

    public IntPtr LeftAimSpace { get; }

    public IntPtr RightAimSpace { get; }

    public bool PanelEnabled => _panelToggle.Enabled;

    public static OpenXrControllerActions? TryCreate(
        IntPtr loader,
        IntPtr instance,
        IntPtr session,
        VrSettings settings)
    {
        try
        {
            return Create(loader, instance, session, settings);
        }
        catch (Exception exception)
        {
            Log("openxr-controller-actions-failure", exception.Message, exception);
            return null;
        }
    }

    private static OpenXrControllerActions? Create(
        IntPtr loader,
        IntPtr instance,
        IntPtr session,
        VrSettings settings)
    {
        ValidateAbi();
        CreateActionSetDelegate createActionSet = LoadExport<CreateActionSetDelegate>(loader, "xrCreateActionSet");
        DestroyActionSetDelegate destroyActionSet = LoadExport<DestroyActionSetDelegate>(loader, "xrDestroyActionSet");
        CreateActionDelegate createAction = LoadExport<CreateActionDelegate>(loader, "xrCreateAction");
        DestroyActionDelegate destroyAction = LoadExport<DestroyActionDelegate>(loader, "xrDestroyAction");
        StringToPathDelegate stringToPath = LoadExport<StringToPathDelegate>(loader, "xrStringToPath");
        SuggestBindingsDelegate suggestBindings = LoadExport<SuggestBindingsDelegate>(loader, "xrSuggestInteractionProfileBindings");
        AttachActionSetsDelegate attachActionSets = LoadExport<AttachActionSetsDelegate>(loader, "xrAttachSessionActionSets");
        CreateActionSpaceDelegate createActionSpace = LoadExport<CreateActionSpaceDelegate>(loader, "xrCreateActionSpace");
        DestroySpaceDelegate destroySpace = LoadExport<DestroySpaceDelegate>(loader, "xrDestroySpace");
        LocateSpaceDelegate locateSpace = LoadExport<LocateSpaceDelegate>(loader, "xrLocateSpace");
        SyncActionsDelegate syncActions = LoadExport<SyncActionsDelegate>(loader, "xrSyncActions");
        GetActionStateBooleanDelegate getBoolean = LoadExport<GetActionStateBooleanDelegate>(loader, "xrGetActionStateBoolean");
        GetActionStateFloatDelegate getFloat = LoadExport<GetActionStateFloatDelegate>(loader, "xrGetActionStateFloat");
        GetActionStateVector2fDelegate getVector2f = LoadExport<GetActionStateVector2fDelegate>(loader, "xrGetActionStateVector2f");
        GetActionStatePoseDelegate getPose = LoadExport<GetActionStatePoseDelegate>(loader, "xrGetActionStatePose");

        IntPtr actionSet = IntPtr.Zero;
        IntPtr gripPoseAction = IntPtr.Zero;
        IntPtr aimPoseAction = IntPtr.Zero;
        IntPtr squeezeValueAction = IntPtr.Zero;
        IntPtr triggerValueAction = IntPtr.Zero;
        IntPtr primaryClickAction = IntPtr.Zero;
        IntPtr secondaryClickAction = IntPtr.Zero;
        IntPtr thumbstickAction = IntPtr.Zero;
        IntPtr leftGripSpace = IntPtr.Zero;
        IntPtr rightGripSpace = IntPtr.Zero;
        IntPtr leftAimSpace = IntPtr.Zero;
        IntPtr rightAimSpace = IntPtr.Zero;
        IntPtr subactionPathsPointer = IntPtr.Zero;
        IntPtr bindingsPointer = IntPtr.Zero;
        IntPtr actionSetsPointer = IntPtr.Zero;
        try
        {
            ulong leftHandPath = ToPath(stringToPath, instance, "/user/hand/left");
            ulong rightHandPath = ToPath(stringToPath, instance, "/user/hand/right");
            ulong interactionProfile = ToPath(
                stringToPath,
                instance,
                "/interaction_profiles/oculus/touch_controller");

            XrActionSetCreateInfo actionSetInfo = new()
            {
                Type = XrTypeActionSetCreateInfo,
                ActionSetName = FixedUtf8("songprism_vr", 64),
                LocalizedActionSetName = FixedUtf8("SongPrism VR", 128),
                Priority = 0
            };
            Check(createActionSet(instance, ref actionSetInfo, out actionSet), "create controller action set");

            subactionPathsPointer = Marshal.AllocHGlobal(2 * sizeof(long));
            Marshal.WriteInt64(subactionPathsPointer, unchecked((long)leftHandPath));
            Marshal.WriteInt64(IntPtr.Add(subactionPathsPointer, sizeof(long)), unchecked((long)rightHandPath));
            gripPoseAction = CreateAction(createAction, actionSet, subactionPathsPointer, "grip_pose", "Grip Pose", XrActionTypePoseInput);
            aimPoseAction = CreateAction(createAction, actionSet, subactionPathsPointer, "aim_pose", "Aim Pose", XrActionTypePoseInput);
            squeezeValueAction = CreateAction(createAction, actionSet, subactionPathsPointer, "squeeze_value", "Grip", XrActionTypeFloatInput);
            triggerValueAction = CreateAction(createAction, actionSet, subactionPathsPointer, "trigger_value", "Trigger", XrActionTypeFloatInput);
            primaryClickAction = CreateAction(createAction, actionSet, subactionPathsPointer, "primary_click", "Primary Click", XrActionTypeBooleanInput);
            secondaryClickAction = CreateAction(createAction, actionSet, subactionPathsPointer, "secondary_click", "Secondary Click", XrActionTypeBooleanInput);
            thumbstickAction = CreateAction(createAction, actionSet, subactionPathsPointer, "thumbstick", "Thumbstick", XrActionTypeVector2fInput);

            XrActionSuggestedBinding[] bindings =
            {
                Bind(gripPoseAction, ToPath(stringToPath, instance, "/user/hand/left/input/grip/pose")),
                Bind(gripPoseAction, ToPath(stringToPath, instance, "/user/hand/right/input/grip/pose")),
                Bind(aimPoseAction, ToPath(stringToPath, instance, "/user/hand/left/input/aim/pose")),
                Bind(aimPoseAction, ToPath(stringToPath, instance, "/user/hand/right/input/aim/pose")),
                Bind(squeezeValueAction, ToPath(stringToPath, instance, "/user/hand/left/input/squeeze/value")),
                Bind(squeezeValueAction, ToPath(stringToPath, instance, "/user/hand/right/input/squeeze/value")),
                Bind(triggerValueAction, ToPath(stringToPath, instance, "/user/hand/left/input/trigger/value")),
                Bind(triggerValueAction, ToPath(stringToPath, instance, "/user/hand/right/input/trigger/value")),
                Bind(primaryClickAction, ToPath(stringToPath, instance, "/user/hand/left/input/x/click")),
                Bind(primaryClickAction, ToPath(stringToPath, instance, "/user/hand/right/input/a/click")),
                Bind(secondaryClickAction, ToPath(stringToPath, instance, "/user/hand/left/input/y/click")),
                Bind(secondaryClickAction, ToPath(stringToPath, instance, "/user/hand/right/input/b/click")),
                Bind(thumbstickAction, ToPath(stringToPath, instance, "/user/hand/left/input/thumbstick")),
                Bind(thumbstickAction, ToPath(stringToPath, instance, "/user/hand/right/input/thumbstick"))
            };
            int bindingSize = Marshal.SizeOf<XrActionSuggestedBinding>();
            bindingsPointer = Marshal.AllocHGlobal(bindingSize * bindings.Length);
            for (int index = 0; index < bindings.Length; index++)
            {
                Marshal.StructureToPtr(
                    bindings[index],
                    IntPtr.Add(bindingsPointer, index * bindingSize),
                    fDeleteOld: false);
            }
            XrInteractionProfileSuggestedBinding suggested = new()
            {
                Type = XrTypeInteractionProfileSuggestedBinding,
                InteractionProfile = interactionProfile,
                CountSuggestedBindings = checked((uint)bindings.Length),
                SuggestedBindings = bindingsPointer
            };
            Check(suggestBindings(instance, ref suggested), "suggest Quest Touch controller bindings");

            actionSetsPointer = Marshal.AllocHGlobal(IntPtr.Size);
            Marshal.WriteIntPtr(actionSetsPointer, actionSet);
            XrSessionActionSetsAttachInfo attachInfo = new()
            {
                Type = XrTypeSessionActionSetsAttachInfo,
                CountActionSets = 1,
                ActionSets = actionSetsPointer
            };
            Check(attachActionSets(session, ref attachInfo), "attach controller action set");

            leftGripSpace = CreateSpace(createActionSpace, session, gripPoseAction, leftHandPath);
            rightGripSpace = CreateSpace(createActionSpace, session, gripPoseAction, rightHandPath);
            leftAimSpace = CreateSpace(createActionSpace, session, aimPoseAction, leftHandPath);
            rightAimSpace = CreateSpace(createActionSpace, session, aimPoseAction, rightHandPath);

            OpenXrControllerActions result = new(
                session,
                actionSet,
                gripPoseAction,
                aimPoseAction,
                squeezeValueAction,
                triggerValueAction,
                primaryClickAction,
                secondaryClickAction,
                thumbstickAction,
                leftHandPath,
                rightHandPath,
                leftGripSpace,
                rightGripSpace,
                leftAimSpace,
                rightAimSpace,
                locateSpace,
                syncActions,
                getBoolean,
                getFloat,
                getVector2f,
                getPose,
                destroySpace,
                destroyAction,
                destroyActionSet,
                settings);
            actionSet = IntPtr.Zero;
            gripPoseAction = IntPtr.Zero;
            aimPoseAction = IntPtr.Zero;
            squeezeValueAction = IntPtr.Zero;
            triggerValueAction = IntPtr.Zero;
            primaryClickAction = IntPtr.Zero;
            secondaryClickAction = IntPtr.Zero;
            thumbstickAction = IntPtr.Zero;
            leftGripSpace = IntPtr.Zero;
            rightGripSpace = IntPtr.Zero;
            leftAimSpace = IntPtr.Zero;
            rightAimSpace = IntPtr.Zero;
            Log(
                "openxr-controller-actions-ready",
                $"Quest Touch actions are attached;panelHand={settings.Panel.PanelHand};pointerHand={settings.Panel.PointerHand};toggle={settings.Panel.ToggleBinding};startEnabled={settings.Panel.StartEnabled};locomotionHand={settings.Tracking.LocomotionHand};viewTurnHand={OppositeHand(settings.Tracking.LocomotionHand)};viewTurnMode={settings.Tracking.ViewTurnMode};viewSnapAngle={settings.Tracking.ViewSnapAngleDegrees}.");
            return result;
        }
        catch (Exception exception)
        {
            Log("openxr-controller-actions-failure", exception.Message, exception);
            DestroySpace(rightAimSpace, destroySpace);
            DestroySpace(leftAimSpace, destroySpace);
            DestroySpace(rightGripSpace, destroySpace);
            DestroySpace(leftGripSpace, destroySpace);
            DestroyAction(thumbstickAction, destroyAction);
            DestroyAction(secondaryClickAction, destroyAction);
            DestroyAction(primaryClickAction, destroyAction);
            DestroyAction(triggerValueAction, destroyAction);
            DestroyAction(squeezeValueAction, destroyAction);
            DestroyAction(aimPoseAction, destroyAction);
            DestroyAction(gripPoseAction, destroyAction);
            if (actionSet != IntPtr.Zero)
            {
                _ = destroyActionSet(actionSet);
            }
            return null;
        }
        finally
        {
            if (actionSetsPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(actionSetsPointer);
            }
            if (bindingsPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(bindingsPointer);
            }
            if (subactionPathsPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(subactionPathsPointer);
            }
        }
    }

    public OpenXrControllerFrame Update(long predictedDisplayTime, IntPtr baseSpace)
    {
        XrActionsSyncInfo syncInfo = new()
        {
            Type = XrTypeActionsSyncInfo,
            CountActiveActionSets = 1,
            ActiveActionSets = _activeActionSetPointer
        };
        int syncResult = _syncActions(_session, ref syncInfo);
        if (syncResult != XrSuccess && syncResult != XrSessionNotFocused)
        {
            Check(syncResult, "sync controller actions");
        }
        if (syncResult == XrSessionNotFocused)
        {
            return new OpenXrControllerFrame(
                PanelEnabled,
                false,
                default,
                false,
                default,
                0f,
                false,
                false,
                0f,
                0f,
                false,
                0f,
                0f,
                false,
                0f,
                0f);
        }

        ulong panelHandPath = PathForHand(_panelSettings.PanelHand);
        ulong pointerHandPath = PathForHand(_panelSettings.PointerHand);
        bool toggleActive;
        float toggleValue;
        if (_panelSettings.ToggleBinding == PanelToggleBinding.Grip)
        {
            TryGetFloat(
                _squeezeValueAction,
                panelHandPath,
                $"{HandName(_panelSettings.PanelHand)} squeeze",
                ref _nextSqueezeReadFailureLogTimestamp,
                out XrActionStateFloat toggleGrip);
            toggleActive = toggleGrip.IsActive != 0;
            toggleValue = toggleGrip.CurrentState;
        }
        else
        {
            IntPtr toggleAction = _panelSettings.ToggleBinding == PanelToggleBinding.PrimaryFace
                ? _primaryClickAction
                : _secondaryClickAction;
            TryGetBoolean(
                toggleAction,
                panelHandPath,
                $"{HandName(_panelSettings.PanelHand)} {_panelSettings.ToggleBinding}",
                ref _nextSqueezeReadFailureLogTimestamp,
                out XrActionStateBoolean toggleButton);
            toggleActive = toggleButton.IsActive != 0;
            toggleValue = toggleButton.CurrentState != 0 ? 1f : 0f;
        }
        if (_panelToggle.Update(
                toggleActive,
                toggleValue,
                Stopwatch.GetTimestamp()))
        {
            Log(
                PanelEnabled ? "hand-panel-enabled" : "hand-panel-disabled",
                $"{_panelSettings.PanelHand} {_panelSettings.ToggleBinding} toggled the panel {(PanelEnabled ? "on" : "off")};value={toggleValue:F3}.");
        }

        bool panelPoseTracked = TryLocatePose(
            _gripPoseAction,
            panelHandPath,
            GripSpaceForHand(_panelSettings.PanelHand),
            baseSpace,
            predictedDisplayTime,
            out OpenXrControllerPose panelPose);
        bool pointerAimTracked = TryLocatePose(
            _aimPoseAction,
            pointerHandPath,
            AimSpaceForHand(_panelSettings.PointerHand),
            baseSpace,
            predictedDisplayTime,
            out OpenXrControllerPose pointerAimPose);
        TryGetFloat(
            _triggerValueAction,
            pointerHandPath,
            $"{HandName(_panelSettings.PointerHand)} trigger",
            ref _nextTriggerReadFailureLogTimestamp,
            out XrActionStateFloat pointerTrigger);
        TryGetBoolean(
            _primaryClickAction,
            pointerHandPath,
            $"{HandName(_panelSettings.PointerHand)} primary",
            ref _nextPrimaryReadFailureLogTimestamp,
            out XrActionStateBoolean pointerPrimary);
        TryGetBoolean(
            _secondaryClickAction,
            pointerHandPath,
            $"{HandName(_panelSettings.PointerHand)} secondary",
            ref _nextSecondaryReadFailureLogTimestamp,
            out XrActionStateBoolean pointerSecondary);
        VrHand locomotionHand = _trackingSettings.LocomotionHand;
        VrHand viewTurnHand = OppositeHand(locomotionHand);
        TryGetVector2f(
            _thumbstickAction,
            PathForHand(locomotionHand),
            $"{HandName(locomotionHand)} locomotion thumbstick",
            ref _nextLocomotionThumbstickReadFailureLogTimestamp,
            out XrActionStateVector2f locomotionThumbstick);
        TryGetVector2f(
            _thumbstickAction,
            PathForHand(viewTurnHand),
            $"{HandName(viewTurnHand)} view-turn thumbstick",
            ref _nextViewTurnThumbstickReadFailureLogTimestamp,
            out XrActionStateVector2f viewTurnThumbstick);
        bool locomotionActive = _trackingSettings.LocomotionEnabled &&
            locomotionThumbstick.IsActive != 0;
        bool viewTurnActive = _trackingSettings.LocomotionEnabled &&
            viewTurnThumbstick.IsActive != 0;
        bool rawPrimary = pointerPrimary.IsActive != 0 && pointerPrimary.CurrentState != 0;
        bool rawSecondary = pointerSecondary.IsActive != 0 && pointerSecondary.CurrentState != 0;
        bool pointerClick = _inputSettings.PrimaryClickButton == FaceButtonBinding.Primary
            ? rawPrimary
            : rawSecondary;
        bool pointerBack = _inputSettings.BackButton == FaceButtonBinding.Primary
            ? rawPrimary
            : rawSecondary;
        return new OpenXrControllerFrame(
            PanelEnabled,
            panelPoseTracked,
            panelPose,
            pointerAimTracked,
            pointerAimPose,
            pointerTrigger.IsActive != 0 ? pointerTrigger.CurrentState : 0f,
            pointerClick,
            pointerBack,
            0f,
            0f,
            locomotionActive,
            locomotionActive ? locomotionThumbstick.CurrentState.X : 0f,
            locomotionActive ? locomotionThumbstick.CurrentState.Y : 0f,
            viewTurnActive,
            viewTurnActive ? viewTurnThumbstick.CurrentState.X : 0f,
            viewTurnActive ? viewTurnThumbstick.CurrentState.Y : 0f);
    }

    private ulong PathForHand(VrHand hand) =>
        hand == VrHand.Left ? _leftHandPath : _rightHandPath;

    private IntPtr GripSpaceForHand(VrHand hand) =>
        hand == VrHand.Left ? LeftGripSpace : RightGripSpace;

    private IntPtr AimSpaceForHand(VrHand hand) =>
        hand == VrHand.Left ? LeftAimSpace : RightAimSpace;

    private static string HandName(VrHand hand) =>
        hand == VrHand.Left ? "left" : "right";

    private static VrHand OppositeHand(VrHand hand) =>
        hand == VrHand.Left ? VrHand.Right : VrHand.Left;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Marshal.FreeHGlobal(_activeActionSetPointer);
        DestroySpace(RightAimSpace, _destroySpace);
        DestroySpace(LeftAimSpace, _destroySpace);
        DestroySpace(RightGripSpace, _destroySpace);
        DestroySpace(LeftGripSpace, _destroySpace);
        DestroyAction(_thumbstickAction, _destroyAction);
        DestroyAction(_secondaryClickAction, _destroyAction);
        DestroyAction(_primaryClickAction, _destroyAction);
        DestroyAction(_triggerValueAction, _destroyAction);
        DestroyAction(_squeezeValueAction, _destroyAction);
        DestroyAction(_aimPoseAction, _destroyAction);
        DestroyAction(_gripPoseAction, _destroyAction);
        if (_actionSet != IntPtr.Zero)
        {
            _ = _destroyActionSet(_actionSet);
        }
    }

    private bool TryLocatePose(
        IntPtr action,
        ulong subactionPath,
        IntPtr actionSpace,
        IntPtr baseSpace,
        long predictedDisplayTime,
        out OpenXrControllerPose pose)
    {
        pose = default;
        XrActionStateGetInfo getInfo = new()
        {
            Type = XrTypeActionStateGetInfo,
            Action = action,
            SubactionPath = subactionPath
        };
        XrActionStatePose actionState = new() { Type = XrTypeActionStatePose };
        int stateResult = _getPose(_session, ref getInfo, ref actionState);
        if (stateResult != XrSuccess || actionState.IsActive == 0)
        {
            return false;
        }

        XrSpaceLocation location = new() { Type = XrTypeSpaceLocation };
        int locateResult = _locateSpace(actionSpace, baseSpace, predictedDisplayTime, ref location);
        if (locateResult != XrSuccess ||
            (location.LocationFlags & XrSpaceLocationPoseValidAndTracked) != XrSpaceLocationPoseValidAndTracked)
        {
            return false;
        }
        pose = new OpenXrControllerPose(
            location.Pose.Orientation.X,
            location.Pose.Orientation.Y,
            location.Pose.Orientation.Z,
            location.Pose.Orientation.W,
            location.Pose.Position.X,
            location.Pose.Position.Y,
            location.Pose.Position.Z);
        return true;
    }

    private bool TryGetFloat(
        IntPtr action,
        ulong subactionPath,
        string actionName,
        ref long nextFailureLogTimestamp,
        out XrActionStateFloat state)
    {
        XrActionStateGetInfo info = new()
        {
            Type = XrTypeActionStateGetInfo,
            Action = action,
            SubactionPath = subactionPath
        };
        state = new XrActionStateFloat { Type = XrTypeActionStateFloat };
        int result = _getFloat(_session, ref info, ref state);
        return HandleActionReadResult(actionName, result, ref nextFailureLogTimestamp);
    }

    private bool TryGetBoolean(
        IntPtr action,
        ulong subactionPath,
        string actionName,
        ref long nextFailureLogTimestamp,
        out XrActionStateBoolean state)
    {
        XrActionStateGetInfo info = new()
        {
            Type = XrTypeActionStateGetInfo,
            Action = action,
            SubactionPath = subactionPath
        };
        state = new XrActionStateBoolean { Type = XrTypeActionStateBoolean };
        int result = _getBoolean(_session, ref info, ref state);
        return HandleActionReadResult(actionName, result, ref nextFailureLogTimestamp);
    }

    private bool TryGetVector2f(
        IntPtr action,
        ulong subactionPath,
        string actionName,
        ref long nextFailureLogTimestamp,
        out XrActionStateVector2f state)
    {
        XrActionStateGetInfo info = new()
        {
            Type = XrTypeActionStateGetInfo,
            Action = action,
            SubactionPath = subactionPath
        };
        state = new XrActionStateVector2f { Type = XrTypeActionStateVector2f };
        int result = _getVector2f(_session, ref info, ref state);
        return HandleActionReadResult(actionName, result, ref nextFailureLogTimestamp);
    }

    private static bool HandleActionReadResult(
        string actionName,
        int result,
        ref long nextFailureLogTimestamp)
    {
        if (result == XrSuccess)
        {
            return true;
        }

        long now = Stopwatch.GetTimestamp();
        if (now >= nextFailureLogTimestamp)
        {
            nextFailureLogTimestamp = now + (Stopwatch.Frequency * 10);
            Log(
                "openxr-controller-action-read-failure",
                $"OpenXR failed to read {actionName} action: {result}; other controller actions remain active.");
        }
        return false;
    }

    private static IntPtr CreateAction(
        CreateActionDelegate createAction,
        IntPtr actionSet,
        IntPtr subactionPaths,
        string name,
        string localizedName,
        int actionType)
    {
        XrActionCreateInfo info = new()
        {
            Type = XrTypeActionCreateInfo,
            ActionName = FixedUtf8(name, 64),
            ActionType = actionType,
            CountSubactionPaths = 2,
            SubactionPaths = subactionPaths,
            LocalizedActionName = FixedUtf8(localizedName, 128)
        };
        Check(createAction(actionSet, ref info, out IntPtr action), $"create {name} action");
        return action;
    }

    private static IntPtr CreateSpace(
        CreateActionSpaceDelegate createActionSpace,
        IntPtr session,
        IntPtr action,
        ulong subactionPath)
    {
        XrActionSpaceCreateInfo info = new()
        {
            Type = XrTypeActionSpaceCreateInfo,
            Action = action,
            SubactionPath = subactionPath,
            PoseInActionSpace = IdentityPose()
        };
        Check(createActionSpace(session, ref info, out IntPtr space), "create controller action space");
        return space;
    }

    private static XrActionSuggestedBinding Bind(IntPtr action, ulong binding) =>
        new() { Action = action, Binding = binding };

    private static ulong ToPath(
        StringToPathDelegate stringToPath,
        IntPtr instance,
        string value)
    {
        Check(stringToPath(instance, value, out ulong path), $"resolve OpenXR path {value}");
        return path;
    }

    private static byte[] FixedUtf8(string value, int size)
    {
        byte[] result = new byte[size];
        int count = Encoding.UTF8.GetBytes(value, 0, value.Length, result, 0);
        if (count >= size)
        {
            throw new InvalidOperationException($"OpenXR name exceeds {size - 1} UTF-8 bytes: {value}");
        }
        result[count] = 0;
        return result;
    }

    private static XrPosef IdentityPose() => new()
    {
        Orientation = new XrQuaternionf { W = 1f }
    };

    private static T LoadExport<T>(IntPtr loader, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(loader, name));

    private static void Check(int result, string operation)
    {
        if (result != XrSuccess)
        {
            throw new InvalidOperationException($"OpenXR failed to {operation}: {result}.");
        }
    }

    private static void ValidateAbi()
    {
        ValidateSize<XrActionSetCreateInfo>(216);
        ValidateSize<XrActionCreateInfo>(224);
        ValidateSize<XrActionSuggestedBinding>(16);
        ValidateSize<XrInteractionProfileSuggestedBinding>(40);
        ValidateSize<XrSessionActionSetsAttachInfo>(32);
        ValidateSize<XrActiveActionSet>(16);
        ValidateSize<XrActionsSyncInfo>(32);
        ValidateSize<XrActionStateGetInfo>(32);
        ValidateSize<XrActionStateBoolean>(40);
        ValidateSize<XrActionStateFloat>(40);
        ValidateSize<XrActionStateVector2f>(48);
        ValidateSize<XrActionStatePose>(24);
        ValidateSize<XrActionSpaceCreateInfo>(64);
        ValidateSize<XrSpaceLocation>(56);
        ValidateSize<XrPosef>(28);
    }

    private static void ValidateSize<T>(int expected) where T : struct
    {
        int actual = Marshal.SizeOf<T>();
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"OpenXR ABI size mismatch for {typeof(T).Name}: expected {expected}, actual {actual}.");
        }
    }

    private static void DestroySpace(IntPtr space, DestroySpaceDelegate destroy)
    {
        if (space != IntPtr.Zero)
        {
            _ = destroy(space);
        }
    }

    private static void DestroyAction(IntPtr action, DestroyActionDelegate destroy)
    {
        if (action != IntPtr.Zero)
        {
            _ = destroy(action);
        }
    }

    private static void Log(string eventName, string reason, Exception? exception = null)
    {
        RuntimeProbe.Append(RuntimeProbe.GetLogPath(), new ProbeEvent
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Event = eventName,
            BootstrapVersion = RuntimeProbe.BootstrapVersion,
            ProcessId = Environment.ProcessId,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            Reason = reason,
            ErrorType = exception?.GetType().FullName,
            Error = exception?.Message
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActionSetCreateInfo
    {
        public int Type;
        public IntPtr Next;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] ActionSetName;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public byte[] LocalizedActionSetName;
        public uint Priority;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActionCreateInfo
    {
        public int Type;
        public IntPtr Next;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)] public byte[] ActionName;
        public int ActionType;
        public uint CountSubactionPaths;
        public IntPtr SubactionPaths;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public byte[] LocalizedActionName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActionSuggestedBinding
    {
        public IntPtr Action;
        public ulong Binding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrInteractionProfileSuggestedBinding
    {
        public int Type;
        public IntPtr Next;
        public ulong InteractionProfile;
        public uint CountSuggestedBindings;
        public IntPtr SuggestedBindings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSessionActionSetsAttachInfo
    {
        public int Type;
        public IntPtr Next;
        public uint CountActionSets;
        public IntPtr ActionSets;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActiveActionSet
    {
        public IntPtr ActionSet;
        public ulong SubactionPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActionsSyncInfo
    {
        public int Type;
        public IntPtr Next;
        public uint CountActiveActionSets;
        public IntPtr ActiveActionSets;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActionStateGetInfo
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Action;
        public ulong SubactionPath;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActionStateBoolean
    {
        public int Type;
        public IntPtr Next;
        public uint CurrentState;
        public uint ChangedSinceLastSync;
        public long LastChangeTime;
        public uint IsActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActionStateFloat
    {
        public int Type;
        public IntPtr Next;
        public float CurrentState;
        public uint ChangedSinceLastSync;
        public long LastChangeTime;
        public uint IsActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActionStatePose
    {
        public int Type;
        public IntPtr Next;
        public uint IsActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActionStateVector2f
    {
        public int Type;
        public IntPtr Next;
        public XrVector2f CurrentState;
        public uint ChangedSinceLastSync;
        public long LastChangeTime;
        public uint IsActive;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrActionSpaceCreateInfo
    {
        public int Type;
        public IntPtr Next;
        public IntPtr Action;
        public ulong SubactionPath;
        public XrPosef PoseInActionSpace;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrSpaceLocation
    {
        public int Type;
        public IntPtr Next;
        public ulong LocationFlags;
        public XrPosef Pose;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XrPosef
    {
        public XrQuaternionf Orientation;
        public XrVector3f Position;
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
    private struct XrVector2f
    {
        public float X;
        public float Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateActionSetDelegate(IntPtr instance, ref XrActionSetCreateInfo createInfo, out IntPtr actionSet);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroyActionSetDelegate(IntPtr actionSet);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateActionDelegate(IntPtr actionSet, ref XrActionCreateInfo createInfo, out IntPtr action);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroyActionDelegate(IntPtr action);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)]
    private delegate int StringToPathDelegate(IntPtr instance, [MarshalAs(UnmanagedType.LPUTF8Str)] string pathString, out ulong path);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SuggestBindingsDelegate(IntPtr instance, ref XrInteractionProfileSuggestedBinding suggestedBindings);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int AttachActionSetsDelegate(IntPtr session, ref XrSessionActionSetsAttachInfo attachInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CreateActionSpaceDelegate(IntPtr session, ref XrActionSpaceCreateInfo createInfo, out IntPtr space);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int DestroySpaceDelegate(IntPtr space);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int LocateSpaceDelegate(IntPtr space, IntPtr baseSpace, long time, ref XrSpaceLocation location);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SyncActionsDelegate(IntPtr session, ref XrActionsSyncInfo syncInfo);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetActionStateBooleanDelegate(IntPtr session, ref XrActionStateGetInfo getInfo, ref XrActionStateBoolean state);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetActionStateFloatDelegate(IntPtr session, ref XrActionStateGetInfo getInfo, ref XrActionStateFloat state);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetActionStateVector2fDelegate(IntPtr session, ref XrActionStateGetInfo getInfo, ref XrActionStateVector2f state);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int GetActionStatePoseDelegate(IntPtr session, ref XrActionStateGetInfo getInfo, ref XrActionStatePose state);
}
