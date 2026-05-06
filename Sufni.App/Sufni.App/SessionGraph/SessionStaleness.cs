namespace Sufni.App.SessionGraph;

/// <summary>
/// Classification of whether recorded-session processed data matches its
/// current raw source and processing inputs. Each state also declares whether
/// rebuilding derived data is currently possible.
/// </summary>
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

    public sealed record MissingRawSource(bool ProcessedStateStale = false) : SessionStaleness
    {
        public override bool IsStale => ProcessedStateStale;
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
