using System.Text;
using Kamal.Configuration;
using Kamal.Utils;
using Renci.SshNet;

namespace Kamal.Execution;

/// <summary>
/// Shared SSH private-key loading and <see cref="ConnectionInfo"/> construction for
/// <see cref="SshBackend"/> and <see cref="SshPortForwarding"/>.
/// </summary>
internal static class SshCredentials
{
   public static ConnectionInfo BuildConnectionInfo(string host, int port, Ssh ssh, string? userOverride = null)
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

   public static List<PrivateKeyFile> LoadKeyFiles(Ssh ssh)
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

      // No keys configured: fall back to the default identity files, like OpenSSH/net-ssh.
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
