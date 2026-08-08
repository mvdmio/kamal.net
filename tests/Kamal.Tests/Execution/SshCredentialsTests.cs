using System.Text;
using Kamal.Cli;
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

   // Encrypted with passphrase "test-passphrase" (aes256-ctr OpenSSH).
   private const string EncryptedPrivateKeyPem =
      """
      -----BEGIN OPENSSH PRIVATE KEY-----
      b3BlbnNzaC1rZXktdjEAAAAACmFlczI1Ni1jdHIAAAAGYmNyeXB0AAAAGAAAABBTvw4iRk
      FThfHhwekt5XEWAAAAGAAAAAEAAAAzAAAAC3NzaC1lZDI1NTE5AAAAILrwLVQGQacPtx7/
      MBFvQX4uB+SlHHpNCM24NrVMU2LJAAAAkIk1b97rB/3lzZslxgXX8ATuGhliAo9b23l7LG
      jneyPe31MUIA8NbFX4DUkT+ePLZw7SspfCIeXXdlxjpOMEYHzVI4iqozO+ieJizeb48K+v
      iil9wjPC4OlU11yJaM5FY9pkNvXUQwinA5TY/LBFpvpU3e/4R1l7+uP9tXrAcpfnpe5N7j
      cZXMzbjBuZvFZCXw==
      -----END OPENSSH PRIVATE KEY-----
      """;

   private const string EncryptedKeyPassphrase = "test-passphrase";

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
   public void MissingConfiguredKeyFileFailsClosed_DoesNotFallThroughToEnvKey()
   {
      var ssh = NewSsh(new Cfg { ["keys"] = L("/nonexistent/kamal-missing-key") });
      var options = IsolatedOptions(envKey: TestPrivateKeyPem, agentKeys: [], defaultKeys: []);

      var ex = Assert.Throws<AuthError>(() => SshCredentials.Resolve(ssh, options));

      Assert.Contains("ssh.keys", ex.Message, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("fall through", ex.Message, StringComparison.OrdinalIgnoreCase);
      Assert.Equal(FailureClass.Auth, FailureClasses.Classify(ex));
      Assert.Equal(FailureClasses.ExitAuth, FailureClasses.ExitCode(FailureClass.Auth));
   }

   [Fact]
   public void MissingConfiguredKeyFileFailsClosed_DoesNotFallThroughToAgentOrDefaults()
   {
      var ssh = NewSsh(new Cfg { ["keys"] = L("/nonexistent/kamal-missing-key") });
      var options = IsolatedOptions(
         envKey: null,
         agentKeys: [LoadPem(TestPrivateKeyPem)],
         defaultKeys: [LoadPem(AlternatePrivateKeyPem)]);

      var ex = Assert.Throws<AuthError>(() => SshCredentials.Resolve(ssh, options));
      Assert.Equal(FailureClass.Auth, FailureClasses.Classify(ex));
   }

   [Fact]
   public void EnvironmentVariableNameIsKAMAL_SSH_PRIVATE_KEY()
   {
      Assert.Equal("KAMAL_SSH_PRIVATE_KEY", SshCredentials.PrivateKeyEnvironmentVariable);
   }

   [Fact]
   public void PassphraseEnvironmentVariableNameIsKAMAL_SSH_PASSPHRASE()
   {
      Assert.Equal("KAMAL_SSH_PASSPHRASE", SshCredentials.PassphraseEnvironmentVariable);
   }

   [Fact]
   public void EncryptedConfiguredKeyDataLoadsWithPassphrase()
   {
      var ssh = NewSsh(new Cfg { ["key_data"] = L(EncryptedPrivateKeyPem) });
      var options = IsolatedOptions(passphrase: EncryptedKeyPassphrase);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.Configured, resolved.Source);
      Assert.Single(resolved.Keys);
   }

   [Fact]
   public void EncryptedConfiguredKeyFileLoadsWithPassphrase()
   {
      using var dir = new TempKeyDir();
      var path = dir.WriteKey("encrypted", EncryptedPrivateKeyPem);
      var ssh = NewSsh(new Cfg { ["keys"] = L(path) });
      var options = IsolatedOptions(passphrase: EncryptedKeyPassphrase);

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.Configured, resolved.Source);
      Assert.Single(resolved.Keys);
   }

   [Fact]
   public void EncryptedConfiguredKeyWithoutPassphraseNonInteractiveFailsClearlyAsAuth()
   {
      var ssh = NewSsh(new Cfg { ["key_data"] = L(EncryptedPrivateKeyPem) });
      var options = IsolatedOptions(passphrase: null, interactive: false);

      var ex = Assert.Throws<MissingPassphraseError>(() => SshCredentials.Resolve(ssh, options));

      Assert.Contains("encrypted", ex.Message, StringComparison.OrdinalIgnoreCase);
      Assert.Contains(SshCredentials.PassphraseEnvironmentVariable, ex.Message);
      Assert.DoesNotContain("silently", ex.Message, StringComparison.OrdinalIgnoreCase);
      Assert.IsAssignableFrom<AuthError>(ex);
      Assert.Equal(FailureClass.Auth, FailureClasses.Classify(ex));
      Assert.Equal(FailureClasses.ExitAuth, FailureClasses.ExitCode(FailureClasses.Classify(ex)));
   }

   [Fact]
   public void EncryptedConfiguredKeyIsNotSilentlySkippedWhenPassphraseMissing()
   {
      var ssh = NewSsh(new Cfg { ["key_data"] = L(EncryptedPrivateKeyPem) });
      // Env key would win if configured keys were skipped.
      var options = IsolatedOptions(
         envKey: TestPrivateKeyPem,
         passphrase: null,
         interactive: false);

      var ex = Assert.Throws<MissingPassphraseError>(() => SshCredentials.Resolve(ssh, options));
      Assert.Contains(SshCredentials.PassphraseEnvironmentVariable, ex.Message);
      Assert.Equal(FailureClass.Auth, FailureClasses.Classify(ex));
   }

   [Fact]
   public void EncryptedKeyPromptsOnlyWhenInteractive()
   {
      var ssh = NewSsh(new Cfg { ["key_data"] = L(EncryptedPrivateKeyPem) });
      var prompted = false;
      var options = new SshCredentialLoadOptions
      {
         GetEnvironmentPrivateKey = () => null,
         GetPassphrase = () => null,
         GetAgentIdentities = () => [],
         GetDefaultIdentities = () => [],
         IsInteractive = () => true,
         PromptForPassphrase = _ =>
         {
            prompted = true;
            return EncryptedKeyPassphrase;
         }
      };

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.True(prompted);
      Assert.Equal(SshCredentialSource.Configured, resolved.Source);
      Assert.Single(resolved.Keys);
   }

   [Fact]
   public void EncryptedEnvKeyLoadsWithPassphraseFromProcessEnvironment()
   {
      var ssh = NewSsh();
      using var keyEnv = new EnvVarScope(SshCredentials.PrivateKeyEnvironmentVariable, EncryptedPrivateKeyPem);
      using var passEnv = new EnvVarScope(SshCredentials.PassphraseEnvironmentVariable, EncryptedKeyPassphrase);
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
   public void ConfigPassphraseSecretUnlocksEncryptedKeyData()
   {
      using var secrets = new TestSecrets($"KEY_PASS={EncryptedKeyPassphrase}");
      var deploy = BaseDeploy();
      deploy["ssh"] = new Cfg
      {
         ["key_data"] = L(EncryptedPrivateKeyPem),
         ["passphrase"] = "KEY_PASS"
      };
      var ssh = new KamalConfiguration(deploy, secrets: secrets.Secrets).Ssh;
      var options = IsolatedOptions(passphrase: null, interactive: false);
      // Config passphrase is read from Ssh; isolated GetPassphrase null must not block config.
      options = new SshCredentialLoadOptions
      {
         GetEnvironmentPrivateKey = () => null,
         GetPassphrase = null, // allow config / env; no env passphrase set
         GetAgentIdentities = () => [],
         GetDefaultIdentities = () => [],
         IsInteractive = () => false
      };

      var resolved = SshCredentials.Resolve(ssh, options);

      Assert.Equal(SshCredentialSource.Configured, resolved.Source);
      Assert.Single(resolved.Keys);
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
      IReadOnlyList<IPrivateKeySource>? defaultKeys = null,
      string? passphrase = null,
      bool interactive = false) =>
      new()
      {
         GetEnvironmentPrivateKey = () => envKey,
         GetPassphrase = () => passphrase,
         IsInteractive = () => interactive,
         PromptForPassphrase = _ => null,
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
