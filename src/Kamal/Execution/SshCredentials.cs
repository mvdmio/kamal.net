using System.Text;
using Kamal.Configuration;
using Kamal.Utils;
using Renci.SshNet;
using SshNet.Agent;

namespace Kamal.Execution;

/// <summary>
/// Which credential source supplied the keys used for an SSH connection.
/// Priority (highest first): configured keys/key_data → <see cref="SshCredentials.PrivateKeyEnvironmentVariable"/> →
/// ssh-agent → default <c>~/.ssh/id_*</c> identity files.
/// </summary>
internal enum SshCredentialSource
{
   /// <summary>No usable keys from any source.</summary>
   None,

   /// <summary><c>ssh.keys</c> paths and/or <c>ssh.key_data</c>.</summary>
   Configured,

   /// <summary><c>KAMAL_SSH_PRIVATE_KEY</c> environment variable PEM material.</summary>
   EnvironmentKey,

   /// <summary>Identities offered by ssh-agent.</summary>
   Agent,

   /// <summary>Default OpenSSH identity files under <c>~/.ssh</c>.</summary>
   DefaultIdentityFiles
}

/// <summary>Resolved private-key material and the source that produced it.</summary>
internal sealed class SshCredentialSet
{
   public SshCredentialSet(SshCredentialSource source, IReadOnlyList<IPrivateKeySource> keys)
   {
      Source = source;
      Keys = keys;
   }

   public SshCredentialSource Source { get; }

   public IReadOnlyList<IPrivateKeySource> Keys { get; }
}

/// <summary>
/// Optional seams for credential resolution (tests inject fakes; production uses process env,
/// ssh-agent, and default identity files).
/// </summary>
internal sealed class SshCredentialLoadOptions
{
   /// <summary>Returns PEM text for <c>KAMAL_SSH_PRIVATE_KEY</c>, or null/empty when unset.</summary>
   public Func<string?>? GetEnvironmentPrivateKey { get; init; }

   /// <summary>Returns identities from ssh-agent (empty when agent is unavailable or has no keys).</summary>
   public Func<IReadOnlyList<IPrivateKeySource>>? GetAgentIdentities { get; init; }

   /// <summary>Returns default <c>~/.ssh/id_*</c> identity sources when nothing higher yielded keys.</summary>
   public Func<IReadOnlyList<IPrivateKeySource>>? GetDefaultIdentities { get; init; }
}

/// <summary>
/// Shared SSH private-key loading and <see cref="ConnectionInfo"/> construction for
/// <see cref="SshBackend"/> and <see cref="SshPortForwarding"/>.
/// </summary>
internal static class SshCredentials
{
   /// <summary>Environment variable holding PEM private-key material (CI convenience).</summary>
   public const string PrivateKeyEnvironmentVariable = "KAMAL_SSH_PRIVATE_KEY";

   public static ConnectionInfo BuildConnectionInfo(
      string host,
      int port,
      Ssh ssh,
      string? userOverride = null,
      SshCredentialLoadOptions? loadOptions = null)
   {
      var user = userOverride ?? ssh.User;
      var credentials = Resolve(ssh, loadOptions);
      var methods = new List<AuthenticationMethod>();

      if (credentials.Keys.Count > 0)
         methods.Add(new PrivateKeyAuthenticationMethod(user, credentials.Keys.ToArray()));

      // Password auth is out of scope; keep "none" as a last method so failures surface as auth errors.
      methods.Add(new NoneAuthenticationMethod(user));

      return new ConnectionInfo(host, port, user, methods.ToArray())
      {
         Timeout = TimeSpan.FromSeconds(30)
      };
   }

   /// <summary>
   /// Selects credential sources by public priority: configured <c>keys</c>/<c>key_data</c>,
   /// then <see cref="PrivateKeyEnvironmentVariable"/> if set, then ssh-agent identities,
   /// then default identity files. Higher-priority sources that yield keys exclude lower ones
   /// (no mixing), matching <c>keys_only</c> intent for agent identities when explicit keys are used.
   /// </summary>
   public static SshCredentialSet Resolve(Ssh ssh, SshCredentialLoadOptions? loadOptions = null)
   {
      var configured = LoadConfiguredKeyFiles(ssh);
      if (configured.Count > 0)
         return new SshCredentialSet(SshCredentialSource.Configured, configured);

      var envPem = (loadOptions?.GetEnvironmentPrivateKey ?? GetEnvironmentPrivateKey)();
      if (!string.IsNullOrWhiteSpace(envPem))
      {
         var envKey = LoadPemKey(envPem);
         return new SshCredentialSet(SshCredentialSource.EnvironmentKey, [envKey]);
      }

      var agentKeys = (loadOptions?.GetAgentIdentities ?? LoadAgentIdentities)();
      if (agentKeys.Count > 0)
         return new SshCredentialSet(SshCredentialSource.Agent, agentKeys);

      var defaults = (loadOptions?.GetDefaultIdentities ?? LoadDefaultIdentityFiles)();
      if (defaults.Count > 0)
         return new SshCredentialSet(SshCredentialSource.DefaultIdentityFiles, defaults);

      return new SshCredentialSet(SshCredentialSource.None, []);
   }

   private static string? GetEnvironmentPrivateKey() =>
      Environment.GetEnvironmentVariable(PrivateKeyEnvironmentVariable);

   private static List<IPrivateKeySource> LoadConfiguredKeyFiles(Ssh ssh)
   {
      var keyFiles = new List<IPrivateKeySource>();

      foreach (var key in RubyHelpers.AsList(ssh.Keys) ?? [])
      {
         var path = ExpandHome(RubyHelpers.RubyToS(key));

         if (File.Exists(path))
            keyFiles.Add(new PrivateKeyFile(path));
      }

      foreach (var keyData in ssh.KeyData ?? [])
         keyFiles.Add(LoadPemKey(keyData));

      return keyFiles;
   }

   private static PrivateKeyFile LoadPemKey(string pem) =>
      new(new MemoryStream(Encoding.UTF8.GetBytes(pem)));

   private static IReadOnlyList<IPrivateKeySource> LoadAgentIdentities()
   {
      // Avoid a doomed connect when no agent socket is configured (Unix).
      if (!OperatingSystem.IsWindows()
          && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_AUTH_SOCK")))
         return [];

      try
      {
         var agent = new SshAgent(TimeSpan.FromSeconds(2));
         var identities = agent.RequestIdentities();
         return identities.Length == 0 ? [] : identities;
      }
      catch (Exception)
      {
         // Agent missing, locked, or unreachable — fall through to default identity files.
         return [];
      }
   }

   private static IReadOnlyList<IPrivateKeySource> LoadDefaultIdentityFiles()
   {
      var keyFiles = new List<IPrivateKeySource>();
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
               // Skip unreadable/passphrase-protected default keys (passphrase path is a later step).
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
