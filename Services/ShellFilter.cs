using System.Diagnostics;
using System.Text;

namespace TuiEdit;

/// <summary>Runs a line block through an external command (:pipe a la Helix, textfilter a la micro).</summary>
internal static class ShellFilter
{
    /// <summary>Default process bound: a stuck filter must never hang the editor.</summary>
    internal const int DefaultTimeoutMs = 30000;

    /// <summary>Splits a command line into executable + arguments (double-quote aware, quotes stripped).</summary>
    internal static (string exe, List<string> args) Split(string command)
    {
        var args = new List<string>();
        var cur = new StringBuilder();
        bool inQuotes = false, hasToken = false;
        foreach (char c in command)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
                continue;
            }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    args.Add(cur.ToString());
                    cur.Clear();
                    hasToken = false;
                }
                continue;
            }
            cur.Append(c);
            hasToken = true;
        }
        if (hasToken)
            args.Add(cur.ToString());
        if (args.Count == 0)
            throw new InvalidOperationException("Empty shell command.");
        string exe = args[0];
        args.RemoveAt(0);
        return (exe, args);
    }

    /// <summary>
    /// Runs exe with input on stdin (UTF-8), returns stdout as lines with trailing
    /// blank lines trimmed. Throws on start failure, timeout (process killed), or
    /// nonzero exit (with the first stderr line). Empty output is a valid (deleting) result.
    /// </summary>
    internal static List<string> Run(string exe, List<string> args, string input, int timeoutMs = DefaultTimeoutMs)
    {
        using var p = new Process();
        p.StartInfo.FileName = exe;
        foreach (string a in args)
            p.StartInfo.ArgumentList.Add(a);
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.RedirectStandardInput = true;
        p.StartInfo.RedirectStandardOutput = true;
        p.StartInfo.RedirectStandardError = true;
        p.StartInfo.StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        p.StartInfo.StandardOutputEncoding = Encoding.UTF8;
        p.StartInfo.StandardErrorEncoding = Encoding.UTF8;
        bool started;
        try
        {
            started = p.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException($"Cannot start shell command: {exe}", ex);
        }
        if (!started)
            throw new InvalidOperationException($"Cannot start shell command: {exe}");
        // Both pipes drain on pool threads: a synchronous ReadToEnd here would block
        // past the timeout below (a chatty or hanging child keeps stdout open).
        Task<string> stdout = Task.Run(() => p.StandardOutput.ReadToEnd());
        Task<string> stderrTask = Task.Run(() => p.StandardError.ReadToEnd());
        p.StandardInput.Write(input);
        p.StandardInput.Close();
        if (!p.WaitForExit(timeoutMs))
        {
            try
            {
                p.Kill(entireProcessTree: true);
            }
            catch
            {
            }
            throw new InvalidOperationException($"Shell command timed out after {timeoutMs}ms: {exe}");
        }
        string output = stdout.Result; // exited: EOF arrived, no blocking
        string stderr = "";
        try
        {
            stderr = stderrTask.Result;
        }
        catch
        {
        }
        if (p.ExitCode != 0)
        {
            string detail = FirstLine(stderr);
            throw new InvalidOperationException(string.IsNullOrEmpty(detail)
                ? $"Shell command exited with code {p.ExitCode}: {exe}"
                : $"Shell command exited with code {p.ExitCode}: {detail}");
        }
        return SplitLines(output);
    }

    /// <summary>Splits output into lines (any line break), trimming trailing blank lines commands echo.</summary>
    internal static List<string> SplitLines(string output)
    {
        var lines = new List<string>(output.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n'));
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    private static string FirstLine(string s)
    {
        int i = s.IndexOf('\n');
        string first = (i < 0 ? s : s[..i]).Trim();
        return first.Length > 160 ? first[..160] : first;
    }
}
