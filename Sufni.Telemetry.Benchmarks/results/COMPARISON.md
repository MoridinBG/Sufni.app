# Telemetry DSP — pre/post-SIMD benchmark comparison

Performance bracket for Workstream C (§6.1 `Filters` Savitzky-Golay → `TensorPrimitives.Dot`,
§6.2 `Strokes` per-stroke reductions, §6.3 `TelemetryStatistics.Frequency` mean-removal
sum → `TensorPrimitives.Sum`). Correctness is owned separately by the §11.1 regression-output
test; this measures performance only.

- **Baseline (pre-SIMD):** untouched DSP tree, before §3.2 and §6.1–6.3.
- **Post-SIMD:** full plan applied.
- **Same benchmark binary and dataset** feed both runs (deterministic 300k-sample dataset,
  seed `20260701`); the public APIs Workstream C touches are signature-unchanged.

**Environment (identical for both runs):** BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.1,
Apple M2 Pro (12 physical cores), .NET SDK 10.0.301 / runtime 10.0.9, Arm64 RyuJIT,
Concurrent Workstation GC, `-c Release`.

Units are normalized below (Mean in ms, Allocated in MB, 1 MB = 1024 KB); raw
BenchmarkDotNet reports live under `results/pre-simd/` and `results/post-simd/`.

## FromRecording_EndToEnd (aggregate pipeline)

| Metric    | Pre-SIMD   | Post-SIMD  | Δ        |
|-----------|-----------:|-----------:|---------:|
| Mean      | 73.602 ms  | 60.622 ms  | −17.6 %  |
| Error     | 0.580 ms   | 1.160 ms   | —        |
| StdDev    | 0.543 ms   | 1.028 ms   | —        |
| Gen0      | 2000.00    | 666.67     | −66.7 %  |
| Gen1      | 1428.57    | 666.67     | −53.3 %  |
| Gen2      | 857.14     | 666.67     | −22.2 %  |
| Allocated | 41.44 MB   | 23.04 MB   | −44.4 %  |

## SavitzkyGolay_Process (dominant SIMD target)

| Metric    | Pre-SIMD   | Post-SIMD  | Δ        |
|-----------|-----------:|-----------:|---------:|
| Mean      | 23.469 ms  | 17.681 ms  | −24.7 %  |
| Error     | 0.091 ms   | 0.136 ms   | —        |
| StdDev    | 0.085 ms   | 0.106 ms   | —        |
| Gen0      | 125.00     | 125.00     | 0 %      |
| Gen1      | 125.00     | 125.00     | 0 %      |
| Gen2      | 125.00     | 125.00     | 0 %      |
| Allocated | 2.29 MB    | 2.29 MB    | ≈0 %     |

Allocation is flat by design: the SIMD change accelerates the convolution compute; the
single result array is still allocated by `Process`.

## StrokeReductions (isolated allocation win)

| Metric    | Pre-SIMD   | Post-SIMD  | Δ        |
|-----------|-----------:|-----------:|---------:|
| Mean      | 1.515 ms   | 0.608 ms   | −59.8 %  |
| Error     | 0.030 ms   | 0.006 ms   | —        |
| StdDev    | 0.042 ms   | 0.005 ms   | —        |
| Gen0      | 1150.39    | 4.88       | −99.6 %  |
| Gen1      | —          | —          | —        |
| Gen2      | —          | —          | —        |
| Allocated | 9.19 MB    | 0.042 MB (43.34 KB) | −99.5 % |

## Verdict against §6.5 acceptance directions

- `SavitzkyGolay_Process` Mean lower — **met** (−24.7 %).
- `StrokeReductions` Allocated → ~0 B/op — **met** (−99.5 %; the four `double[]` per stroke
  are eliminated, leaving only the residual per-`Stroke` object itself).
- `FromRecording_EndToEnd` Allocated lower and Mean ≤ baseline — **met** (−44.4 % allocated,
  −17.6 % Mean).

All three benchmarks moved in the expected direction; no regression to investigate.
