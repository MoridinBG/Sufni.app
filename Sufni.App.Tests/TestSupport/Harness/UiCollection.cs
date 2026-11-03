using Xunit;

namespace Sufni.App.Tests.TestSupport.Harness;

/// <summary>
/// Marks tests that touch <c>TestApp</c>, <c>Application.Current</c>
/// resources, <c>ViewTestHelpers</c>, the headless Avalonia dispatcher, or
/// <c>DispatcherUiTimerScheduler</c>. Those surfaces share process-global state, so the
/// collection runs separately from the parallel non-UI tests.
/// </summary>
[CollectionDefinition("Ui", DisableParallelization = true)]
public class UiCollectionDefinition;
