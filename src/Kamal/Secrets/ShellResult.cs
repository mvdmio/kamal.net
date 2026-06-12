namespace Kamal.Secrets;

/// <summary>
/// The result of running a local shell command, mirroring what Ruby exposes via backticks plus <c>$?</c>.
/// </summary>
public sealed record ShellResult(int ExitCode, string Stdout, string Stderr)
{
   /// <summary>Equivalent of Ruby's <c>$?.success?</c>.</summary>
   public bool Success => ExitCode == 0;
}
