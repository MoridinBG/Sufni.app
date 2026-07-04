namespace Sufni.App.Shared.Stores;

public abstract record StoreMutationResult<TSnapshot>
{
    private StoreMutationResult()
    {
    }

    public sealed record Saved(TSnapshot Snapshot) : StoreMutationResult<TSnapshot>;

    public sealed record Conflict(TSnapshot CurrentSnapshot) : StoreMutationResult<TSnapshot>;

    public sealed record Missing(string ErrorMessage) : StoreMutationResult<TSnapshot>;

    public sealed record Failed(string ErrorMessage) : StoreMutationResult<TSnapshot>;
}

public abstract record StoreDeleteResult<TSnapshot>
    where TSnapshot : class
{
    private StoreDeleteResult()
    {
    }

    public sealed record Deleted(TSnapshot? PreviousSnapshot = null) : StoreDeleteResult<TSnapshot>;

    public sealed record Blocked(string ErrorMessage, TSnapshot? CurrentSnapshot = null) : StoreDeleteResult<TSnapshot>;

    public sealed record Missing(string ErrorMessage) : StoreDeleteResult<TSnapshot>;

    public sealed record Failed(string ErrorMessage) : StoreDeleteResult<TSnapshot>;
}
