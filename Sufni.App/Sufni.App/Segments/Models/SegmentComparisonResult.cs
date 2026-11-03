using Sufni.App.Shared.Models;
using Sufni.App.Segments.Coordinators;

namespace Sufni.App.Segments.Models;

public abstract record SegmentComparisonPageResult
{
    private SegmentComparisonPageResult() { }
    public sealed record Loaded(SegmentComparisonPage Page) : SegmentComparisonPageResult;
    public sealed record Failed(SegmentComparisonFailureReason Reason, string? Details) : SegmentComparisonPageResult;
}

public abstract record SegmentComparisonDetailResult
{
    private SegmentComparisonDetailResult() { }
    public sealed record Loaded(SegmentComparisonDetail Detail) : SegmentComparisonDetailResult;
    public sealed record Unavailable : SegmentComparisonDetailResult;
    public sealed record Failed(SegmentComparisonFailureReason Reason, string? Details) : SegmentComparisonDetailResult;
}

public abstract record SegmentAnalysisComparisonResult
{
    private SegmentAnalysisComparisonResult() { }
    public sealed record Built(SegmentAnalysisComparison Comparison) : SegmentAnalysisComparisonResult;
    public sealed record Failed(SegmentComparisonFailureReason Reason, string? Details) : SegmentAnalysisComparisonResult;
}
