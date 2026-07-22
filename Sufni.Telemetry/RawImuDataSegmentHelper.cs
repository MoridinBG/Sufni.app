namespace Sufni.Telemetry;

public static class RawImuDataSegmentHelper
{
    public static void FinalizeCanonicalSegments(
        RawImuData imuData,
        IEnumerable<RawStreamGap> streamGaps)
    {
        imuData.Records.Clear();

        var imuGapExists = streamGaps.Any(gap => gap.StreamKind == SstV5ProtocolConstants.StreamImu);
        var segmentsByLocation = imuData.Segments
            .GroupBy(segment => segment.LocationId)
            .ToDictionary(group => group.Key, group => group.OrderBy(segment => segment.FirstIndex).ToArray());
        var aligned = imuData.ActiveLocations.Count > 0 &&
            imuData.ActiveLocations.All(location => segmentsByLocation.TryGetValue(location, out var segments) && segments.Length == 1) &&
            !imuGapExists;

        if (aligned)
        {
            var firstLocation = imuData.ActiveLocations[0];
            var firstSegment = segmentsByLocation[firstLocation][0];
            aligned = imuData.ActiveLocations.All(location =>
            {
                var segment = segmentsByLocation[location][0];
                return segment.FirstIndex == firstSegment.FirstIndex &&
                    segment.FirstMonotonicDeltaUs == firstSegment.FirstMonotonicDeltaUs &&
                    segment.Records.Length == firstSegment.Records.Length;
            });
        }

        imuData.HasGaps = !aligned ||
            imuData.Segments.CountBy(segment => segment.LocationId).Any(kvp => kvp.Value > 1);
    }
}
