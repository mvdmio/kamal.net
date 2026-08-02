using System.Diagnostics;
using System.Text;

namespace Kamal.Execution;

/// <summary>
/// Runs commands on the local machine (the equivalent of SSHKit's <c>run_locally</c> backend):
/// <c>sh -c</c> on Unix, <c>cmd /d /s /c</c> on Windows. Hooks run through this backend with
/// their KAMAL_* env injected.
/// </summary>
public sealed class LocalBackend : BackendBase
{
   public override string Host => "localhost";

   protected override async Task<RunResult> Run(
      string commandLine,
      string? input,
      IReadOnlyDictionary<string, string>? env,
      Action<string, string> onOutputLine,
      CancellationToken cancellationToken)
   {
      var startInfo = new ProcessStartInfo
      {
         RedirectStandardOutput = true,
         RedirectStandardError = true,
         RedirectStandardInput = input is not null,
         UseShellExecute = false,
         CreateNoWindow = true
      };

      if (OperatingSystem.IsWindows())
      {
         startInfo.FileName = "cmd.exe";
         startInfo.Arguments = $"/d /s /c \"{commandLine}\"";
      }
      else
      {
         startInfo.FileName = "/bin/sh";
         startInfo.ArgumentList.Add("-c");
         startInfo.ArgumentList.Add(commandLine);
      }

      foreach (var (key, value) in env ?? Enumerable.Empty<KeyValuePair<string, string>>())
         startInfo.Environment[key] = value;

      using var process = Process.Start(startInfo)
         ?? throw new InvalidOperationException($"Failed to start process for command: {commandLine}");

      if (input is not null)
      {
         await process.StandardInput.WriteAsync(input).ConfigureAwait(false);
         process.StandardInput.Close();
      }

      var stdout = new StringBuilder();
      var stderr = new StringBuilder();
      var stdoutTask = PumpAsync(process.StandardOutput, "stdout", stdout, onOutputLine, cancellationToken);
      var stderrTask = PumpAsync(process.StandardError, "stderr", stderr, onOutputLine, cancellationToken);

      try
      {
         await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
         try
         {
            process.Kill(entireProcessTree: true);
         }
         catch (InvalidOperationException)
         {
            // Already exited.
         }

         throw;
      }

      await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

      return new RunResult(process.ExitCode, stdout.ToString(), stderr.ToString());
   }

   public override Task Upload(string localPath, string remotePath, string? mode = null, bool recursive = false, CancellationToken cancellationToken = default)
   {
      // Parsed before anything is copied, so a bad mode leaves nothing behind at the destination.
      var uploadMode = UploadMode.ParseOptional(mode, remotePath);

      if (recursive && Directory.Exists(localPath))
      {
         var destination = Path.Combine(remotePath, Path.GetFileName(Path.TrimEndingDirectorySeparator(localPath)));
         CopyDirectory(new DirectoryInfo(localPath), destination);
         ApplyMode(destination, uploadMode);
      }
      else
      {
         Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(remotePath)) ?? ".");
         File.Copy(localPath, remotePath, overwrite: true);
         ApplyMode(remotePath, uploadMode);
      }

      return Task.CompletedTask;
   }

   public override async Task Upload(Stream local, string remotePath, string? mode = null, CancellationToken cancellationToken = default)
   {
      // Parsed before anything is copied, so a bad mode leaves nothing behind at the destination.
      var uploadMode = UploadMode.ParseOptional(mode, remotePath);

      Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(remotePath)) ?? ".");

      await using (var file = File.Create(remotePath))
         await local.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

      ApplyMode(remotePath, uploadMode);
   }

   private static async Task PumpAsync(StreamReader reader, string streamName, StringBuilder buffer, Action<string, string> onOutputLine, CancellationToken cancellationToken)
   {
      while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
      {
         buffer.Append(line).Append('\n');
         onOutputLine(streamName, line);
      }
   }

   private static void CopyDirectory(DirectoryInfo source, string destination)
   {
      Directory.CreateDirectory(destination);

      foreach (var file in source.GetFiles())
         file.CopyTo(Path.Combine(destination, file.Name), overwrite: true);

      foreach (var directory in source.GetDirectories())
         CopyDirectory(directory, Path.Combine(destination, directory.Name));
   }

   /// <summary>
   /// Applies <paramref name="mode"/> to <paramref name="path"/> and, when it is a directory,
   /// to everything beneath it — matching what the SSH backend does for a recursive upload.
   /// </summary>
   private static void ApplyMode(string path, UploadMode? mode)
   {
      if (mode is not { } uploadMode || OperatingSystem.IsWindows())
         return;

      if (Directory.Exists(path))
      {
         foreach (var entry in Directory.GetFileSystemEntries(path))
            ApplyMode(entry, uploadMode);

         // Last, so a mode without the traverse bit cannot lock us out of the tree we are still walking.
         File.SetUnixFileMode(path, uploadMode.UnixFileMode);
      }
      else if (File.Exists(path))
      {
         File.SetUnixFileMode(path, uploadMode.UnixFileMode);
      }
   }
}
