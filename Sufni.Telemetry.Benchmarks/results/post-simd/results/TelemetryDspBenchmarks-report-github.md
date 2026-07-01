```

BenchmarkDotNet v0.15.8, macOS Tahoe 26.5.1 (25F80) [Darwin 25.5.0]
Apple M2 Pro, 1 CPU, 12 logical and 12 physical cores
.NET SDK 10.0.301
  [Host]     : .NET 10.0.9 (10.0.9, 10.0.926.27113), Arm64 RyuJIT armv8.0-a
  DefaultJob : .NET 10.0.9 (10.0.9, 10.0.926.27113), Arm64 RyuJIT armv8.0-a


```
| Method                 | Mean        | Error       | StdDev      | Gen0     | Gen1     | Gen2     | Allocated   |
|----------------------- |------------:|------------:|------------:|---------:|---------:|---------:|------------:|
| FromRecording_EndToEnd | 60,621.7 μs | 1,159.52 μs | 1,027.88 μs | 666.6667 | 666.6667 | 666.6667 | 23594.39 KB |
| SavitzkyGolay_Process  | 17,680.6 μs |   135.80 μs |   106.02 μs | 125.0000 | 125.0000 | 125.0000 |  2343.85 KB |
| StrokeReductions       |    608.4 μs |     6.06 μs |     4.73 μs |   4.8828 |        - |        - |    43.34 KB |
