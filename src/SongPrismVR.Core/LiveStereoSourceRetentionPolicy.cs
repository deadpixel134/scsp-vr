namespace SongPrismVR.Core;

public static class LiveStereoSourceRetentionPolicy
{
    public static bool ShouldRetain(
        bool isConcreteLiveScene,
        bool isSameLiveScene,
        bool hasApprovedEstablishedSource,
        bool sourceObjectAlive) =>
        isConcreteLiveScene &&
        isSameLiveScene &&
        hasApprovedEstablishedSource &&
        sourceObjectAlive;

    public static bool IsSameSource(
        long establishedSourceToken,
        long discoveredSourceToken) =>
        establishedSourceToken != 0 &&
        establishedSourceToken == discoveredSourceToken;
}
