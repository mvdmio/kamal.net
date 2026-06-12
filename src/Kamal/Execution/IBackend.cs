namespace Kamal.Execution;

/// <summary>
/// Per-host command execution interface: the .NET equivalent of an SSHKit backend
/// (<c>execute</c> / <c>capture</c> / <c>test</c> / <c>upload!</c> inside an <c>on(hosts)</c> block).
/// Commands are the <c>object[]</c> token arrays produced by the <c>Kamal.Commands</c> builders;
/// <see cref="Kamal.Utils.Sensitive"/> tokens execute with their real values but are redacted in logs.
/// </summary>
public interface IBackend
{
   /// <summary>The host this backend runs on ("localhost" for local backends).</summary>
   string Host { get; }

   /// <summary>
   /// Runs a command; throws <see cref="ExecuteError"/> on a non-zero exit unless
   /// <paramref name="raiseOnNonZeroExit"/> is false. <paramref name="verbosity"/> is the level
   /// the command itself is logged at (SSHKit's <c>verbosity:</c> option).
   /// <paramref name="input"/> is fed to the process's stdin (the minimal stand-in for SSHKit's
   /// interaction handlers). <paramref name="env"/> injects environment variables for the run.
   /// </summary>
   Task Execute(
      object?[] command,
      Verbosity verbosity = Verbosity.Info,
      bool raiseOnNonZeroExit = true,
      string? input = null,
      IReadOnlyDictionary<string, string>? env = null,
      CancellationToken cancellationToken = default);

   /// <summary>Runs a command and returns its stripped stdout (SSHKit <c>capture</c>, default verbosity DEBUG).</summary>
   Task<string> Capture(
      object?[] command,
      Verbosity verbosity = Verbosity.Debug,
      bool raiseOnNonZeroExit = true,
      string? input = null,
      IReadOnlyDictionary<string, string>? env = null,
      CancellationToken cancellationToken = default);

   /// <summary>Port of the <c>capture_with_info</c> extension: capture, logged at INFO.</summary>
   Task<string> CaptureWithInfo(object?[] command, bool raiseOnNonZeroExit = true, CancellationToken cancellationToken = default);

   /// <summary>Port of the <c>capture_with_debug</c> extension: capture, logged at DEBUG.</summary>
   Task<string> CaptureWithDebug(object?[] command, bool raiseOnNonZeroExit = true, CancellationToken cancellationToken = default);

   /// <summary>Port of the <c>capture_with_pretty_json</c> extension: capture and pretty-print the JSON output.</summary>
   Task<string> CaptureWithPrettyJson(object?[] command, bool raiseOnNonZeroExit = true, CancellationToken cancellationToken = default);

   /// <summary>Runs a command and returns whether it exited zero (SSHKit <c>test</c>; never throws on non-zero exit).</summary>
   Task<bool> Test(object?[] command, CancellationToken cancellationToken = default);

   /// <summary>Uploads a local file (or directory, with <paramref name="recursive"/>) to the remote path (SSHKit <c>upload!</c>).</summary>
   Task Upload(string localPath, string remotePath, string? mode = null, bool recursive = false, CancellationToken cancellationToken = default);

   /// <summary>Uploads in-memory content to the remote path (SSHKit <c>upload! StringIO.new(...), path</c>).</summary>
   Task Upload(Stream local, string remotePath, string? mode = null, CancellationToken cancellationToken = default);
}
