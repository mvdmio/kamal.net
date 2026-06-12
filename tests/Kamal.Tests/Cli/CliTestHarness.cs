using System.Collections.Concurrent;
using Kamal.Cli;
using Kamal.Execution;
using Kamal.Tests.Configuration;
using Kamal.Tests.Execution;
using Kamal.Utils;

namespace Kamal.Tests.Cli;

/// <summary>
/// CLI test world, the stand-in for the Ruby cli tests' SSHKit stubbing: a temp working
/// directory with a deploy config, fake backends recording every command (with scriptable
/// responses), captured console output, a fake git runner, and a pinned VERSION.
/// </summary>
public sealed class CliTestHarness : IDisposable
{
   public const string DefaultDeploy =
      """
      service: app
      image: dhh/app
      servers:
        - 1.1.1.1
        - 1.1.1.2
      registry:
        username: user
        password: pw
      builder:
        arch: amd64
      """;

   private readonly StringWriter _output = new();
   private readonly StringWriter _error = new();
   private readonly TextWriter _originalOut;
   private readonly TextWriter _originalError;
   private readonly string _originalCwd;
   private readonly IGitRunner _originalGit;
   private readonly string? _originalVersionEnv;

   public CliTestHarness(string? deployYml = DefaultDeploy, string version = "999")
   {
      Dir = Path.Combine(Path.GetTempPath(), "kamal-cli-tests-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(Dir);
      _originalCwd = Directory.GetCurrentDirectory();
      Directory.SetCurrentDirectory(Dir);

      _originalOut = Console.Out;
      Console.SetOut(_output);
      _originalError = Console.Error;
      Console.SetError(_error);

      KamalRuntime.Reset();

      Coordinator.BackendFactory = host => new FakeBackend(host, Handle);
      Coordinator.LocalBackendFactory = () => new FakeBackend("localhost", Handle);

      _originalGit = Git.Runner;
      Git.Runner = new FakeGitRunner { UsedResult = false };

      _originalVersionEnv = Environment.GetEnvironmentVariable("VERSION");
      Environment.SetEnvironmentVariable("VERSION", version);

      if (deployYml is not null)
      {
         Directory.CreateDirectory(Path.Combine(Dir, "config"));
         File.WriteAllText(Path.Combine(Dir, "config", "deploy.yml"), deployYml);
      }
   }

   public string Dir { get; }

   /// <summary>All executed commands, in arrival order, with the host they ran on.</summary>
   public ConcurrentQueue<(string Host, string Command)> Commands { get; } = new();

   /// <summary>Scripted responses: the first responder returning non-null wins; default exit 0, no output.</summary>
   public List<Func<string, string, RunResult?>> Responders { get; } = new();

   public string Output => _output.ToString();

   public string ErrorOutput => _error.ToString();

   public List<string> CommandsOn(string host) => Commands.Where(entry => entry.Host == host).Select(entry => entry.Command).ToList();

   public List<string> AllCommands => Commands.Select(entry => entry.Command).ToList();

   public Task<int> Run(params string[] args) => KamalCli.Start(args);

   public void RespondTo(string commandFragment, string stdout, int exitCode = 0, string stderr = "")
   {
      Responders.Add((_, command) => command.Contains(commandFragment) ? new RunResult(exitCode, stdout, stderr) : null);
   }

   private RunResult Handle(string host, string command)
   {
      var result = Responders.Select(responder => responder(host, command)).FirstOrDefault(response => response is not null);

      Commands.Enqueue((host, command));

      return result ?? new RunResult(0, "", "");
   }

   public void Dispose()
   {
      Directory.SetCurrentDirectory(_originalCwd);
      Console.SetOut(_originalOut);
      Console.SetError(_originalError);
      Environment.SetEnvironmentVariable("VERSION", _originalVersionEnv);
      Git.Runner = _originalGit;
      CliBase.ExecHandler = null;
      CliBase.AskHandler = null;
      KamalRuntime.Reset();

      try
      {
         Directory.Delete(Dir, recursive: true);
      }
      catch (IOException)
      {
      }
      catch (UnauthorizedAccessException)
      {
      }
   }
}
