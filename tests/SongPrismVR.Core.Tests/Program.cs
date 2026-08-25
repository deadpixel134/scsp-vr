using SongPrismVR.Core;
using System.Text.Json;

var tests = new (string Name, Action Run)[]
{
    ("Transition takes priority", TransitionTakesPriority),
    ("Video takes priority over 3D", VideoTakesPriority),
    ("Unapproved live uses safe panel", UnapprovedLiveUsesPanel),
    ("Approved live enables immersive", ApprovedLiveEnablesImmersive),
    ("UI-only scene uses panel", UiOnlyUsesPanel),
    ("Orientation waits and rebinds", OrientationWaitsAndRebinds),
    ("Render dimensions override stale Unity orientation", DimensionsOverrideStaleUnityOrientation),
    ("Orientation timeout falls back", OrientationTimeoutFallsBack),
    ("Failed rebind falls back", FailedRebindFallsBack),
    ("Grip toggle requires release edge", GripToggleRequiresReleaseEdge),
    ("Grip toggle rejects shallow and debounced presses", GripToggleRejectsShallowAndDebouncedPresses),
    ("Grip toggle supports configured initial state", GripToggleSupportsInitialState),
    ("Analog click latches before the press threshold", AnalogClickLatchesBeforePress),
    ("Analog click cancels shallow presses", AnalogClickCancelsShallowPress),
    ("Stereo startup waits for frames and Present", StereoStartupWaitsForFramesAndPresent),
    ("Stereo startup reset requires a fresh generation", StereoStartupResetRequiresFreshGeneration),
    ("Black stereo frames retry before timeout", BlackStereoFramesRetryBeforeTimeout),
    ("Black stereo frame policy resets per generation", BlackStereoFramePolicyResetsPerGeneration),
    ("6DoF origin preserves configured eye separation", SixDofOriginPreservesEyeSeparation),
    ("6DoF maps physical translation into Unity space", SixDofMapsPhysicalTranslation),
    ("6DoF maps relative head rotation into Unity space", SixDofMapsRelativeRotation),
    ("6DoF can preserve XR tracking-space orientation across first stereo activation", SixDofPreservesTrackingSpaceOrientation),
    ("6DoF keeps eye positions in the preserved XR tracking-space basis", SixDofPreservesTrackingSpacePositionBasis),
    ("6DoF separates physical roll from yaw at a tilted origin", SixDofSeparatesPhysicalRoll),
    ("6DoF reset captures a fresh scene origin", SixDofResetCapturesFreshOrigin),
    ("Locomotion moves in the full 3D view direction", LocomotionMovesInViewDirection),
    ("Locomotion follows upward and downward view pitch", LocomotionFollowsViewPitch),
    ("Locomotion applies radial deadzone and frame clamp", LocomotionAppliesDeadzoneAndFrameClamp),
    ("Locomotion reset clears the scene offset", LocomotionResetClearsSceneOffset),
    ("View turn changes movement direction", ViewTurnChangesMovementDirection),
    ("View turn keeps world yaw and pitch cardinal", ViewTurnKeepsWorldAxesCardinal),
    ("World view composition removes scene camera roll", WorldViewCompositionRemovesSceneRoll),
    ("Snap turn advances once per stick deflection", SnapTurnAdvancesOncePerDeflection),
    ("View turn reset clears artificial rotation", ViewTurnResetClearsRotation),
    ("VR settings preserve product defaults", VrSettingsPreserveProductDefaults),
    ("Live camera sync setting preserves legacy inverse mapping", LiveCameraSyncSettingPreservesLegacyInverseMapping),
    ("VR settings allow 2x eye render scale", VrSettingsAllowTwoTimesEyeRenderScale),
    ("VR settings reject unsupported VFX modes", VrSettingsRejectUnsupportedVfxModes),
    ("VR settings repair legacy manual VFX mode", VrSettingsRepairLegacyManualVfxMode),
    ("VR settings load swapped movement hand", VrSettingsLoadSwappedMovementHand),
    ("VR settings repair invalid roles and ranges", VrSettingsRepairInvalidValues),
    ("VR settings reject unsupported schema", VrSettingsRejectUnsupportedSchema),
    ("Spatial defaults preserve baseline", SpatialDefaultsPreserveBaseline),
    ("Spatial auto inverts character size", SpatialAutoInvertsCharacterSize),
    ("Spatial manual values override independently", SpatialManualOverridesIndependently),
    ("Spatial head translation shares the eye world scale", SpatialHeadTranslationSharesEyeWorldScale),
    ("Spatial validation repairs invalid values", SpatialValidationRepairsInvalidValues),
    ("COM pointer guard rejects low and misaligned values", ComPointerGuardRejectsInvalidValues)
};

var failed = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"Executed {tests.Length} tests; failures: {failed}");
return failed == 0 ? 0 : 1;

static void ComPointerGuardRejectsInvalidValues()
{
    False(ComPointerGuard.IsPlausible(IntPtr.Zero));
    False(ComPointerGuard.IsPlausible(new IntPtr(0x1682)));
    False(ComPointerGuard.IsPlausible(new IntPtr(0x10003)));
    True(ComPointerGuard.IsPlausible(new IntPtr(0x10000)));
}

static void SpatialDefaultsPreserveBaseline()
{
    SpatialScaleMultipliers result = SpatialScaleResolver.Resolve(VrSettings.CreateApprovedDefaults().Spatial.Live);
    Equal(1f, result.EyeOffsetMultiplier);
    Equal(1f, result.HeadTranslationMultiplier);
    Equal(1f, result.LocomotionMultiplier);
}

static void SpatialAutoInvertsCharacterSize()
{
    SpatialScaleMultipliers result = SpatialScaleResolver.Resolve(new VrSpatialScaleProfile { PerceivedCharacterScale = 1.5f });
    Near(2f / 3f, result.EyeOffsetMultiplier);
    Near(2f / 3f, result.HeadTranslationMultiplier);
    Near(2f / 3f, result.LocomotionMultiplier);
}

static void SpatialManualOverridesIndependently()
{
    SpatialScaleMultipliers result = SpatialScaleResolver.Resolve(new VrSpatialScaleProfile
    {
        PerceivedCharacterScale = 2f,
        EyeOffsetMode = SpatialScaleMode.Manual,
        EyeOffsetMultiplier = 1.25f,
        LocomotionMode = SpatialScaleMode.Manual,
        LocomotionMultiplier = 0.5f
    });
    Equal(1.25f, result.EyeOffsetMultiplier);
    Equal(0.5f, result.HeadTranslationMultiplier);
    Equal(0.5f, result.LocomotionMultiplier);
}

static void SpatialValidationRepairsInvalidValues()
{
    VrSettings settings = VrSettings.CreateApprovedDefaults();
    settings.Spatial.Live.PerceivedCharacterScale = 5f;
    settings.Spatial.NonLive.EyeOffsetMultiplier = float.NaN;
    VrSettingsValidationResult result = VrSettingsValidator.Validate(settings);
    True(result.UsedFallback);
    Equal(1f, result.Settings.Spatial.Live.PerceivedCharacterScale);
    Equal(1f, result.Settings.Spatial.NonLive.EyeOffsetMultiplier);
}

static RenderObservation StableObservation()
{
    return new RenderObservation
    {
        AreRenderTargetsStable = true,
        StableFrameCount = 5,
        HasValidWorldCamera = true,
        IsUrpCameraStackValid = true,
        WorldCameraCount = 1
    };
}

static void GripToggleRequiresReleaseEdge()
{
    var toggle = new GripToggleLatch(0.72f, 0.25f, 10);
    Equal(false, toggle.Enabled);
    Equal(true, toggle.Update(true, 0.80f, 100));
    Equal(true, toggle.Enabled);
    Equal(false, toggle.Update(true, 0.95f, 120));
    Equal(false, toggle.Update(true, 0.50f, 130));
    Equal(false, toggle.Update(true, 0.20f, 140));
    Equal(true, toggle.Update(true, 0.80f, 150));
    Equal(false, toggle.Enabled);
}

static void GripToggleRejectsShallowAndDebouncedPresses()
{
    var toggle = new GripToggleLatch(0.72f, 0.25f, 100);
    Equal(false, toggle.Update(true, 0.60f, 100));
    Equal(false, toggle.Enabled);
    Equal(true, toggle.Update(true, 0.80f, 110));
    Equal(false, toggle.Update(true, 0.10f, 120));
    Equal(false, toggle.Update(true, 0.80f, 130));
    Equal(false, toggle.Update(true, 0.10f, 140));
    Equal(true, toggle.Update(true, 0.80f, 220));
    Equal(false, toggle.Enabled);
}

static void GripToggleSupportsInitialState()
{
    var toggle = new GripToggleLatch(0.72f, 0.25f, 10, initialEnabled: true);
    True(toggle.Enabled);
    True(toggle.Update(active: true, value: 0.80f, timestamp: 0));
    False(toggle.Enabled);
}

static void AnalogClickLatchesBeforePress()
{
    var latch = new AnalogPressLatch(0.15f, 0.72f, 0.10f, 0.25f);
    Equal(AnalogPressTransition.None, latch.Update(true, 0.10f));
    Equal(AnalogPressTransition.Armed, latch.Update(true, 0.20f));
    True(latch.IsArmed);
    Equal(AnalogPressTransition.None, latch.Update(true, 0.60f));
    Equal(AnalogPressTransition.Pressed, latch.Update(true, 0.80f));
    True(latch.IsPressed);
    Equal(AnalogPressTransition.None, latch.Update(true, 0.50f));
    Equal(AnalogPressTransition.Released, latch.Update(true, 0.20f));
    False(latch.IsPressed);
}

static void AnalogClickCancelsShallowPress()
{
    var latch = new AnalogPressLatch(0.15f, 0.72f, 0.10f, 0.25f);
    Equal(AnalogPressTransition.Armed, latch.Update(true, 0.30f));
    Equal(AnalogPressTransition.None, latch.Update(true, 0.05f));
    False(latch.IsArmed);
    Equal(AnalogPressTransition.Armed, latch.Update(true, 0.20f));
    False(latch.Cancel());
    False(latch.IsArmed);
}

static void StereoStartupWaitsForFramesAndPresent()
{
    var gate = new StereoStartupGate(requiredStableFrames: 2);
    gate.Arm(frameCount: 100, presentSerial: 50);

    False(gate.IsReady(frameCount: 101, presentSerial: 51));
    False(gate.IsReady(frameCount: 102, presentSerial: 50));
    True(gate.IsReady(frameCount: 102, presentSerial: 51));
}

static void StereoStartupResetRequiresFreshGeneration()
{
    var gate = new StereoStartupGate(requiredStableFrames: 2);
    gate.Arm(frameCount: 100, presentSerial: 50);
    True(gate.IsReady(frameCount: 102, presentSerial: 51));

    gate.Reset();
    False(gate.IsReady(frameCount: 200, presentSerial: 100));

    gate.Arm(frameCount: 200, presentSerial: 100);
    False(gate.IsReady(frameCount: 201, presentSerial: 101));
    True(gate.IsReady(frameCount: 202, presentSerial: 101));
}

static void BlackStereoFramesRetryBeforeTimeout()
{
    var policy = new StereoBlackFrameRetryPolicy(
        maximumAttempts: 3,
        timeoutMilliseconds: 1_000);
    Equal(StereoBlackFrameDecision.Retry, policy.ObserveBlack(100));
    Equal(StereoBlackFrameDecision.Retry, policy.ObserveBlack(200));
    Equal(StereoBlackFrameDecision.TimedOut, policy.ObserveBlack(300));
    Equal(3, policy.AttemptCount);
}

static void BlackStereoFramePolicyResetsPerGeneration()
{
    var policy = new StereoBlackFrameRetryPolicy(
        maximumAttempts: 3,
        timeoutMilliseconds: 100);
    Equal(StereoBlackFrameDecision.Retry, policy.ObserveBlack(1_000));
    Equal(StereoBlackFrameDecision.TimedOut, policy.ObserveBlack(1_101));
    policy.Reset();
    Equal(0, policy.AttemptCount);
    Equal(StereoBlackFrameDecision.Retry, policy.ObserveBlack(2_000));
}

static void SixDofOriginPreservesEyeSeparation()
{
    var mapper = new SixDofPoseMapper();
    True(mapper.TryMap(
        StereoPose(-0.03f, 0f, 0f, 0.03f, 0f, 0f),
        headTranslationScale: 1f,
        eyeOffsetScale: 0.5f,
        out UnityStereoPose mapped));

    Near(-0.015f, mapped.Left.LocalPosition.X);
    Near(0.015f, mapped.Right.LocalPosition.X);
    Near(0f, mapped.Left.LocalPosition.Y);
    Near(0f, mapped.Left.LocalPosition.Z);
    Near(1f, mapped.Left.LocalRotation.W);
}

static void SixDofMapsPhysicalTranslation()
{
    var mapper = new SixDofPoseMapper();
    True(mapper.TryMap(
        StereoPose(-0.03f, 0f, 0f, 0.03f, 0f, 0f),
        1f,
        1f,
        out _));
    True(mapper.TryMap(
        StereoPose(0.17f, 0.10f, -0.50f, 0.23f, 0.10f, -0.50f),
        1f,
        1f,
        out UnityStereoPose mapped));

    Near(0.17f, mapped.Left.LocalPosition.X);
    Near(0.23f, mapped.Right.LocalPosition.X);
    Near(0.10f, mapped.Left.LocalPosition.Y);
    Near(0.50f, mapped.Left.LocalPosition.Z);
}

static void SixDofMapsRelativeRotation()
{
    var mapper = new SixDofPoseMapper();
    True(mapper.TryMap(
        StereoPose(-0.03f, 0f, 0f, 0.03f, 0f, 0f),
        1f,
        1f,
        out _));
    float halfAngle = MathF.PI * 0.25f;
    TrackingQuaternion yaw = new(0f, MathF.Sin(halfAngle), 0f, MathF.Cos(halfAngle));
    True(mapper.TryMap(
        StereoPose(-0.03f, 0f, 0f, 0.03f, 0f, 0f, yaw),
        1f,
        1f,
        out UnityStereoPose mapped));

    Near(-MathF.Sin(halfAngle), mapped.Left.LocalRotation.Y);
    Near(MathF.Cos(halfAngle), mapped.Left.LocalRotation.W);
}

static void SixDofResetCapturesFreshOrigin()
{
    var mapper = new SixDofPoseMapper();
    True(mapper.TryMap(
        StereoPose(-0.03f, 0f, 0f, 0.03f, 0f, 0f),
        1f,
        1f,
        out _));
    mapper.Reset();
    True(mapper.TryMap(
        StereoPose(1.97f, 1f, -3f, 2.03f, 1f, -3f),
        1f,
        1f,
        out UnityStereoPose mapped));

    Near(-0.03f, mapped.Left.LocalPosition.X);
    Near(0f, mapped.Left.LocalPosition.Y);
    Near(0f, mapped.Left.LocalPosition.Z);
}

static void SixDofSeparatesPhysicalRoll()
{
    var mapper = new SixDofPoseMapper();
    TrackingQuaternion originUnity = AxisAngle(0f, 0f, 1f, 20f);
    True(mapper.TryMap(
        StereoPose(
            -0.03f,
            0f,
            0f,
            0.03f,
            0f,
            0f,
            UnityToTracking(originUnity)),
        1f,
        1f,
        out _));

    TrackingQuaternion yawWithSameRoll = MultiplyTestQuaternion(
        AxisAngle(0f, 1f, 0f, 45f),
        originUnity);
    True(mapper.TryMap(
        StereoPose(
            -0.03f,
            0f,
            0f,
            0.03f,
            0f,
            0f,
            UnityToTracking(yawWithSameRoll)),
        1f,
        1f,
        out UnityStereoPose yawMapped));
    Near(MathF.PI / 4f, yawMapped.Left.YawDeltaRadians);
    Near(0f, yawMapped.Left.RollDeltaRadians);

    TrackingQuaternion yawWithAddedRoll = MultiplyTestQuaternion(
        AxisAngle(0f, 1f, 0f, 45f),
        AxisAngle(0f, 0f, 1f, 35f));
    True(mapper.TryMap(
        StereoPose(
            -0.03f,
            0f,
            0f,
            0.03f,
            0f,
            0f,
            UnityToTracking(yawWithAddedRoll)),
        1f,
        1f,
        out UnityStereoPose rollMapped));
    Near(MathF.PI / 12f, rollMapped.Left.RollDeltaRadians);
}

static void LocomotionMovesInViewDirection()
{
    var locomotion = new VrLocomotionIntegrator();
    TrackingQuaternion identity = new(0f, 0f, 0f, 1f);
    True(locomotion.Update(0f, 1f, identity, 0.10f, 2f));
    Near(0f, locomotion.Offset.X);
    Near(0f, locomotion.Offset.Y);
    Near(0.20f, locomotion.Offset.Z);

    float halfAngle = MathF.PI * 0.25f;
    TrackingQuaternion rightFacing = new(
        0f,
        MathF.Sin(halfAngle),
        0f,
        MathF.Cos(halfAngle));
    True(locomotion.Update(0f, 1f, rightFacing, 0.10f, 2f));
    Near(0.20f, locomotion.Offset.X);
    Near(0.20f, locomotion.Offset.Z);
}

static void LocomotionFollowsViewPitch()
{
    float halfAngle = MathF.PI * 0.25f;
    TrackingQuaternion lookingUp = new(
        -MathF.Sin(halfAngle),
        0f,
        0f,
        MathF.Cos(halfAngle));
    var upward = new VrLocomotionIntegrator();
    True(upward.Update(0f, 1f, lookingUp, 0.10f, 2f));
    Near(0.20f, upward.Offset.Y);
    Near(0f, upward.Offset.Z);

    TrackingQuaternion lookingDown = new(
        MathF.Sin(halfAngle),
        0f,
        0f,
        MathF.Cos(halfAngle));
    var downward = new VrLocomotionIntegrator();
    True(downward.Update(0f, 1f, lookingDown, 0.10f, 2f));
    Near(-0.20f, downward.Offset.Y);
    Near(0f, downward.Offset.Z);
}

static void LocomotionAppliesDeadzoneAndFrameClamp()
{
    var locomotion = new VrLocomotionIntegrator();
    TrackingQuaternion identity = new(0f, 0f, 0f, 1f);
    True(locomotion.Update(0.10f, 0.10f, identity, 1f, 5f));
    Near(0f, locomotion.Offset.X);
    Near(0f, locomotion.Offset.Z);

    True(locomotion.Update(1f, 0f, identity, 1f, 5f));
    Near(0.50f, locomotion.Offset.X);
    Near(0f, locomotion.Offset.Z);
}

static void LocomotionResetClearsSceneOffset()
{
    var locomotion = new VrLocomotionIntegrator();
    True(locomotion.Update(
        1f,
        0f,
        new TrackingQuaternion(0f, 0f, 0f, 1f),
        0.10f,
        1f));
    locomotion.Reset();
    Near(0f, locomotion.Offset.X);
    Near(0f, locomotion.Offset.Y);
    Near(0f, locomotion.Offset.Z);
}

static void ViewTurnChangesMovementDirection()
{
    var turn = new VrViewTurnIntegrator();
    for (int index = 0; index < 5; index++)
    {
        True(turn.Update(
            1f,
            0f,
            0.10f,
            VrViewTurnMode.Smooth,
            180f,
            15,
            0f));
    }
    var locomotion = new VrLocomotionIntegrator();
    True(locomotion.Update(0f, 1f, turn.Rotation, 0.10f, 2f));
    Near(0.20f, locomotion.Offset.X);
    Near(0f, locomotion.Offset.Z);
}

static void ViewTurnKeepsWorldAxesCardinal()
{
    var turn = new VrViewTurnIntegrator();
    True(turn.Update(
        0f,
        1f,
        0.10f,
        VrViewTurnMode.Smooth,
        300f,
        15,
        0f));
    // The small off-axis Y component must not add pitch during a yaw input.
    True(turn.Update(
        1f,
        0.30f,
        0.10f,
        VrViewTurnMode.Smooth,
        900f,
        15,
        0f));

    var locomotion = new VrLocomotionIntegrator();
    True(locomotion.Update(0f, 1f, turn.Rotation, 0.10f, 2f));
    Near(0.20f * MathF.Cos(MathF.PI / 6f), locomotion.Offset.X);
    Near(0.10f, locomotion.Offset.Y);
    Near(0f, locomotion.Offset.Z);
}

static void SnapTurnAdvancesOncePerDeflection()
{
    var turn = new VrViewTurnIntegrator();
    True(turn.Update(1f, 0f, 0.10f, VrViewTurnMode.Snap, 90f, 15));
    True(turn.Update(1f, 0f, 0.10f, VrViewTurnMode.Snap, 90f, 15));

    var first = new VrLocomotionIntegrator();
    True(first.Update(0f, 1f, turn.Rotation, 0.10f, 2f));
    Near(0.20f * MathF.Sin(MathF.PI / 12f), first.Offset.X);
    Near(0.20f * MathF.Cos(MathF.PI / 12f), first.Offset.Z);

    True(turn.Update(0f, 0f, 0.10f, VrViewTurnMode.Snap, 90f, 15));
    True(turn.Update(1f, 0f, 0.10f, VrViewTurnMode.Snap, 90f, 15));
    var second = new VrLocomotionIntegrator();
    True(second.Update(0f, 1f, turn.Rotation, 0.10f, 2f));
    Near(0.10f, second.Offset.X);
    Near(0.20f * MathF.Cos(MathF.PI / 6f), second.Offset.Z);
}

static void WorldViewCompositionRemovesSceneRoll()
{
    TrackingQuaternion baseRotation = MultiplyTestQuaternion(
        AxisAngle(0f, 1f, 0f, 40f),
        MultiplyTestQuaternion(
            AxisAngle(1f, 0f, 0f, -20f),
            AxisAngle(0f, 0f, 1f, 25f)));
    var turn = new VrViewTurnIntegrator();
    True(turn.TryCreateWorldNavigationRotation(baseRotation, out TrackingQuaternion leveled));

    TrackingVector3 originalForward = RotateTestVector(
        baseRotation,
        new TrackingVector3(0f, 0f, 1f));
    TrackingVector3 leveledForward = RotateTestVector(
        leveled,
        new TrackingVector3(0f, 0f, 1f));
    TrackingVector3 leveledRight = RotateTestVector(
        leveled,
        new TrackingVector3(1f, 0f, 0f));
    Near(originalForward.X, leveledForward.X);
    Near(originalForward.Y, leveledForward.Y);
    Near(originalForward.Z, leveledForward.Z);
    Near(0f, leveledRight.Y);

    True(turn.Update(1f, 0.1f, 0.10f, VrViewTurnMode.Snap, 90f, 30));
    True(turn.TryCreateWorldNavigationRotation(baseRotation, out TrackingQuaternion turned));
    TrackingVector3 turnedRight = RotateTestVector(
        turned,
        new TrackingVector3(1f, 0f, 0f));
    Near(0f, turnedRight.Y);

    True(turn.TryCreateWorldViewRotation(
        baseRotation,
        physicalYawDeltaRadians: MathF.PI / 6f,
        physicalPitchDeltaRadians: 0f,
        physicalRollDeltaRadians: MathF.PI / 12f,
        out TrackingQuaternion physicalRollView));
    TrackingVector3 physicalRight = RotateTestVector(
        physicalRollView,
        new TrackingVector3(1f, 0f, 0f));
    TrackingVector3 physicalUp = RotateTestVector(
        physicalRollView,
        new TrackingVector3(0f, 1f, 0f));
    Near(
        MathF.PI / 12f,
        MathF.Atan2(physicalRight.Y, physicalUp.Y));
}

static void ViewTurnResetClearsRotation()
{
    var turn = new VrViewTurnIntegrator();
    True(turn.Update(
        1f,
        1f,
        0.10f,
        VrViewTurnMode.Smooth,
        90f,
        15,
        0f));
    turn.Reset();
    Near(0f, turn.Rotation.X);
    Near(0f, turn.Rotation.Y);
    Near(0f, turn.Rotation.Z);
    Near(1f, turn.Rotation.W);
}

static TrackingStereoPose StereoPose(
    float leftX,
    float leftY,
    float leftZ,
    float rightX,
    float rightY,
    float rightZ,
    TrackingQuaternion? orientation = null)
{
    TrackingQuaternion rotation = orientation ?? new TrackingQuaternion(0f, 0f, 0f, 1f);
    return new TrackingStereoPose(
        new TrackingEyePose(new TrackingVector3(leftX, leftY, leftZ), rotation),
        new TrackingEyePose(new TrackingVector3(rightX, rightY, rightZ), rotation));
}

static TrackingQuaternion AxisAngle(float x, float y, float z, float degrees)
{
    float halfRadians = degrees * MathF.PI / 360f;
    float sine = MathF.Sin(halfRadians);
    return new TrackingQuaternion(
        x * sine,
        y * sine,
        z * sine,
        MathF.Cos(halfRadians));
}

static TrackingQuaternion UnityToTracking(TrackingQuaternion value) =>
    new(-value.X, -value.Y, value.Z, value.W);

static TrackingQuaternion MultiplyTestQuaternion(
    TrackingQuaternion left,
    TrackingQuaternion right) => new(
        (left.W * right.X) + (left.X * right.W) +
            (left.Y * right.Z) - (left.Z * right.Y),
        (left.W * right.Y) - (left.X * right.Z) +
            (left.Y * right.W) + (left.Z * right.X),
        (left.W * right.Z) + (left.X * right.Y) -
            (left.Y * right.X) + (left.Z * right.W),
        (left.W * right.W) - (left.X * right.X) -
            (left.Y * right.Y) - (left.Z * right.Z));

static TrackingVector3 RotateTestVector(
    TrackingQuaternion rotation,
    TrackingVector3 value)
{
    TrackingVector3 q = new(rotation.X, rotation.Y, rotation.Z);
    TrackingVector3 t = ScaleTestVector(CrossTestVector(q, value), 2f);
    return AddTestVector(
        value,
        AddTestVector(
            ScaleTestVector(t, rotation.W),
            CrossTestVector(q, t)));
}

static TrackingVector3 CrossTestVector(
    TrackingVector3 left,
    TrackingVector3 right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));

static TrackingVector3 ScaleTestVector(TrackingVector3 value, float scale) =>
    new(value.X * scale, value.Y * scale, value.Z * scale);

static TrackingVector3 AddTestVector(
    TrackingVector3 left,
    TrackingVector3 right) =>
    new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

static void VrSettingsPreserveProductDefaults()
{
    VrSettingsValidationResult result = VrSettingsValidator.Validate(
        VrSettings.CreateApprovedDefaults());
    Equal(false, result.UsedFallback);
    Equal(VrHand.Left, result.Settings.Panel.PanelHand);
    Equal(VrHand.Right, result.Settings.Panel.PointerHand);
    Equal(true, result.Settings.Panel.ViewerFacing);
    Equal(0.10f, result.Settings.Panel.OffsetY);
    Equal(0.65f, result.Settings.Render.EyeRenderScale);
    Equal(true, result.Settings.Tracking.LiveSixDofEnabled);
    Equal(false, VrLivePositionPolicy.SynchronizeToGameCamera(
        result.Settings.Tracking.LiveSixDofEnabled));
    Equal(true, result.Settings.Tracking.LocomotionEnabled);
    Equal(VrHand.Right, result.Settings.Tracking.LocomotionHand);
    Equal(1.95f, result.Settings.Tracking.LocomotionSpeed);
    Equal(VrViewTurnMode.Snap, result.Settings.Tracking.ViewTurnMode);
    Equal(90f, result.Settings.Tracking.ViewTurnSpeed);
    Equal(30, result.Settings.Tracking.ViewSnapAngleDegrees);
    Equal(VrVisualEffectModes.AllOff, result.Settings.Render.VisualEffectMode);
    Equal(true, result.Settings.Render.ManualVisualEffects.PostProcessingEnabled);
    Equal(1.40f, result.Settings.Render.ManualVisualEffects.VlBloomIntensityScale);
    Equal(1, result.Settings.Render.ManualVisualEffects.VlBloomDiffusion);
    Equal(false, result.Settings.Render.ManualVisualEffects.VlDepthOfFieldEnabled);
    Equal(false, result.Settings.Render.ManualVisualEffects.VlTextureBlurEnabled);
}

static void SpatialHeadTranslationSharesEyeWorldScale()
{
    SpatialScaleMultipliers multipliers = SpatialScaleResolver.Resolve(
        new VrSpatialScaleProfile { PerceivedCharacterScale = 1.25f });
    Near(
        0.22f,
        SpatialScaleResolver.ResolveHeadTranslationScale(0.275f, multipliers));
}

static void SixDofPreservesTrackingSpaceOrientation()
{
    var mapper = new SixDofPoseMapper();
    TrackingQuaternion tiltedUnity = AxisAngle(0f, 0f, 1f, 20f);
    True(mapper.TryMap(
        StereoPose(
            -0.03f,
            0f,
            0f,
            0.03f,
            0f,
            0f,
            UnityToTracking(tiltedUnity)),
        1f,
        1f,
        out UnityStereoPose tilted,
        preserveTrackingSpaceOrientation: true));
    Near(MathF.PI / 9f, tilted.Left.RollDeltaRadians);

    True(mapper.TryMap(
        StereoPose(-0.03f, 0f, 0f, 0.03f, 0f, 0f),
        1f,
        1f,
        out UnityStereoPose upright,
        preserveTrackingSpaceOrientation: true));
    Near(0f, upright.Left.YawDeltaRadians);
    Near(0f, upright.Left.PitchDeltaRadians);
    Near(0f, upright.Left.RollDeltaRadians);
}

static void SixDofPreservesTrackingSpacePositionBasis()
{
    var mapper = new SixDofPoseMapper();
    const float eyeOffset = 0.03f;
    const float rollDegrees = 20f;
    float rollRadians = rollDegrees * MathF.PI / 180f;
    float eyeX = eyeOffset * MathF.Cos(rollRadians);
    float eyeY = eyeOffset * MathF.Sin(rollRadians);
    TrackingQuaternion tiltedUnity = AxisAngle(0f, 0f, 1f, rollDegrees);

    True(mapper.TryMap(
        StereoPose(
            -eyeX,
            -eyeY,
            0f,
            eyeX,
            eyeY,
            0f,
            UnityToTracking(tiltedUnity)),
        1f,
        1f,
        out UnityStereoPose mapped,
        preserveTrackingSpaceOrientation: true));

    Near(-eyeX, mapped.Left.LocalPosition.X);
    Near(-eyeY, mapped.Left.LocalPosition.Y);
    Near(eyeX, mapped.Right.LocalPosition.X);
    Near(eyeY, mapped.Right.LocalPosition.Y);
    Near(MathF.PI / 9f, mapped.Left.RollDeltaRadians);
}

static void LiveCameraSyncSettingPreservesLegacyInverseMapping()
{
    Equal(false, VrLivePositionPolicy.SynchronizeToGameCamera(
        liveSixDofEnabled: true));
    Equal(true, VrLivePositionPolicy.SynchronizeToGameCamera(
        liveSixDofEnabled: false));
    Equal(false, VrLivePositionPolicy.EnableLiveSixDof(
        synchronizeToGameCamera: true));
    Equal(true, VrLivePositionPolicy.EnableLiveSixDof(
        synchronizeToGameCamera: false));

    string json = JsonSerializer.Serialize(new VrTrackingSettings());
    Equal(true, json.Contains("LiveSixDofEnabled", StringComparison.Ordinal));
    Equal(false, json.Contains("Synchronize", StringComparison.Ordinal));
}

static void VrSettingsRejectUnsupportedVfxModes()
{
    VrSettings settings = VrSettings.CreateApprovedDefaults();
    settings.Tracking.LiveSixDofEnabled = true;
    settings.Render.VisualEffectMode = VrVisualEffectModes.Manual;
    settings.Render.ManualVisualEffects.PostProcessingEnabled = true;
    settings.Render.ManualVisualEffects.VlBloomEnabled = false;
    settings.Render.ManualVisualEffects.VlBloomIntensityScale = 0.85f;
    settings.Render.ManualVisualEffects.VlBloomDiffusion = 4;
    settings.Render.ManualVisualEffects.VlDepthOfFieldEnabled = true;
    settings.Render.ManualVisualEffects.VlTextureBlurEnabled = true;
    settings.Render.ManualVisualEffects.VlStarStreakEnabled = false;
    settings.Render.ManualVisualEffects.VlFlareEnabled = false;

    VrSettingsValidationResult result = VrSettingsValidator.Validate(settings);

    Equal(true, result.UsedFallback);
    Equal(VrVisualEffectModes.AllOff, result.Settings.Render.VisualEffectMode);
    Equal(true, result.Settings.Tracking.LiveSixDofEnabled);
    Equal(true, result.Settings.Render.ManualVisualEffects.PostProcessingEnabled);
    Equal(false, result.Settings.Render.ManualVisualEffects.VlBloomEnabled);
    Equal(0.85f, result.Settings.Render.ManualVisualEffects.VlBloomIntensityScale);
    Equal(4, result.Settings.Render.ManualVisualEffects.VlBloomDiffusion);
    Equal(true, result.Settings.Render.ManualVisualEffects.VlDepthOfFieldEnabled);
    Equal(true, result.Settings.Render.ManualVisualEffects.VlTextureBlurEnabled);
    Equal(false, result.Settings.Render.ManualVisualEffects.VlStarStreakEnabled);
    Equal(false, result.Settings.Render.ManualVisualEffects.VlFlareEnabled);
}

static void VrSettingsAllowTwoTimesEyeRenderScale()
{
    VrSettings settings = VrSettings.CreateApprovedDefaults();
    settings.Render.EyeRenderScale = 2.00f;

    VrSettingsValidationResult result = VrSettingsValidator.Validate(settings);

    Equal(false, result.UsedFallback);
    Equal(2.00f, result.Settings.Render.EyeRenderScale);
}

static void VrSettingsRepairLegacyManualVfxMode()
{
    const string json = """
        {
          "schemaVersion": 1,
          "render": {
            "visualEffectMode": "manual"
          }
        }
        """;
    VrSettings? parsed = JsonSerializer.Deserialize<VrSettings>(
        json,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    VrSettingsValidationResult result = VrSettingsValidator.Validate(parsed);

    Equal(true, result.UsedFallback);
    Equal(VrVisualEffectModes.AllOff, result.Settings.Render.VisualEffectMode);
    Equal(true, result.Settings.Tracking.LiveSixDofEnabled);
    Equal(true, result.Settings.Tracking.LocomotionEnabled);
    Equal(VrHand.Right, result.Settings.Tracking.LocomotionHand);
    Equal(1.95f, result.Settings.Tracking.LocomotionSpeed);
    Equal(VrViewTurnMode.Snap, result.Settings.Tracking.ViewTurnMode);
    Equal(90f, result.Settings.Tracking.ViewTurnSpeed);
    Equal(30, result.Settings.Tracking.ViewSnapAngleDegrees);
    Equal(true, result.Settings.Render.ManualVisualEffects.PostProcessingEnabled);
    Equal(1.40f, result.Settings.Render.ManualVisualEffects.VlBloomIntensityScale);
    Equal(1, result.Settings.Render.ManualVisualEffects.VlBloomDiffusion);
}

static void VrSettingsLoadSwappedMovementHand()
{
    const string json = """
        {
          "schemaVersion": 1,
          "tracking": {
            "locomotionEnabled": true,
            "locomotionHand": "left",
            "locomotionSpeed": 2.25,
            "viewTurnMode": "smooth",
            "viewTurnSpeed": 120.0,
            "viewSnapAngleDegrees": 45
          }
        }
        """;
    VrSettings? parsed = JsonSerializer.Deserialize<VrSettings>(
        json,
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        });

    VrSettingsValidationResult result = VrSettingsValidator.Validate(parsed);

    False(result.UsedFallback);
    Equal(VrHand.Left, result.Settings.Tracking.LocomotionHand);
    Equal(2.25f, result.Settings.Tracking.LocomotionSpeed);
    Equal(VrViewTurnMode.Smooth, result.Settings.Tracking.ViewTurnMode);
    Equal(120f, result.Settings.Tracking.ViewTurnSpeed);
    Equal(45, result.Settings.Tracking.ViewSnapAngleDegrees);
}

static void VrSettingsRepairInvalidValues()
{
    VrSettings settings = VrSettings.CreateApprovedDefaults();
    settings.Panel.PointerHand = VrHand.Left;
    settings.Panel.MaximumWidth = 4f;
    settings.Render.EyeRenderScale = 2.01f;
    settings.Render.ManualVisualEffects.VlBloomIntensityScale = 4f;
    settings.Render.ManualVisualEffects.VlBloomDiffusion = 0;
    settings.Tracking.LocomotionHand = (VrHand)99;
    settings.Tracking.LocomotionSpeed = 9f;
    settings.Tracking.ViewTurnMode = (VrViewTurnMode)99;
    settings.Tracking.ViewTurnSpeed = 500f;
    settings.Tracking.ViewSnapAngleDegrees = 20;
    settings.Input.BackButton = FaceButtonBinding.Primary;
    VrSettingsValidationResult result = VrSettingsValidator.Validate(settings);
    Equal(true, result.UsedFallback);
    Equal(VrHand.Left, result.Settings.Panel.PanelHand);
    Equal(VrHand.Right, result.Settings.Panel.PointerHand);
    Equal(0.42f, result.Settings.Panel.MaximumWidth);
    Equal(0.75f, result.Settings.Render.EyeRenderScale);
    Equal(1.40f, result.Settings.Render.ManualVisualEffects.VlBloomIntensityScale);
    Equal(1, result.Settings.Render.ManualVisualEffects.VlBloomDiffusion);
    Equal(VrHand.Right, result.Settings.Tracking.LocomotionHand);
    Equal(1.95f, result.Settings.Tracking.LocomotionSpeed);
    Equal(VrViewTurnMode.Snap, result.Settings.Tracking.ViewTurnMode);
    Equal(90f, result.Settings.Tracking.ViewTurnSpeed);
    Equal(30, result.Settings.Tracking.ViewSnapAngleDegrees);
    Equal(FaceButtonBinding.Primary, result.Settings.Input.PrimaryClickButton);
    Equal(FaceButtonBinding.Secondary, result.Settings.Input.BackButton);
}

static void VrSettingsRejectUnsupportedSchema()
{
    VrSettings settings = VrSettings.CreateApprovedDefaults();
    settings.SchemaVersion = 99;
    settings.Panel.PanelHand = VrHand.Right;
    VrSettingsValidationResult result = VrSettingsValidator.Validate(settings);
    Equal(true, result.UsedFallback);
    Equal(VrSettings.CurrentSchemaVersion, result.Settings.SchemaVersion);
    Equal(VrHand.Left, result.Settings.Panel.PanelHand);
}

static void TransitionTakesPriority()
{
    var result = new SceneClassifier().Classify(new RenderObservation
    {
        IsOrientationChanging = true,
        AreRenderTargetsStable = true,
        StableFrameCount = 5,
        HasLiveCameraMarker = true,
        HasValidWorldCamera = true,
        IsUrpCameraStackValid = true,
        IsProfileApproved = true,
        WorldCameraCount = 1
    });

    Equal(PresentationContext.Transition, result.Context);
    Equal(RecommendedPresentationMode.FrozenPanel, result.Mode);
}

static void VideoTakesPriority()
{
    var observation = StableObservation();
    observation = new RenderObservation
    {
        AreRenderTargetsStable = observation.AreRenderTargetsStable,
        StableFrameCount = observation.StableFrameCount,
        HasValidWorldCamera = observation.HasValidWorldCamera,
        IsUrpCameraStackValid = observation.IsUrpCameraStackValid,
        WorldCameraCount = observation.WorldCameraCount,
        HasFullScreenVideo = true,
        HasLiveCameraMarker = true,
        IsProfileApproved = true
    };

    var result = new SceneClassifier().Classify(observation);
    Equal(PresentationContext.Video2D, result.Context);
    Equal(RecommendedPresentationMode.SafePanel, result.Mode);
}

static void UnapprovedLiveUsesPanel()
{
    var result = new SceneClassifier().Classify(new RenderObservation
    {
        AreRenderTargetsStable = true,
        StableFrameCount = 5,
        HasLiveCameraMarker = true,
        HasValidWorldCamera = true,
        IsUrpCameraStackValid = true,
        WorldCameraCount = 2
    });

    Equal(PresentationContext.LiveCandidate, result.Context);
    Equal(RecommendedPresentationMode.SafePanel, result.Mode);
}

static void ApprovedLiveEnablesImmersive()
{
    var result = new SceneClassifier().Classify(new RenderObservation
    {
        AreRenderTargetsStable = true,
        StableFrameCount = 5,
        HasLiveCameraMarker = true,
        HasValidWorldCamera = true,
        IsUrpCameraStackValid = true,
        IsProfileApproved = true,
        WorldCameraCount = 2
    });

    Equal(PresentationContext.LiveCandidate, result.Context);
    Equal(RecommendedPresentationMode.Immersive, result.Mode);
}

static void UiOnlyUsesPanel()
{
    var result = new SceneClassifier().Classify(new RenderObservation
    {
        AreRenderTargetsStable = true,
        StableFrameCount = 5,
        UiCanvasDominates = true,
        WorldCameraCount = 0
    });

    Equal(PresentationContext.Menu2D, result.Context);
    Equal(RecommendedPresentationMode.SafePanel, result.Mode);
}

static void OrientationWaitsAndRebinds()
{
    var stabilizer = new OrientationStabilizer(requiredStableFrames: 3, timeoutMilliseconds: 2000);
    var initial = stabilizer.Observe(Sample(1080, 1920, "portrait", 0));
    Equal(OrientationTransitionState.StablePortrait, initial.State);

    var first = stabilizer.Observe(Sample(1920, 1080, "landscape", 10, explicitChange: true));
    Equal(OrientationTransitionState.WaitingForStableTargets, first.State);
    True(first.FreezeFrame);
    True(first.BlockPointerInput);

    var second = stabilizer.Observe(Sample(1920, 1080, "landscape", 20));
    False(second.RequestRebind);

    var third = stabilizer.Observe(Sample(1920, 1080, "landscape", 30));
    Equal(OrientationTransitionState.Rebinding, third.State);
    True(third.RequestRebind);

    var completed = stabilizer.CompleteRebind(success: true);
    Equal(OrientationTransitionState.StableLandscape, completed.State);
    Equal(OrientationKind.Landscape, completed.Orientation);
    False(completed.FreezeFrame);
}

static void OrientationTimeoutFallsBack()
{
    var stabilizer = new OrientationStabilizer(requiredStableFrames: 3, timeoutMilliseconds: 100);
    stabilizer.Observe(Sample(1080, 1920, "portrait", 0));
    stabilizer.Observe(Sample(0, 0, "none", 10, explicitChange: true, valid: false));
    var timedOut = stabilizer.Observe(Sample(0, 0, "none", 110, valid: false));

    Equal(OrientationTransitionState.SafePanel, timedOut.State);
    True(timedOut.TimedOut);
    False(timedOut.BlockPointerInput);
}

static void DimensionsOverrideStaleUnityOrientation()
{
    var stabilizer = new OrientationStabilizer(requiredStableFrames: 2, timeoutMilliseconds: 2000);
    var portrait = Sample(1080, 1920, "portrait", 0);
    portrait.ReportedScreenOrientation = 1;
    Equal(OrientationTransitionState.StablePortrait, stabilizer.Observe(portrait).State);

    var firstLandscape = Sample(1920, 1080, "landscape", 10);
    firstLandscape.ReportedScreenOrientation = 1;
    Equal(
        OrientationTransitionState.WaitingForStableTargets,
        stabilizer.Observe(firstLandscape).State);

    var stableLandscape = Sample(1920, 1080, "landscape", 20);
    stableLandscape.ReportedScreenOrientation = 1;
    var pending = stabilizer.Observe(stableLandscape);
    True(pending.RequestRebind);
    Equal(
        OrientationTransitionState.StableLandscape,
        stabilizer.CompleteRebind(success: true).State);
}

static void FailedRebindFallsBack()
{
    var stabilizer = new OrientationStabilizer(requiredStableFrames: 1, timeoutMilliseconds: 1000);
    stabilizer.Observe(Sample(1080, 1920, "portrait", 0));
    var pending = stabilizer.Observe(Sample(1920, 1080, "landscape", 10, explicitChange: true));
    if (!pending.RequestRebind)
    {
        pending = stabilizer.Observe(Sample(1920, 1080, "landscape", 20));
    }

    Equal(OrientationTransitionState.Rebinding, pending.State);
    var failed = stabilizer.CompleteRebind(success: false);
    Equal(OrientationTransitionState.SafePanel, failed.State);
    True(failed.FreezeFrame);
}

static OrientationSample Sample(
    int width,
    int height,
    string signature,
    long now,
    bool explicitChange = false,
    bool valid = true)
{
    return new OrientationSample
    {
        Width = width,
        Height = height,
        TargetSignature = signature,
        NowMilliseconds = now,
        ExplicitChangeSignal = explicitChange,
        RenderTargetsValid = valid
    };
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void True(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected true, got false.");
    }
}

static void False(bool value)
{
    if (value)
    {
        throw new InvalidOperationException("Expected false, got true.");
    }
}

static void Near(float expected, float actual, float tolerance = 0.0001f)
{
    if (MathF.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
