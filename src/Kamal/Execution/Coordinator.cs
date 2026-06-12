namespace Kamal.Execution;

/// <summary>
/// The .NET mirror of SSHKit's <c>on(hosts) { ... }</c> / <c>run_locally { ... }</c> DSL.
/// Hosts run in parallel (SSHKit's default <c>:parallel</c> runner, with the Kamal patch that
/// waits for all hosts and aggregates the failures); the group overload mirrors
/// <c>on(hosts, in: :groups, limit:, wait:)</c> used for rolling boots.
/// </summary>
public static class Coordinator
{
   private static readonly Func<string, IBackend> DefaultBackendFactory = static host => new SshBackend(host);
   private static readonly Func<IBackend> DefaultLocalBackendFactory = static () => new LocalBackend();

   /// <summary>Creates the per-host backend; replaceable in tests with a fake.</summary>
   public static Func<string, IBackend> BackendFactory { get; set; } = DefaultBackendFactory;

   /// <summary>Creates the local backend; replaceable in tests with a fake.</summary>
   public static Func<IBackend> LocalBackendFactory { get; set; } = DefaultLocalBackendFactory;

   /// <summary>Restores the default factories (for tests).</summary>
   public static void Reset()
   {
      BackendFactory = DefaultBackendFactory;
      LocalBackendFactory = DefaultLocalBackendFactory;
   }

   /// <summary>Runs the work on a single host.</summary>
   public static Task On(string host, Func<IBackend, Task> work, CancellationToken cancellationToken = default)
   {
      return On([host], work, cancellationToken);
   }

   /// <summary>
   /// Runs the work on all hosts in parallel. All hosts run to completion; a single failure is
   /// rethrown as <see cref="ExecuteError"/>, multiple failures aggregate into
   /// <see cref="MultipleExecuteError"/>.
   /// </summary>
   public static async Task On(IEnumerable<string> hosts, Func<IBackend, Task> work, CancellationToken cancellationToken = default)
   {
      var distinctHosts = hosts.Distinct().ToList();

      var tasks = distinctHosts
         .Select(host => (Host: host, Task: Task.Run(() => work(BackendFactory(host)), cancellationToken)))
         .ToList();

      var errors = new List<ExecuteError>();

      foreach (var (host, task) in tasks)
      {
         try
         {
            await task.ConfigureAwait(false);
         }
         catch (ExecuteError error)
         {
            errors.Add(error);
         }
         catch (Exception exception)
         {
            errors.Add(new ExecuteError(host, $"Exception while executing on host {host}: {exception.Message}", innerException: exception));
         }
      }

      if (errors.Count == 1)
         throw errors[0];

      if (errors.Count > 1)
         throw new MultipleExecuteError(errors);
   }

   /// <summary>
   /// Group semantics of <c>on(hosts, in: :groups, limit:, wait:)</c>: hosts are sliced into
   /// groups of <paramref name="limit"/>, each group runs in parallel, and execution pauses
   /// <paramref name="waitSeconds"/> after each group. A failing group stops subsequent groups.
   /// </summary>
   public static async Task On(IEnumerable<string> hosts, int? limit, double? waitSeconds, Func<IBackend, Task> work, CancellationToken cancellationToken = default)
   {
      var distinctHosts = hosts.Distinct().ToList();
      var groups = limit is > 0
         ? distinctHosts.Chunk(limit.Value).Select(group => group.ToList()).ToList()
         : [distinctHosts];

      foreach (var group in groups)
      {
         await On(group, work, cancellationToken).ConfigureAwait(false);

         if (waitSeconds is > 0)
            await Task.Delay(TimeSpan.FromSeconds(waitSeconds.Value), cancellationToken).ConfigureAwait(false);
      }
   }

   /// <summary>SSHKit's <c>run_locally { ... }</c>: runs the work against the local backend.</summary>
   public static Task RunLocally(Func<IBackend, Task> work)
   {
      return work(LocalBackendFactory());
   }
}
