using System.Collections.Concurrent;
using Renci.SshNet;

namespace Kamal.Execution;

/// <summary>
/// A pooled SSH connection: the target client plus, when a jump proxy is configured,
/// the bastion client and the local forwarded port tunnelling to the target.
/// </summary>
internal sealed class PooledSshConnection : IDisposable
{
   private long _lastUsedTicks = DateTime.UtcNow.Ticks;

   public PooledSshConnection(SshClient client, SshClient? jumpClient = null, ForwardedPortLocal? forwardedPort = null)
   {
      Client = client;
      JumpClient = jumpClient;
      ForwardedPort = forwardedPort;
   }

   public SshClient Client { get; }
   public SshClient? JumpClient { get; }
   public ForwardedPortLocal? ForwardedPort { get; }

   public DateTime LastUsedUtc => new(Interlocked.Read(ref _lastUsedTicks), DateTimeKind.Utc);

   public void Touch() => Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);

   public void Dispose()
   {
      try
      {
         Client.Dispose();
      }
      catch
      {
         // Best-effort teardown.
      }

      try
      {
         if (ForwardedPort is { IsStarted: true })
            ForwardedPort.Stop();
      }
      catch
      {
      }

      try
      {
         JumpClient?.Dispose();
      }
      catch
      {
      }
   }
}

/// <summary>
/// Connection reuse keyed by host (SSHKit's <c>Netssh.pool</c>): connections are shared across
/// commands and evicted after the configured idle timeout (<c>sshkit.pool_idle_timeout</c>).
/// </summary>
internal static class SshConnectionPool
{
   private static readonly ConcurrentDictionary<string, Task<PooledSshConnection>> Connections = new();
   private static Timer? _cleanupTimer;
   private static readonly Lock CleanupLock = new();

   /// <summary>Idle timeout before a pooled connection is closed; set from <c>config.sshkit.pool_idle_timeout</c>.</summary>
   public static TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(900);

   public static async Task<PooledSshConnection> GetAsync(string host, Func<string, CancellationToken, Task<PooledSshConnection>> factory, CancellationToken cancellationToken)
   {
      EnsureCleanupTimer();

      while (true)
      {
         cancellationToken.ThrowIfCancellationRequested();

         var task = Connections.GetOrAdd(host, h => factory(h, cancellationToken));
         PooledSshConnection connection;

         try
         {
            connection = await task.ConfigureAwait(false);
         }
         catch
         {
            Connections.TryRemove(new KeyValuePair<string, Task<PooledSshConnection>>(host, task));
            throw;
         }

         if (connection.Client.IsConnected)
         {
            connection.Touch();
            return connection;
         }

         // Stale connection: drop it and retry with a fresh one.
         if (Connections.TryRemove(new KeyValuePair<string, Task<PooledSshConnection>>(host, task)))
            connection.Dispose();
      }
   }

   /// <summary>Closes all pooled connections (used on shutdown and between test runs).</summary>
   public static void DisconnectAll()
   {
      foreach (var (host, task) in Connections.ToArray())
      {
         if (!Connections.TryRemove(new KeyValuePair<string, Task<PooledSshConnection>>(host, task)))
            continue;

         if (task.IsCompletedSuccessfully)
            task.Result.Dispose();
      }
   }

   private static void EnsureCleanupTimer()
   {
      if (_cleanupTimer is not null)
         return;

      lock (CleanupLock)
      {
         _cleanupTimer ??= new Timer(_ => EvictIdle(), null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
      }
   }

   private static void EvictIdle()
   {
      var cutoff = DateTime.UtcNow - IdleTimeout;

      foreach (var (host, task) in Connections.ToArray())
      {
         if (!task.IsCompletedSuccessfully)
            continue;

         var connection = task.Result;

         if (connection.LastUsedUtc < cutoff && Connections.TryRemove(new KeyValuePair<string, Task<PooledSshConnection>>(host, task)))
            connection.Dispose();
      }
   }
}
