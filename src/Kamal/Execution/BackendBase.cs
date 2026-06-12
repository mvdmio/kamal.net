using System.Diagnostics;
using System.Text.Json;
using Kamal.Output;

namespace Kamal.Execution;

/// <summary>The raw result of running a command on a backend.</summary>
public readonly record struct RunResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Shared backend behavior: command rendering, logging through <see cref="KamalOutput"/>,
/// error raising, and the capture/test conveniences. Concrete backends implement
/// <see cref="Run"/> (and the uploads).
/// </summary>
public abstract class BackendBase : IBackend
{
   public abstract string Host { get; }

   public async Task Execute(
      object?[] command,
      Verbosity verbosity = Verbosity.Info,
      bool raiseOnNonZeroExit = true,
      string? input = null,
      IReadOnlyDictionary<string, string>? env = null,
      CancellationToken cancellationToken = default)
   {
      await RunLogged(command, verbosity, raiseOnNonZeroExit, input, env, cancellationToken).ConfigureAwait(false);
   }

   public async Task<string> Capture(
      object?[] command,
      Verbosity verbosity = Verbosity.Debug,
      bool raiseOnNonZeroExit = true,
      string? input = null,
      IReadOnlyDictionary<string, string>? env = null,
      CancellationToken cancellationToken = default)
   {
      var result = await RunLogged(command, verbosity, raiseOnNonZeroExit, input, env, cancellationToken).ConfigureAwait(false);

      // SSHKit's capture strips the output by default.
      return result.Stdout.Trim();
   }

   public Task<string> CaptureWithInfo(object?[] command, bool raiseOnNonZeroExit = true, CancellationToken cancellationToken = default)
   {
      return Capture(command, Verbosity.Info, raiseOnNonZeroExit, cancellationToken: cancellationToken);
   }

   public Task<string> CaptureWithDebug(object?[] command, bool raiseOnNonZeroExit = true, CancellationToken cancellationToken = default)
   {
      return Capture(command, Verbosity.Debug, raiseOnNonZeroExit, cancellationToken: cancellationToken);
   }

   public async Task<string> CaptureWithPrettyJson(object?[] command, bool raiseOnNonZeroExit = true, CancellationToken cancellationToken = default)
   {
      var output = await Capture(command, raiseOnNonZeroExit: raiseOnNonZeroExit, cancellationToken: cancellationToken).ConfigureAwait(false);

      // Ruby: JSON.pretty_generate(JSON.parse(capture(...)))
      using var document = JsonDocument.Parse(output);

      return JsonSerializer.Serialize(document.RootElement, PrettyJsonOptions);
   }

   public async Task<bool> Test(object?[] command, CancellationToken cancellationToken = default)
   {
      var result = await RunLogged(command, Verbosity.Debug, raiseOnNonZeroExit: false, input: null, env: null, cancellationToken).ConfigureAwait(false);

      return result.ExitCode == 0;
   }

   public abstract Task Upload(string localPath, string remotePath, string? mode = null, bool recursive = false, CancellationToken cancellationToken = default);

   public abstract Task Upload(Stream local, string remotePath, string? mode = null, CancellationToken cancellationToken = default);

   /// <summary>
   /// Runs an already-joined command line. <paramref name="onOutputLine"/> receives
   /// ("stdout"|"stderr", line) as output arrives, for DEBUG logging.
   /// </summary>
   protected abstract Task<RunResult> Run(
      string commandLine,
      string? input,
      IReadOnlyDictionary<string, string>? env,
      Action<string, string> onOutputLine,
      CancellationToken cancellationToken);

   private static readonly JsonSerializerOptions PrettyJsonOptions = new() { WriteIndented = true };

   private async Task<RunResult> RunLogged(
      object?[] command,
      Verbosity verbosity,
      bool raiseOnNonZeroExit,
      string? input,
      IReadOnlyDictionary<string, string>? env,
      CancellationToken cancellationToken)
   {
      var commandLine = CommandRendering.Unredacted(command);
      var redacted = CommandRendering.Redacted(command);
      var logger = KamalOutput.Logger;

      logger.CommandStart(Host, redacted, verbosity);

      var stopwatch = Stopwatch.StartNew();
      RunResult result;

      try
      {
         result = await Run(commandLine, input, env, (stream, line) => logger.CommandData(Host, stream, line), cancellationToken).ConfigureAwait(false);
      }
      catch (ExecuteError)
      {
         throw;
      }
      catch (OperationCanceledException)
      {
         throw;
      }
      catch (Exception exception)
      {
         throw new ExecuteError(Host, $"Exception while executing on host {Host}: {exception.Message}", redacted, innerException: exception);
      }

      logger.CommandExit(Host, redacted, result.ExitCode, stopwatch.Elapsed.TotalSeconds, verbosity);

      if (result.ExitCode != 0 && raiseOnNonZeroExit)
      {
         throw new ExecuteError(
            Host,
            $"Command \"{redacted}\" failed on {Host} with exit status {result.ExitCode}: {result.Stderr.Trim()}",
            redacted,
            result.ExitCode,
            result.Stderr);
      }

      return result;
   }
}
