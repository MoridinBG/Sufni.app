```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.1 (25F80) [Darwin 25.5.0]
Apple M2 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.9 (10.0.9, 10.0.926.27113), Arm64 RyuJIT armv8.0-a


```
| Method                 | Mean      | Error     | StdDev    | Gen0      | Gen1      | Gen2     | Allocated |
|----------------------- |----------:|----------:|----------:|----------:|----------:|---------:|----------:|
| FromRecording_EndToEnd | 73.602 ms | 0.5803 ms | 0.5428 ms | 2000.0000 | 1428.5714 | 857.1429 |  41.44 MB |
| SavitzkyGolay_Process  | 23.469 ms | 0.0909 ms | 0.0850 ms |  125.0000 |  125.0000 | 125.0000 |   2.29 MB |
| StrokeReductions       |  1.515 ms | 0.0296 ms | 0.0416 ms | 1150.3906 |         - |        - |   9.19 MB |
