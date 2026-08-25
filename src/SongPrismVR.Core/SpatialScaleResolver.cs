namespace SongPrismVR.Core;

public readonly struct SpatialScaleMultipliers
{
    public SpatialScaleMultipliers(
        float eyeOffsetMultiplier,
        float headTranslationMultiplier,
        float locomotionMultiplier)
    {
        EyeOffsetMultiplier = eyeOffsetMultiplier;
        HeadTranslationMultiplier = headTranslationMultiplier;
        LocomotionMultiplier = locomotionMultiplier;
    }

    public float EyeOffsetMultiplier { get; }

    public float HeadTranslationMultiplier { get; }

    public float LocomotionMultiplier { get; }
}

public static class SpatialScaleResolver
{
    public static SpatialScaleMultipliers Resolve(VrSpatialScaleProfile profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }
        float automatic = 1f / profile.PerceivedCharacterScale;
        return new SpatialScaleMultipliers(
            Resolve(profile.EyeOffsetMode, profile.EyeOffsetMultiplier, automatic),
            Resolve(profile.HeadTranslationMode, profile.HeadTranslationMultiplier, automatic),
            Resolve(profile.LocomotionMode, profile.LocomotionMultiplier, automatic));
    }

    public static float ResolveHeadTranslationScale(
        float worldEyeOffsetScale,
        SpatialScaleMultipliers multipliers) =>
        worldEyeOffsetScale * multipliers.HeadTranslationMultiplier;

    private static float Resolve(SpatialScaleMode mode, float manual, float automatic) =>
        mode == SpatialScaleMode.Auto ? automatic : manual;
}
