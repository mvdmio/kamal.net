using System.Text;
using Kamal.Configuration;
using Kamal.Execution;
using Kamal.Tests.Configuration;
using Renci.SshNet;
using static Kamal.Tests.Configuration.TestConfig;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Execution;

/// <summary>
/// External selection behaviour for SSH credential sources and priority
/// (configured keys/key_data → KAMAL_SSH_PRIVATE_KEY → agent → default id_*).
/// </summary>
[Collection("kamal-config")]
public class SshCredentialsTests
{
   // Ephemeral OpenSSH ed25519 private keys (no passphrase) for unit tests only.
   private const string TestPrivateKeyPem =
      """
      -----BEGIN OPENSSH PRIVATE KEY-----
      b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZW
      QyNTUxOQAAACDpEYBlW4jWqh6XNVO2pBpkY9dmQ81MuNVGOOOEvcLY3AAAAIi9OUU8vTlF
      PAAAAAtzc2gtZWQyNTUxOQAAACDpEYBlW4jWqh6XNVO2pBpkY9dmQ81MuNVGOOOEvcLY3A
      AAAEBHck7qTEhAkWeXpW5jmbGCnd8VWBwCIGfsJaAykZ6c0ekRgGVbiNaqHpc1U7akGmRj
      12ZDzUy41UY444S9wtjcAAAAAnQxAQID
      -----END OPENSSH PRIVATE KEY-----
      """;

   private const string AlternatePrivateKeyPem =
      """
      -----BEGIN OPENSSH PRIVATE KEY-----
      b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZW
      QyNTUxOQAAACDU5ZGfZ2HYQzeRjuLKAdbgqat8yPgqPWSlFDuCQ7g+2AAAAIjhNfBQ4TXw
      UAAAAAtzc2gtZWQyNTUxOQAAACDU5ZGfZ2HYQzeRjuLKAdbgqat8yPgqPWSlFDuCQ7g+2A
      AAAEDcec3udUW1HnD8jTR3v+jrRCw+iWHxbFiy91udXfiJ4dTlkZ9nYdhDN5GO4soB1uCp
      q3zI+Co9ZKUUO4JDuD7YAAAAAnQyAQID
      -----END OPENSSH PRIVATE KEY-----
      """;

   [Fact]
   public void UsesEnvironmentPrivateKeyWhenNoConfiguredKeys()
   {
      var ssh = NewSsh();
      var options = IsolatedOptions(envKey: TestPrivateKeyPem);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.EnvironmentKey, resolved.Source);
      Assert.Single(resolved.Keys);
      AssertConnectionUsesPrivateKey(ssh, options);
   }

   [Fact]
   public void UsesAgentWhenNoConfiguredKeysOrEnvKey()
   {
      var ssh = NewSsh();
      var agentKey = LoadPem(TestPrivateKeyPem);
      var options = IsolatedOptions(
         envKey: null,
         agentKeys: [agentKey]);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.Agent, resolved.Source);
      Assert.Same(agentKey, Assert.Single(resolved.Keys));
   }

   [Fact]
   public void ConfiguredKeyFileWinsOverEnvAndAgent()
   {
      using var dir = new TempKeyDir();
      var path = dir.WriteKey("deploy_key", TestPrivateKeyPem);
      var ssh = NewSsh(new Cfg { ["keys"] = L(path) });
      var options = IsolatedOptions(
         envKey: AlternatePrivateKeyPem,
         agentKeys: [LoadPem(AlternatePrivateKeyPem)],
         defaultKeys: [LoadPem(AlternatePrivateKeyPem)]);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.Configured, resolved.Source);
      Assert.Single(resolved.Keys);
   }

   [Fact]
   public void ConfiguredKeyDataWinsOverEnvAndAgent()
   {
      var ssh = NewSsh(new Cfg { ["key_data"] = L(TestPrivateKeyPem) });
      var options = IsolatedOptions(
         envKey: AlternatePrivateKeyPem,
         agentKeys: [LoadPem(AlternatePrivateKeyPem)]);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.Configured, resolved.Source);
      Assert.Single(resolved.Keys);
   }

   [Fact]
   public void EnvKeyWinsOverAgentAndDefaults()
   {
      var ssh = NewSsh();
      var options = IsolatedOptions(
         envKey: TestPrivateKeyPem,
         agentKeys: [LoadPem(AlternatePrivateKeyPem)],
         defaultKeys: [LoadPem(AlternatePrivateKeyPem)]);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.EnvironmentKey, resolved.Source);
      Assert.Single(resolved.Keys);
   }

   [Fact]
   public void AgentWinsOverDefaultIdentityFiles()
   {
      var ssh = NewSsh();
      var agentKey = LoadPem(TestPrivateKeyPem);
      var options = IsolatedOptions(
         envKey: null,
         agentKeys: [agentKey],
         defaultKeys: [LoadPem(AlternatePrivateKeyPem)]);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.Agent, resolved.Source);
      Assert.Same(agentKey, Assert.Single(resolved.Keys));
   }

   [Fact]
   public void DefaultIdentityFilesUsedOnlyWhenNothingElseYieldsKeys()
   {
      var ssh = NewSsh();
      var defaultKey = LoadPem(TestPrivateKeyPem);
      var options = IsolatedOptions(
         envKey: null,
         agentKeys: [],
         defaultKeys: [defaultKey]);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.DefaultIdentityFiles, resolved.Source);
      Assert.Same(defaultKey, Assert.Single(resolved.Keys));
   }

   [Fact]
   public void EmptyAgentAndNoEnvFallsThroughToDefaults()
   {
      var ssh = NewSsh();
      var defaultKey = LoadPem(TestPrivateKeyPem);
      var options = IsolatedOptions(envKey: null, agentKeys: [], defaultKeys: [defaultKey]);

      Assert.Equal(SshCredentialSource.DefaultIdentityFiles, SshCredentials.Resolve(ssh, options).Source);
   }

   [Fact]
   public void MissingConfiguredKeyFileDoesNotBlockEnvKey()
   {
      var ssh = NewSsh(new Cfg { ["keys"] = L("/nonexistent/kamal-missing-key") });
      var options = IsolatedOptions(envKey: TestPrivateKeyPem, agentKeys: [], defaultKeys: []);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.EnvironmentKey, resolved.Source);
   }

   [Fact]
   public void EnvironmentVariableNameIsKAMAL_SSH_PRIVATE_KEY()
   {
      Assert.Equal("KAMAL_SSH_PRIVATE_KEY", SshCredentials.PrivateKeyEnvironmentVariable);
   }

   [Fact]
   public void ResolveReadsKAMAL_SSH_PRIVATE_KEYFromProcessEnvironment()
   {
      var ssh = NewSsh();
      using var env = new EnvVarScope(SshCredentials.PrivateKeyEnvironmentVariable, TestPrivateKeyPem);
      // Block agent/defaults so only the real env path can win.
      var options = new SshCredentialLoadOptions
      {
         GetAgentIdentities = () => [],
         GetDefaultIdentities = () => []
      };

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.EnvironmentKey, resolved.Source);
      Assert.Single(resolved.Keys);
   }

   [Fact]
   public void NoneWhenAllSourcesEmpty()
   {
      var ssh = NewSsh();
      var options = IsolatedOptions(envKey: null, agentKeys: [], defaultKeys: []);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.None, resolved.Source);
      Assert.Empty(resolved.Keys);
   }

   private static Ssh NewSsh(Cfg? sshConfig = null)
   {
      var deploy = BaseDeploy();
      if (sshConfig is not null)
         deploy["ssh"] = sshConfig;
      return new KamalConfiguration(deploy).Ssh;
   }

   private static SshCredentialLoadOptions IsolatedOptions(
      string? envKey = null,
      IReadOnlyList<IPrivateKeySource>? agentKeys = null,
      IReadOnlyList<IPrivateKeySource>? defaultKeys = null) =>
      new()
      {
         GetEnvironmentPrivateKey = () => envKey,
         GetAgentIdentities = () => agentKeys ?? [],
         GetDefaultIdentities = () => defaultKeys ?? []
      };

   private static PrivateKeyFile LoadPem(string pem) =>
      new(new MemoryStream(Encoding.UTF8.GetBytes(pem)));

   private static void AssertConnectionUsesPrivateKey(Ssh ssh, SshCredentialLoadOptions options)
   {
      var info = SshCredentials.BuildConnectionInfo("127.0.0.1", 22, ssh, loadOptions: options);
      Assert.Contains(info.AuthenticationMethods, m => m is PrivateKeyAuthenticationMethod);
   }

   private sealed class TempKeyDir : IDisposable
   {
      private readonly string _dir = Path.Combine(Path.GetTempPath(), "kamal-ssh-cred-" + Guid.NewGuid().ToString("N"));

      public TempKeyDir() => Directory.CreateDirectory(_dir);

      public string WriteKey(string name, string pem)
      {
         var path = Path.Combine(_dir, name);
         File.WriteAllText(path, pem);
         return path;
      }

      public void Dispose()
      {
         try
         {
            Directory.Delete(_dir, recursive: true);
         }
         catch (IOException)
         {
         }
      }
   }
}
