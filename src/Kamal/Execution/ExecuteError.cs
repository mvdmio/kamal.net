namespace Kamal.Execution;

/// <summary>
/// Raised when a command fails on a host (the .NET stand-in for both
/// <c>SSHKit::Command::Failed</c> and <c>SSHKit::Runner::ExecuteError</c>).
/// The command text carried here is always the redacted rendering, so the error is safe to log.
/// </summary>
public class ExecuteError : Exception
{
   public ExecuteError(string host, string message, string? command = null, int? exitCode = null, string? stderr = null, Exception? innerException = null)
      : base(message, innerException)
   {
      Host = host;
      Command = command;
      ExitCode = exitCode;
      Stderr = stderr;
   }

   /// <summary>The host the command failed on ("localhost" for local runs).</summary>
   public string Host { get; }

   /// <summary>The redacted command line, when the failure came from a command.</summary>
   public string? Command { get; }

   /// <summary>The command's exit code, when it ran to completion.</summary>
   public int? ExitCode { get; }

   /// <summary>Captured stderr, when available.</summary>
   public string? Stderr { get; }
}

/// <summary>
/// Aggregates failures from multiple hosts after a parallel <c>on(hosts)</c> run completes
/// (port of the patched <c>SSHKit::Runner::Parallel</c> "Exceptions on N hosts" behavior).
/// </summary>
public sealed class MultipleExecuteError : Exception
{
   public MultipleExecuteError(IReadOnlyList<ExecuteError> errors)
      : base(BuildMessage(errors), errors.Count > 0 ? errors[0] : null)
   {
      Errors = errors;
   }

   public IReadOnlyList<ExecuteError> Errors { get; }

   private static string BuildMessage(IReadOnlyList<ExecuteError> errors)
   {
      var lines = new List<string> { $"Exceptions on {errors.Count} hosts:" };
      lines.AddRange(errors.Select(error => error.Message));

      return string.Join("\n", lines);
   }
}
