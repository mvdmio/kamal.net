using System.Text;
using Kamal.Cli;
using Kamal.Configuration;
using Kamal.Utils;
using Renci.SshNet;
using Renci.SshNet.Common;
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

   /// <summary>Returns passphrase for encrypted keys (<c>KAMAL_SSH_PASSPHRASE</c>), or null when unset.</summary>
   public Func<string?>? GetPassphrase { get; init; }

   /// <summary>True when an interactive passphrase prompt is allowed (TTY present).</summary>
   public Func<bool>? IsInteractive { get; init; }

   /// <summary>Prompts for a passphrase (only invoked when interactive). Argument is a key description.</summary>
   public Func<string, string?>? PromptForPassphrase { get; init; }

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

   /// <summary>Environment variable holding the passphrase for encrypted private keys.</summary>
   public const string PassphraseEnvironmentVariable = "KAMAL_SSH_PASSPHRASE";

   public static ConnectionInfo BuildConnectionInfo(
      string host,
      int port,
      Ssh ssh,
      string? userOverride = null,
      SshCredentialLoadOptions? loadOptions = null)
   {
      var user = userOverride ?? ssh.User;
      var credentials = Resolve(ssh, loadOptions);
      return CreateConnectionInfo(host, port, user, credentials.Keys);
   }

   /// <summary>Builds <see cref="ConnectionInfo"/> from already-resolved private key sources.</summary>
   internal static ConnectionInfo CreateConnectionInfo(
      string host,
      int port,
      string user,
      IReadOnlyList<IPrivateKeySource> keys)
   {
      var methods = BuildAuthenticationMethods(user, keys);
      return new ConnectionInfo(host, port, user, methods)
      {
         Timeout = TimeSpan.FromSeconds(30)
      };
   }

   /// <summary>Authentication methods: private keys when present, then "none" so failures surface as auth errors.</summary>
   internal static AuthenticationMethod[] BuildAuthenticationMethods(
      string user,
      IReadOnlyList<IPrivateKeySource> keys)
   {
      var methods = new List<AuthenticationMethod>();

      if (keys.Count > 0)
         methods.Add(new PrivateKeyAuthenticationMethod(user, keys.ToArray()));

      // Password auth is out of scope; keep "none" as a last method so failures surface as auth errors.
      methods.Add(new NoneAuthenticationMethod(user));
      return methods.ToArray();
   }

   /// <summary>
   /// Selects credential sources by public priority: configured <c>keys</c>/<c>key_data</c>,
   /// then <see cref="PrivateKeyEnvironmentVariable"/> if set, then ssh-agent identities,
   /// then default identity files. Higher-priority sources that yield keys exclude lower ones
   /// (no mixing), matching <c>keys_only</c> intent for agent identities when explicit keys are used.
   /// Explicit <c>ssh.keys</c>/<c>key_data</c> fail closed when configured but none load.
   /// </summary>
   public static SshCredentialSet Resolve(Ssh ssh, SshCredentialLoadOptions? loadOptions = null)
   {
      var passphraseState = new PassphraseState(ssh, loadOptions);

      if (HasExplicitConfiguredCredentials(ssh))
      {
         var configured = LoadConfiguredKeyFiles(ssh, passphraseState);
         if (configured.Count == 0)
         {
            throw new AuthError(
               "Configured ssh.keys / ssh.key_data did not yield any usable private keys. "
               + "Check that key paths exist and are readable, and that key_data secrets resolve. "
               + "Kamal does not fall through to KAMAL_SSH_PRIVATE_KEY, ssh-agent, or default "
               + "identity files when explicit keys or key_data are configured.");
         }

         return new SshCredentialSet(SshCredentialSource.Configured, configured);
      }

      var envPem = (loadOptions?.GetEnvironmentPrivateKey ?? GetEnvironmentPrivateKey)();
      if (!string.IsNullOrWhiteSpace(envPem))
      {
         var envKey = LoadPemKey(envPem!, $"{PrivateKeyEnvironmentVariable} private key", passphraseState, required: true);
         return new SshCredentialSet(SshCredentialSource.EnvironmentKey, [envKey]);
      }

      var agentKeys = (loadOptions?.GetAgentIdentities ?? LoadAgentIdentities)();
      if (agentKeys.Count > 0)
         return new SshCredentialSet(SshCredentialSource.Agent, agentKeys);

      if (loadOptions?.GetDefaultIdentities is { } customDefaults)
      {
         var defaults = customDefaults();
         if (defaults.Count > 0)
            return new SshCredentialSet(SshCredentialSource.DefaultIdentityFiles, defaults);
      }
      else
      {
         var defaults = LoadDefaultIdentityFiles(passphraseState);
         if (defaults.Count > 0)
            return new SshCredentialSet(SshCredentialSource.DefaultIdentityFiles, defaults);
      }

      return new SshCredentialSet(SshCredentialSource.None, []);
   }

   /// <summary>True when the operator set non-empty <c>ssh.keys</c> and/or <c>ssh.key_data</c>.</summary>
   internal static bool HasExplicitConfiguredCredentials(Ssh ssh)
   {
      var keys = RubyHelpers.AsList(ssh.Keys);
      if (keys is { Count: > 0 })
         return true;

      var keyData = ssh.KeyData;
      return keyData is { Count: > 0 };
   }

   private static string? GetEnvironmentPrivateKey() =>
      Environment.GetEnvironmentVariable(PrivateKeyEnvironmentVariable);

   private static string? GetEnvironmentPassphrase() =>
      Environment.GetEnvironmentVariable(PassphraseEnvironmentVariable);

   private static List<IPrivateKeySource> LoadConfiguredKeyFiles(Ssh ssh, PassphraseState passphraseState)
   {
      var keyFiles = new List<IPrivateKeySource>();

      foreach (var key in RubyHelpers.AsList(ssh.Keys) ?? [])
      {
         var path = KamalUtils.ExpandHome(RubyHelpers.RubyToS(key));

         if (!File.Exists(path))
            continue;

         keyFiles.Add(LoadKeyFile(path, path, passphraseState, required: true));
      }

      foreach (var keyData in ssh.KeyData ?? [])
         keyFiles.Add(LoadPemKey(keyData, "ssh.key_data private key", passphraseState, required: true));

      return keyFiles;
   }

   private static PrivateKeyFile LoadPemKey(string pem, string description, PassphraseState passphraseState, bool required) =>
      LoadKey(
         description,
         passphraseState,
         required,
         passphrase => passphrase is null
            ? new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(pem)))
            : new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(pem)), passphrase));

   private static PrivateKeyFile LoadKeyFile(string path, string description, PassphraseState passphraseState, bool required) =>
      LoadKey(
         description,
         passphraseState,
         required,
         passphrase => passphrase is null
            ? new PrivateKeyFile(path)
            : new PrivateKeyFile(path, passphrase));

   private static PrivateKeyFile LoadKey(
      string description,
      PassphraseState passphraseState,
      bool required,
      Func<string?, PrivateKeyFile> open)
   {
      try
      {
         // Prefer a known passphrase when one is already available (unencrypted keys ignore it).
         var known = passphraseState.TryGetKnownPassphrase();
         if (known is not null)
            return open(known);

         return open(null);
      }
      catch (SshPassPhraseNullOrEmptyException)
      {
         var passphrase = passphraseState.RequirePassphrase(description);
         try
         {
            return open(passphrase);
         }
         catch (SshException exception)
         {
            throw new AuthError(
               $"Failed to decrypt {description}: incorrect passphrase or corrupt key. {exception.Message}",
               exception);
         }
      }
      catch (SshException exception) when (required)
      {
         throw new AuthError(
            $"Failed to load {description}: {exception.Message}",
            exception);
      }
   }

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

   private static IReadOnlyList<IPrivateKeySource> LoadDefaultIdentityFiles(PassphraseState passphraseState)
   {
      var keyFiles = new List<IPrivateKeySource>();
      var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
      var encryptedWithoutPassphrase = new List<string>();

      foreach (var name in (string[])["id_ed25519", "id_ecdsa", "id_rsa", "id_dsa"])
      {
         var path = Path.Combine(sshDir, name);

         if (!File.Exists(path))
            continue;

         try
         {
            keyFiles.Add(LoadKeyFile(path, path, passphraseState, required: false));
         }
         catch (MissingPassphraseError)
         {
            // Typed missing-passphrase: collect paths; fail closed only if nothing else loads.
            encryptedWithoutPassphrase.Add(path);
         }
         catch (Exception)
         {
            // Skip other unreadable default keys (permissions, corrupt, wrong passphrase, etc.).
         }
      }

      if (keyFiles.Count == 0 && encryptedWithoutPassphrase.Count > 0)
      {
         throw new MissingPassphraseError(
            MissingPassphraseMessage(string.Join(", ", encryptedWithoutPassphrase)));
      }

      return keyFiles;
   }

   internal static string MissingPassphraseMessage(string keyDescription) =>
      $"Private key is encrypted but no passphrase is available ({keyDescription}). "
      + $"Set {PassphraseEnvironmentVariable}, configure ssh.passphrase (secret name or value), "
      + "or run interactively with a TTY so a passphrase can be prompted. "
      + "CI should use ssh-agent, unencrypted keys, key_data, or KAMAL_SSH_PRIVATE_KEY without a passphrase.";

   /// <summary>Resolves and caches a passphrase for the current credential resolution.</summary>
   private sealed class PassphraseState
   {
      private readonly Ssh _ssh;
      private readonly SshCredentialLoadOptions? _loadOptions;
      private string? _resolved;
      private bool _resolvedSet;

      public PassphraseState(Ssh ssh, SshCredentialLoadOptions? loadOptions)
      {
         _ssh = ssh;
         _loadOptions = loadOptions;
      }

      public string? TryGetKnownPassphrase()
      {
         if (_resolvedSet)
            return _resolved;

         var fromOptions = _loadOptions?.GetPassphrase?.Invoke();
         if (!string.IsNullOrEmpty(fromOptions))
         {
            _resolved = fromOptions;
            _resolvedSet = true;
            return _resolved;
         }

         var fromConfig = _ssh.Passphrase;
         if (!string.IsNullOrEmpty(fromConfig))
         {
            _resolved = fromConfig;
            _resolvedSet = true;
            return _resolved;
         }

         // Only consult process env when no GetPassphrase seam was provided (or it returned empty).
         if (_loadOptions?.GetPassphrase is null)
         {
            var fromEnv = GetEnvironmentPassphrase();
            if (!string.IsNullOrEmpty(fromEnv))
            {
               _resolved = fromEnv;
               _resolvedSet = true;
               return _resolved;
            }
         }

         return null;
      }

      public string RequirePassphrase(string keyDescription)
      {
         var known = TryGetKnownPassphrase();
         if (!string.IsNullOrEmpty(known))
            return known!;

         var interactive = (_loadOptions?.IsInteractive ?? IsInteractiveConsole)();
         if (interactive)
         {
            var prompted = (_loadOptions?.PromptForPassphrase ?? PromptForPassphraseConsole)(keyDescription);
            if (!string.IsNullOrEmpty(prompted))
            {
               _resolved = prompted;
               _resolvedSet = true;
               return prompted!;
            }
         }

         throw new MissingPassphraseError(MissingPassphraseMessage(keyDescription));
      }

      private static bool IsInteractiveConsole() =>
         !Console.IsInputRedirected && Environment.UserInteractive;

      private static string? PromptForPassphraseConsole(string keyDescription)
      {
         Console.Error.Write($"Enter passphrase for {keyDescription}: ");
         return Console.ReadLine();
      }
   }
}
