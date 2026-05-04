namespace Sufni.App.SessionGraph;

public abstract record SessionStaleness
{
    public abstract bool IsStale { get; }
    public abstract bool CanRecompute { get; }

    public sealed record Current : SessionStaleness
    {
        public override bool IsStale => false;
        public override bool CanRecompute => false;
    }

    public sealed record MissingProcessedData : SessionStaleness
    {
        public override bool IsStale => true;
        public override bool CanRecompute => true;
    }

    public sealed record MissingRawSource : SessionStaleness
    {
        public override bool IsStale => true;
        public override bool CanRecompute => false;
    }

    public sealed record ProcessingVersionChanged(int Persisted, int CurrentVersion) : SessionStaleness
    {
        public override bool IsStale => true;
        public override bool CanRecompute => true;
    }

    public sealed record DependencyHashChanged : SessionStaleness
    {
        public override bool IsStale => true;
        public override bool CanRecompute => true;
    }

    public sealed record MissingDependencies(bool SetupMissing, bool BikeMissing) : SessionStaleness
    {
        public override bool IsStale => true;
        public override bool CanRecompute => false;
    }

    public sealed record UnknownLegacyFingerprint : SessionStaleness
    {
        public override bool IsStale => true;
        public override bool CanRecompute => true;
    }
}
