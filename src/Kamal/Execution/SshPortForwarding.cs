using System.Text;
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
            var client = new SshClient(BuildConnectionInfo(host, portOverride ?? TargetPort(ssh), ssh, userOverride))
            {
               KeepAliveInterval = TimeSpan.FromSeconds(30)
            };

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
   {
      var user = userOverride ?? ssh.User;
      var keyFiles = LoadKeyFiles(ssh);
      var methods = new List<AuthenticationMethod>();

      if (keyFiles.Count > 0)
         methods.Add(new PrivateKeyAuthenticationMethod(user, keyFiles.Cast<IPrivateKeySource>().ToArray()));

      methods.Add(new NoneAuthenticationMethod(user));

      return new ConnectionInfo(host, port, user, methods.ToArray())
      {
         Timeout = TimeSpan.FromSeconds(30)
      };
   }

   private static List<PrivateKeyFile> LoadKeyFiles(Ssh ssh)
   {
      var keyFiles = new List<PrivateKeyFile>();

      foreach (var key in RubyHelpers.AsList(ssh.Keys) ?? [])
      {
         var path = ExpandHome(RubyHelpers.RubyToS(key));

         if (File.Exists(path))
            keyFiles.Add(new PrivateKeyFile(path));
      }

      foreach (var keyData in ssh.KeyData ?? [])
         keyFiles.Add(new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(keyData))));

      if (keyFiles.Count > 0)
         return keyFiles;

      var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

      foreach (var name in (string[])["id_ed25519", "id_ecdsa", "id_rsa", "id_dsa"])
      {
         var path = Path.Combine(sshDir, name);

         if (File.Exists(path))
         {
            try
            {
               keyFiles.Add(new PrivateKeyFile(path));
            }
            catch (Exception)
            {
               // Skip unreadable/passphrase-protected default keys.
            }
         }
      }

      return keyFiles;
   }

   private static string ExpandHome(string path)
   {
      if (path.StartsWith("~/", StringComparison.Ordinal) || path == "~")
         return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~', '/', '\\'));

      return path;
   }
}
