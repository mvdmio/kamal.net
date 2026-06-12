using System.Diagnostics;

namespace Kamal.Secrets;

/// <summary>
/// Small shared helper for running a local command line through the OS shell and capturing its output.
/// This is the C# stand-in for Ruby's backticks, which always go through the shell.
/// On Unix the command runs via <c>/bin/sh -c</c>; on Windows via <c>cmd.exe /d /s /c</c>.
/// </summary>
public static class ShellRunner
{
   /// <summary>
   /// Runs <paramref name="commandLine"/> through the shell and returns exit code, stdout and stderr.
   /// Never throws on a non-zero exit code (like Ruby backticks: callers inspect <c>$?</c>).
   /// </summary>
   public static ShellResult Run(string commandLine)
   {
      var psi = new ProcessStartInfo
      {
         RedirectStandardOutput = true,
         RedirectStandardError = true,
         UseShellExecute = false
      };

      if (OperatingSystem.IsWindows())
      {
         psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
         psi.Arguments = $"/d /s /c \"{commandLine}\"";
      }
      else
      {
         psi.FileName = "/bin/sh";
         psi.ArgumentList.Add("-c");
         psi.ArgumentList.Add(commandLine);
      }

      using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start shell for command: {commandLine}");
      var stdout = process.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      process.WaitForExit();

      return new ShellResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
   }

   /// <summary>
   /// Runs <paramref name="commandLine"/> and returns stdout, raising on a non-zero exit code with stderr in the message.
   /// </summary>
   public static string RunAndCapture(string commandLine)
   {
      var result = Run(commandLine);

      if (!result.Success)
         throw new InvalidOperationException($"Command '{commandLine}' failed with exit code {result.ExitCode}: {result.Stderr.Trim()}");

      return result.Stdout;
   }
}
