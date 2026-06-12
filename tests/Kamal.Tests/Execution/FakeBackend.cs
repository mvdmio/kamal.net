using Kamal.Execution;

namespace Kamal.Tests.Execution;

/// <summary>
/// A scripted <see cref="BackendBase"/> for unit tests: records the command lines it runs and
/// returns canned <see cref="RunResult"/>s, exercising the shared logging/error logic without SSH.
/// </summary>
public sealed class FakeBackend : BackendBase
{
   private readonly Func<string, string, RunResult> _handler;
   private readonly System.Threading.Lock _lock = new();

   public FakeBackend(string host, Func<string, string, RunResult>? handler = null)
   {
      Host = host;
      _handler = handler ?? ((_, _) => new RunResult(0, "", ""));
   }

   public override string Host { get; }

   public List<string> Commands { get; } = new();

   public List<(object Local, string RemotePath, string? Mode)> Uploads { get; } = new();

   public string? LastInput { get; private set; }

   public IReadOnlyDictionary<string, string>? LastEnv { get; private set; }

   protected override Task<RunResult> Run(
      string commandLine,
      string? input,
      IReadOnlyDictionary<string, string>? env,
      Action<string, string> onOutputLine,
      CancellationToken cancellationToken)
   {
      lock (_lock)
      {
         Commands.Add(commandLine);
         LastInput = input;
         LastEnv = env;
      }

      var result = _handler(Host, commandLine);

      foreach (var line in result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
         onOutputLine("stdout", line);

      return Task.FromResult(result);
   }

   public override Task Upload(string localPath, string remotePath, string? mode = null, bool recursive = false, CancellationToken cancellationToken = default)
   {
      lock (_lock)
         Uploads.Add((localPath, remotePath, mode));

      return Task.CompletedTask;
   }

   public override Task Upload(Stream local, string remotePath, string? mode = null, CancellationToken cancellationToken = default)
   {
      lock (_lock)
         Uploads.Add((local, remotePath, mode));

      return Task.CompletedTask;
   }
}
