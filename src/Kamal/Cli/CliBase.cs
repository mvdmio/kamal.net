using System.Diagnostics;
using Kamal.Configuration;
using Kamal.Execution;
using Kamal.Output;

namespace Kamal.Cli;

/// <summary>
/// Port of <c>Kamal::Cli::Base</c>: the shared helpers every CLI command class uses —
/// say (with ANSI colors), runtime printing, the deploy lock, hooks, host iteration and the
/// pre-connect handshake.
/// </summary>
public abstract class CliBase
{
   public const string Magenta = "magenta";
   public const string Red = "red";
   public const string Yellow = "yellow";

   /// <summary>Test hook: replaces Ruby's <c>Kernel#exec</c> for interactive commands.</summary>
   public static Action<string>? ExecHandler { get; set; }

   /// <summary>Test hook: replaces stdin for <c>confirming</c> prompts.</summary>
   public static Func<string?>? AskHandler { get; set; }

   protected CliBase(CliContext context)
   {
      Context = context;
   }

   public CliContext Context { get; }

   protected CliOptions Options => Context.Options;

   /// <summary>The KAMAL global.</summary>
   protected static Commander KAMAL => KamalRuntime.Commander;

   // ----- Output ---------------------------------------------------------------------------

   /// <summary>Port of Thor's <c>say</c> + the Kamal override that mirrors output to the audit log.</summary>
   protected void Say(string message = "", string? color = null)
   {
      Console.WriteLine(Colorize(message, color));
      KAMAL.Log(message);
   }

   internal static string Colorize(string message, string? color)
   {
      if (color is null || !UseColor())
         return message;

      var code = color switch
      {
         Magenta => "35",
         Red => "31",
         Yellow => "33",
         _ => null
      };

      return code is null ? message : $"\x1b[{code}m{message}\x1b[0m";
   }

   private static bool UseColor()
   {
      return Environment.GetEnvironmentVariable("NO_COLOR") is null && !Console.IsOutputRedirected;
   }

   /// <summary>Thor's <c>error</c>: writes to stderr.</summary>
   protected static void ErrorOut(string message) => Console.Error.WriteLine(message);

   /// <summary>Port of <c>puts_by_host</c> from sshkit_with_ext.rb.</summary>
   protected static void PutsByHost(string host, string output, string type = "App", bool quiet = false)
   {
      if (!quiet)
         Console.WriteLine($"{type} Host: {host}");

      Console.WriteLine($"{output}\n");
   }

   /// <summary>Port of <c>print_runtime</c>; returns the elapsed seconds.</summary>
   protected static async Task<double> PrintRuntime(Func<Task> action)
   {
      var stopwatch = Stopwatch.StartNew();

      try
      {
         await action().ConfigureAwait(false);
      }
      finally
      {
         Console.WriteLine($"  Finished all in {stopwatch.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} seconds");
      }

      return stopwatch.Elapsed.TotalSeconds;
   }

   // ----- Modify / locking -----------------------------------------------------------------

   /// <summary>Port of <c>modify(lock:)</c>.</summary>
   protected Task Modify(Func<Task> action, bool requireLock = false)
   {
      return KAMAL.Modify(Context.Command, Context.Subcommand, () => requireLock ? WithLock(action) : action());
   }

   /// <summary>Port of <c>with_lock</c>: acquire/release around the action, releasing on error too.</summary>
   protected async Task WithLock(Func<Task> action)
   {
      if (KAMAL.HoldingLock)
      {
         await action().ConfigureAwait(false);
         return;
      }

      await AcquireLock().ConfigureAwait(false);

      try
      {
         await action().ConfigureAwait(false);
      }
      catch
      {
         try
         {
            await ReleaseLock().ConfigureAwait(false);
         }
         catch (Exception e)
         {
            Say($"Error releasing the deploy lock: {e.Message}", Red);
         }

         throw;
      }

      await ReleaseLock().ConfigureAwait(false);
   }

   protected async Task AcquireLock()
   {
      await EnsureRunDirectory().ConfigureAwait(false);

      await RaiseIfLocked(async () =>
      {
         Say("Acquiring the deploy lock...", Magenta);
         await On(KAMAL.PrimaryHost!, backend =>
            backend.Execute(KAMAL.Lock.Acquire("Automatic deploy lock", KAMAL.Config.Version), verbosity: Verbosity.Debug)).ConfigureAwait(false);
      }).ConfigureAwait(false);

      KAMAL.HoldingLock = true;
   }

   protected async Task ReleaseLock()
   {
      Say("Releasing the deploy lock...", Magenta);
      await On(KAMAL.PrimaryHost!, backend =>
         backend.Execute(KAMAL.Lock.Release(), verbosity: Verbosity.Debug)).ConfigureAwait(false);

      KAMAL.HoldingLock = false;
   }

   protected async Task RaiseIfLocked(Func<Task> action)
   {
      try
      {
         await action().ConfigureAwait(false);
      }
      catch (ExecuteError e) when (e.Message.Contains("cannot create directory"))
      {
         Say("Deploy lock already in place!", Red);
         await On(KAMAL.PrimaryHost!, async backend =>
            Console.WriteLine(await backend.CaptureWithDebug(KAMAL.Lock.Status()).ConfigureAwait(false))).ConfigureAwait(false);

         throw new LockError("Deploy lock found. Run 'kamal lock help' for more information");
      }
   }

   // ----- Confirmation ---------------------------------------------------------------------

   /// <summary>Port of <c>confirming(question)</c>: a y/N prompt unless <c>--confirmed</c>.</summary>
   protected async Task Confirming(string question, Func<Task> action)
   {
      if (Options.Confirmed)
      {
         await action().ConfigureAwait(false);
         return;
      }

      Console.Write($"{question} [y, N] ");
      var answer = (AskHandler is { } ask ? ask() : Console.ReadLine())?.Trim();

      if (string.Equals(answer, "y", StringComparison.Ordinal))
         await action().ConfigureAwait(false);
      else
         Say("Aborted", Red);
   }

   // ----- Hooks ----------------------------------------------------------------------------

   /// <summary>Port of <c>run_hook(hook, **extra_details)</c>.</summary>
   protected async Task RunHook(string hook, bool secrets = false, params (string Key, object? Value)[] extraDetails)
   {
      if (Options.SkipHooks || !KAMAL.Hook.HookExists(hook))
         return;

      var details = new List<KeyValuePair<string, object?>>
      {
         new("hosts", string.Join(",", KAMAL.Hosts)),
         new("roles", KAMAL.SpecificRoles is null ? null : string.Join(",", KAMAL.SpecificRoles.Select(role => role.Name))),
         new("lock", KAMAL.HoldingLock ? "true" : "false"),
         new("command", Context.Command),
         new("subcommand", Context.Subcommand)
      };

      details.RemoveAll(pair => pair.Value is null);
      details.AddRange(extraDetails.Select(extra => new KeyValuePair<string, object?>(extra.Key, extra.Value)));

      var hooksOutput = KAMAL.Config.HooksOutputFor(hook);

      // CLI flags override config: -q hides all, -v shows all
      // Config setting :verbose forces output, :quiet forces silence
      var hookVerbosity = KAMAL.Verbosity == Verbosity.Info && hooksOutput is not null
         ? hooksOutput == "verbose" ? Verbosity.Debug : Verbosity.Error
         : KAMAL.Verbosity;

      var env = KAMAL.Hook.Env(secrets, details.ToArray());

      try
      {
         await KAMAL.WithVerbosity(hookVerbosity, () =>
            Coordinator.RunLocally(backend =>
               backend.Execute(KAMAL.Hook.Run(hook), env: env))).ConfigureAwait(false);
      }
      catch (ExecuteError e)
      {
         throw new HookError($"Hook `{hook}` failed:\n{e.Message}");
      }
   }

   // ----- Host iteration -------------------------------------------------------------------

   /// <summary>SSHKit's <c>on(hosts)</c> with Kamal's pre-connect hook.</summary>
   protected async Task On(IEnumerable<string> hosts, Func<IBackend, Task> work)
   {
      await PreConnectIfRequired().ConfigureAwait(false);
      await Coordinator.On(hosts, work).ConfigureAwait(false);
   }

   protected Task On(string host, Func<IBackend, Task> work) => On([host], work);

   /// <summary>SSHKit's <c>run_locally</c>.</summary>
   protected static Task RunLocally(Func<IBackend, Task> work) => Coordinator.RunLocally(work);

   /// <summary>
   /// Port of <c>on_roles(roles, hosts:, parallel:)</c>: parallel runs each role's hosts in its
   /// own task (allowing concurrent connections per host); sequential iterates hosts in parallel
   /// with the roles on each host run in order.
   /// </summary>
   protected async Task OnRoles(IReadOnlyList<Role> roles, IReadOnlyList<string> hosts, Func<IBackend, Role, Task> work, bool parallel = true)
   {
      await PreConnectIfRequired().ConfigureAwait(false);

      if (parallel)
      {
         var tasks = new List<(Role Role, Task Task)>();

         foreach (var role in roles)
         {
            var roleHosts = role.Hosts.Where(hosts.Contains).ToList();

            if (roleHosts.Count > 0)
               tasks.Add((role, Coordinator.On(roleHosts, backend => work(backend, role))));
         }

         var errors = new List<ExecuteError>();

         foreach (var (role, task) in tasks)
         {
            try
            {
               await task.ConfigureAwait(false);
            }
            catch (ExecuteError error)
            {
               errors.Add(error);
            }
            catch (MultipleExecuteError multiple)
            {
               errors.AddRange(multiple.Errors);
            }
            catch (Exception exception)
            {
               errors.Add(new ExecuteError(role.Name, $"Exception while executing on {role}: {exception.Message}", innerException: exception));
            }
         }

         if (errors.Count == 1)
            throw errors[0];

         if (errors.Count > 1)
            throw new MultipleExecuteError(errors);
      }
      else
      {
         // Host-first iteration: hosts run in parallel, roles on each host run sequentially
         await Coordinator.On(hosts, async backend =>
         {
            foreach (var role in roles)
            {
               if (role.Hosts.Contains(backend.Host))
                  await work(backend, role).ConfigureAwait(false);
            }
         }).ConfigureAwait(false);
      }
   }

   protected async Task PreConnectIfRequired()
   {
      if (!KAMAL.Connected)
      {
         if (!Options.SkipHooks)
            await RunHook("pre-connect", secrets: true).ConfigureAwait(false);

         KAMAL.Connected = true;
      }
   }

   protected Task EnsureRunDirectory()
   {
      return On(KAMAL.Hosts, backend => backend.Execute(KAMAL.Server.EnsureRunDirectory()));
   }

   // ----- Local environment ----------------------------------------------------------------

   /// <summary>Port of <c>with_env</c>: temporarily sets process environment variables.</summary>
   protected static async Task WithEnv(IEnumerable<KeyValuePair<string, string>> env, Func<Task> action)
   {
      var original = new Dictionary<string, string?>();

      foreach (var (key, value) in env)
      {
         original[key] = Environment.GetEnvironmentVariable(key);
         Environment.SetEnvironmentVariable(key, value);
      }

      try
      {
         await action().ConfigureAwait(false);
      }
      finally
      {
         foreach (var (key, value) in original)
            Environment.SetEnvironmentVariable(key, value);
      }
   }

   /// <summary>Port of <c>ensure_docker_installed</c> (the CLI helper raising DependencyError).</summary>
   protected static async Task EnsureDockerInstalled()
   {
      try
      {
         await RunLocally(backend => backend.Execute(KAMAL.Builder.EnsureDockerInstalled())).ConfigureAwait(false);
      }
      catch (ExecuteError e)
      {
         var error = e.Message.Contains("command not found") || e.Message.Contains("not recognized")
            ? "Docker is not installed locally"
            : "Docker buildx plugin is not installed locally";

         throw new DependencyError(error);
      }
   }

   /// <summary>Ruby's <c>Kernel#exec</c>: replaces the process with an interactive command.</summary>
   protected static void ExecLocally(string command)
   {
      if (ExecHandler is { } handler)
      {
         handler(command);
         return;
      }

      var startInfo = new ProcessStartInfo { UseShellExecute = false };

      if (OperatingSystem.IsWindows())
      {
         startInfo.FileName = "cmd.exe";
         startInfo.Arguments = $"/d /s /c \"{command}\"";
      }
      else
      {
         startInfo.FileName = "/bin/sh";
         startInfo.ArgumentList.Add("-c");
         startInfo.ArgumentList.Add(command);
      }

      using var process = Process.Start(startInfo)
         ?? throw new InvalidOperationException($"Failed to start process for command: {command}");

      process.WaitForExit();
      SshBackend.DisconnectAll();
      Environment.Exit(process.ExitCode);
   }

   // ----- Small shared helpers ---------------------------------------------------------------

   /// <summary>ActiveSupport's <c>Array#to_sentence</c>.</summary>
   protected static string ToSentence(IReadOnlyList<string> items)
   {
      return items.Count switch
      {
         0 => "",
         1 => items[0],
         2 => $"{items[0]} and {items[1]}",
         _ => $"{string.Join(", ", items.Take(items.Count - 1))}, and {items[^1]}"
      };
   }

   /// <summary>Builds the env mapping the exec commands take from the <c>--env</c> option pairs.</summary>
   protected static OrderedDictionary<string, object?> ParseEnvPairs(string[]? pairs)
   {
      var env = new OrderedDictionary<string, object?>();

      foreach (var pair in pairs ?? [])
      {
         var separator = pair.IndexOf('=');

         if (separator < 0)
            env[pair] = "";
         else
            env[pair[..separator]] = pair[(separator + 1)..];
      }

      return env;
   }
}
