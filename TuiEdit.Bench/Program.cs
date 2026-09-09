using BenchmarkDotNet.Running;
using TuiEdit.Bench;

BenchmarkSwitcher.FromAssembly(typeof(CoreBenchmarks).Assembly).Run(args);
