using System.Diagnostics;
using System.Text;

namespace AncosSantiye.Actions;

/// <summary>What git said. <see cref="Output"/> is stdout and stderr together, cut to a readable length.</summary>
public sealed record GitResult(int ExitCode, string Stdout, string Output)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>
/// Runs git with an argument list, never through a shell. Pathspecs are literal, prompts are off,
/// and every call has a time limit.
/// </summary>
public static class Git
{
    private const int MaxOutput = 4000;

    public static async Task<GitResult> Run(string repo, TimeSpan timeout, params string[] args)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repo,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        // A file named ":(glob)*" stays a file name, and a push never waits for a password on a closed stdin.
        start.Environment["GIT_LITERAL_PATHSPECS"] = "1";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";

        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        // The pipes are inside the time limit too: a hook that leaves a child holding them must not hang us.
        using var cts = new CancellationTokenSource(timeout);
        string output, errors;
        try
        {
            await process.WaitForExitAsync(cts.Token);
            output = await stdout.WaitAsync(cts.Token);
            errors = await stderr.WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return new GitResult(-1, "", $"git {args[0]} took longer than {timeout.TotalSeconds:0} s and was stopped.");
        }

        return new GitResult(process.ExitCode, output, Trim(output + errors));
    }

    private static string Trim(string text)
    {
        text = text.Trim();
        return text.Length <= MaxOutput ? text : text[..MaxOutput] + "\n(cut)";
    }
}
