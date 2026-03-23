using BenchmarkDotNet.Running;
using Proxos.Benchmarks;

// Execute em Release: dotnet run -c Release
// Para benchmark específico:  dotnet run -c Release -- --filter *Send*
BenchmarkSwitcher.FromTypes([typeof(SendBenchmark), typeof(PublishBenchmark)]).Run(args);
