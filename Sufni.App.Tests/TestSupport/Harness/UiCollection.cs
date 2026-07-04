using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Sufni.App.Tests.TestSupport.Harness;

/// <summary>
/// Marks tests that touch <c>TestApp</c>, <c>Application.Current</c>
/// resources, <c>ViewTestHelpers</c>, the headless Avalonia dispatcher, or
/// <c>PeriodicUiTimer</c>. Those surfaces share process-global state, so the
/// assembly explicitly disables test parallelization above.
/// </summary>
[CollectionDefinition("Ui", DisableParallelization = true)]
public class UiCollectionDefinition;
