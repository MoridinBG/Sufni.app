namespace Sufni.App.Tests.TestSupport.Harness;

/// <summary>
/// Serializes the UI tier: tests that touch <c>TestApp</c>,
/// <c>Application.Current</c> resources, <c>ViewTestHelpers</c>, the headless
/// Avalonia dispatcher, or <c>PeriodicUiTimer</c> share process-global state
/// and must not run in parallel with anything else. Persistence and pure
/// unit tiers run in xunit's default parallel collections.
/// </summary>
[CollectionDefinition("Ui", DisableParallelization = true)]
public class UiCollectionDefinition;
