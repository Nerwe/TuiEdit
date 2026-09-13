# Performance baseline

Local reference numbers for `TuiEdit.Bench` (8 core benchmarks).
CI runs the same suite on every push (`bench` job, artifacts only — no fail gate yet:
shared runners are too noisy; compare manually against this file).

- Date: 2026-09-09
- Machine: Windows 11, X64 RyuJIT AVX-512, .NET 10.0.11, BenchmarkDotNet 0.15.2
- Command: `dotnet run -c Release --project TuiEdit.Bench -- --filter '*'`

| Method        | Mean             | Allocated |
|-------------- |-----------------:|----------:|
| FindNextPlain |    50,472.066 ns |         - |
| FindNextRegex |   318,776.208 ns |  770368 B |
| HighlightRow  |         8.602 ns |         - |
| HighlightFull | 2,258,460.208 ns | 1979208 B |
| SortFiles     |   258,425.310 ns |    8056 B |
| TabSlice      |       241.872 ns |     584 B |
| WrapSegments  |       423.566 ns |         - |
| TypeChars     | 1,236,795.196 ns | 2424296 B |

Known allocation sinks for future passes: regex full scan (770 KB),
full highlight pass (2 MB), sequential typing with undo history (2.4 MB).

## Frame composition (120x30, 12000-line C# buffer, `*Frame*` filter)

- Date: 2026-09-13
- Machine: Windows 11, X64 RyuJIT AVX-512, .NET 10.0.12, BenchmarkDotNet 0.15.2

| Method              | Mean    | Allocated |
|-------------------- |--------:|----------:|
| FrameCursorTop      | 38.7 us |   19.9 KB |
| FrameCursorDeep     | 44.0 us |   21.2 KB |
| FrameCursorDeepWrap | 42.6 us |   23.2 KB |

History: before the `EnsureVisible` fix, `FrameCursorDeep` cost 233 us and
922 KB (O(cursor) scans from row 0 plus a `SortedSet` enumerator per row);
the relative walk from `_top` flattened it. Scroll steps repaint the whole
viewport (~197 diff ops), so `Screen.FlushAnsi` batches them into a single
`Console.Write` with inline CUP addressing instead of ~600 syscalls.
