using TuiEdit;
using Xunit;

namespace TuiEdit.Tests;

/// <summary>SCRATCH: параллельный шторм ForFile — гонки кэша/флага. Удалить после диагностики.</summary>
public sealed class GitStressScratch
{
    [Fact]
    public async Task ParallelForFileStorm()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tui_gstorm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var tasks = new List<Task>();
            for (int t = 0; t < 10; t++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int i = 0; i < 100; i++)
                        GitStatus.ForFile(Path.Combine(dir, "f.txt"));
                }));
            }
            await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(60));
            await Task.Delay(3000); // дать фону завершиться
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
