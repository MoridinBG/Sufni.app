namespace Sufni.Telemetry;

public static class RawImuDataSegmentHelper
{
    public static void PopulateDenseRecordsFromAlignedSegments(
        RawImuData imuData,
        IEnumerable<RawStreamGap> streamGaps)
    {
        imuData.Records.Clear();

        var imuGapExists = streamGaps.Any(gap => gap.StreamKind == SstV5ProtocolConstants.StreamImu);
        var segmentsByLocation = imuData.Segments
            .GroupBy(segment => segment.LocationId)
            .ToDictionary(group => group.Key, group => group.OrderBy(segment => segment.FirstIndex).ToArray());
        var dense = imuData.ActiveLocations.Count > 0 &&
            imuData.ActiveLocations.All(location => segmentsByLocation.TryGetValue(location, out var segments) && segments.Length == 1) &&
            !imuGapExists;

        if (dense)
        {
            var firstLocation = imuData.ActiveLocations[0];
            var firstSegment = segmentsByLocation[firstLocation][0];
            dense = imuData.ActiveLocations.All(location =>
            {
                var segment = segmentsByLocation[location][0];
                return segment.FirstIndex == firstSegment.FirstIndex &&
                    segment.FirstMonotonicDeltaUs == firstSegment.FirstMonotonicDeltaUs &&
                    segment.Records.Length == firstSegment.Records.Length;
            });

            if (dense)
            {
                for (var sampleIndex = 0; sampleIndex < firstSegment.Records.Length; sampleIndex++)
                {
                    foreach (var location in imuData.ActiveLocations)
                    {
                        imuData.Records.Add(segmentsByLocation[location][0].Records[sampleIndex]);
                    }
                }
            }
        }

        imuData.HasGaps = !dense ||
            imuData.Segments.CountBy(segment => segment.LocationId).Any(kvp => kvp.Value > 1);
    }
}
