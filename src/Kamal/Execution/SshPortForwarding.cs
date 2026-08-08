using Kamal.Configuration;
using Kamal.Utils;
using Renci.SshNet;

namespace Kamal.Execution;

/// <summary>
/// Port of <c>Kamal::Cli::Build::PortForwarding</c>'s transport: forwards a local port to remote
/// hosts over SSH (the remote side listens on 127.0.0.1:port and tunnels back to the local
/// registry), so remote builders and hosts can reach a local registry. Connection settings come
/// from the deploy's <see cref="Ssh"/> configuration, with optional user/port overrides for
/// <c>ssh://</c> remote builder URLs.
/// </summary>
public sealed class SshPortForwarding : IDisposable
{
   /// <summary>Test hook: replaces the real SSH forwarding (returns a disposable per forwarding session).</summary>
   public static Func<IReadOnlyList<string>, int, IDisposable>? ForwarderFactory { get; set; }

   private readonly List<IDisposable> _resources = new();

   private SshPortForwarding()
   {
   }

   public static SshPortForwarding Start(IReadOnlyList<string> hosts, int port, Ssh ssh, string? userOverride = null, int? portOverride = null)
   {
      var forwarding = new SshPortForwarding();

      if (ForwarderFactory is { } factory)
      {
         forwarding._resources.Add(factory(hosts, port));
         return forwarding;
      }

      try
      {
         foreach (var host in hosts)
         {
            var sshPort = portOverride ?? TargetPort(ssh);
            var client = new SshClient(BuildConnectionInfo(host, sshPort, ssh, userOverride))
            {
               KeepAliveInterval = TimeSpan.FromSeconds(30)
            };
            SshHostKeyPolicy.Apply(client, host, sshPort, ssh);

            forwarding._resources.Add(client);
            client.Connect();

            var forwardedPort = new ForwardedPortRemote("127.0.0.1", (uint)port, "localhost", (uint)port);
            client.AddForwardedPort(forwardedPort);
            forwarding._resources.Add(forwardedPort);
            forwardedPort.Start();

            if (!forwardedPort.IsStarted)
               throw new InvalidOperationException($"Failed to establish port forward on {host}");
         }
      }
      catch
      {
         forwarding.Dispose();
         throw;
      }

      return forwarding;
   }

   public void Dispose()
   {
      for (var i = _resources.Count - 1; i >= 0; i--)
      {
         try
         {
            if (_resources[i] is ForwardedPortRemote forwardedPort && forwardedPort.IsStarted)
               forwardedPort.Stop();

            _resources[i].Dispose();
         }
         catch
         {
            // Best-effort teardown.
         }
      }

      _resources.Clear();
   }

   private static int TargetPort(Ssh ssh) => Convert.ToInt32(RubyHelpers.RubyToS(ssh.Port));

   private static ConnectionInfo BuildConnectionInfo(string host, int port, Ssh ssh, string? userOverride)
      => SshCredentials.BuildConnectionInfo(host, port, ssh, userOverride);
}
