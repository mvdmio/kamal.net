using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kamal.Configuration.Validation;
using Kamal.Secrets;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>
/// Port of <c>Kamal::Configuration</c>: loads, validates and exposes the deploy configuration.
/// </summary>
public sealed partial class KamalConfiguration
{
   /// <summary>The Kamal version this port tracks (<c>Kamal::VERSION</c>).</summary>
   public const string KamalVersion = "2.11.0";

   public static readonly string[] HooksOutputLevels = ["quiet", "verbose"];

   [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.IgnoreCase)]
   private static partial Regex ServiceNameRegex();

   private string? _declaredVersion;
   private string? _gitVersion;
   private List<EnvTag>? _envTags;

   /// <summary>
   /// Port of <c>Kamal::Configuration.create_from</c>: loads config/deploy.yml plus the
   /// destination overlay config/deploy.&lt;destination&gt;.yml (deep merged, destination wins).
   /// </summary>
   public static KamalConfiguration CreateFrom(string? configFile = null, string? destination = null, string? version = null, KamalSecrets? secrets = null)
   {
      Environment.SetEnvironmentVariable("KAMAL_DESTINATION", destination);

      var rawConfig = LoadRawConfig(configFile ?? RubyHelpers.JoinPath("config", "deploy.yml"), destination);

      return new KamalConfiguration(rawConfig, destination: destination, version: version, secrets: secrets);
   }

   /// <summary>Port of <c>Kamal::Configuration.load_raw_config</c>, returning the merged raw mapping.</summary>
   public static IDictionary<string, object?> LoadRawConfig(string configFile, string? destination = null)
   {
      var config = LoadConfigFile(configFile);

      if (destination is not null)
      {
         var destinationFile = DestinationConfigFile(configFile, destination);
         config = RubyHelpers.DeepMerge(config, LoadConfigFile(destinationFile));
      }

      return config;
   }

   private static IDictionary<string, object?> LoadConfigFile(string file)
   {
      if (!File.Exists(file))
         throw new InvalidOperationException($"Configuration file not found in {file}");

      // DEVIATION: Ruby renders the file through ERB before YAML parsing; the C# port loads
      // the YAML directly, so `<%= ... %>` templating is not supported.
      var loaded = YamlLoader.LoadFile(file);

      return RubyHelpers.AsDict(loaded) ?? new OrderedDictionary<string, object?>();
   }

   private static string DestinationConfigFile(string baseConfigFile, string destination)
   {
      // Ruby: base_config_file.sub_ext(".#{destination}.yml")
      var directory = Path.GetDirectoryName(baseConfigFile);
      var fileName = Path.GetFileNameWithoutExtension(baseConfigFile);
      var destinationFileName = $"{fileName}.{destination}.yml";

      return string.IsNullOrEmpty(directory) ? destinationFileName : Path.Combine(directory, destinationFileName);
   }

   public KamalConfiguration(IDictionary<string, object?> rawConfig, string? destination = null, string? version = null, KamalSecrets? secrets = null)
   {
      RawConfig = rawConfig;
      Destination = destination;
      _declaredVersion = version;

      new ConfigurationValidator(rawConfig, ValidationDocs.Doc("configuration"), "").Validate();

      Secrets = secrets ?? new KamalSecrets(destination: destination, secretsPath: SecretsPath);

      // Eager load config to validate it; these are first as they have dependencies later on.
      Servers = new Servers(this);
      Registry = new Registry(RawConfig, Secrets);

      Accessories = (RubyHelpers.AsDict(RawConfig.Get("accessories"))?.Keys ?? Enumerable.Empty<string>())
         .Select(name => new Accessory(name, this))
         .ToList();

      Aliases = new OrderedDictionary<string, Alias>();
      foreach (var name in RubyHelpers.AsDict(RawConfig.Get("aliases"))?.Keys ?? Enumerable.Empty<string>())
         Aliases[name] = new Alias(name, this);

      Boot = new Boot(this);
      Builder = new Builder(this);
      Env = new Env(RawConfig.Get("env") ?? new OrderedDictionary<string, object?>(), Secrets);

      Logging = new Logging(RawConfig.Get("logging"));
      Output = new Output(this);
      Proxy = new Proxy(this, RawConfig.Get("proxy"), Secrets);
      ProxyBoot = new ProxyBoot(this);
      Ssh = new Ssh(this);
      Sshkit = new Sshkit(this);

      EnsureDestinationIfRequired();
      EnsureRequiredKeysPresent();
      EnsureValidKamalVersion();
      EnsureRetainContainersValid();
      EnsureValidServiceName();
      EnsureNoTraefikRebootHooks();
      EnsureOneHostForSslRoles();
      EnsureUniqueHostsForSslRoles();
      EnsureLocalRegistryRemoteBuilderHasSshUrl();
      EnsureNoConflictingProxyRuns();
      EnsureValidHooksOutput();
   }

   public IDictionary<string, object?> RawConfig { get; }
   public string? Destination { get; }
   public KamalSecrets Secrets { get; }

   public Servers Servers { get; }
   public Registry Registry { get; }
   public List<Accessory> Accessories { get; }
   public OrderedDictionary<string, Alias> Aliases { get; }
   public Boot Boot { get; }
   public Builder Builder { get; }
   public Env Env { get; }
   public Logging Logging { get; }
   public Output Output { get; }
   public Proxy Proxy { get; }
   public ProxyBoot ProxyBoot { get; }
   public Ssh Ssh { get; }
   public Sshkit Sshkit { get; }

   public string? Service => RawConfig.Get("service") as string;

   public object? Labels => RawConfig.Get("labels");

   /// <summary>Declared version &gt; VERSION env var &gt; git revision (with an _uncommitted_ suffix for dirty non-clone builds).</summary>
   public string Version
   {
      get => RubyHelpers.Presence(_declaredVersion) ?? RubyHelpers.Presence(Environment.GetEnvironmentVariable("VERSION")) ?? GitVersion;
      set => _declaredVersion = value;
   }

   public string? AbbreviatedVersion
   {
      get
      {
         var version = Version;

         // Don't abbreviate <sha>_uncommitted_<etc>
         if (version.Contains('_'))
            return version;

         return version.Length > 7 ? version[..7] : version;
      }
   }

   public string? MinimumVersion => RawConfig.Get("minimum_version") as string;

   public string ServiceAndDestination =>
      string.Join("-", new[] { Service, Destination }.Where(part => part is not null));

   public List<Role> Roles => Servers.Roles;

   public Role? Role(string name) => Roles.FirstOrDefault(role => role.Name == name);

   public Accessory? Accessory(string name) => Accessories.FirstOrDefault(accessory => accessory.Name == name);

   public List<string> AllHosts =>
      Roles.SelectMany(role => role.Hosts).Concat(Accessories.SelectMany(accessory => accessory.Hosts)).Distinct().ToList();

   public List<Role> HostRoles(string host) => Roles.Where(role => role.Hosts.Contains(host)).ToList();

   public List<Accessory> HostAccessories(string host) =>
      Accessories.Where(accessory => accessory.Hosts.Contains(host)).ToList();

   public List<string> AppHosts => Roles.SelectMany(role => role.Hosts).Distinct().ToList();

   public string? PrimaryHost => PrimaryRole?.PrimaryHost;

   public string PrimaryRoleName => RawConfig.Get("primary_role") as string ?? "web";

   public Role? PrimaryRole => Role(PrimaryRoleName);

   public bool AllowEmptyRoles => RawConfig.Get("allow_empty_roles") is true;

   public List<Role> ProxyRoles => Roles.Where(role => role.RunningProxy).ToList();

   public List<string> ProxyRoleNames => ProxyRoles.Select(role => role.Name).ToList();

   public List<Accessory> ProxyAccessories => Accessories.Where(accessory => accessory.RunningProxy).ToList();

   public List<string> ProxyHosts =>
      ProxyRoles.SelectMany(role => role.Hosts).Concat(ProxyAccessories.SelectMany(accessory => accessory.Hosts)).Distinct().ToList();

   public string? Image
   {
      get
      {
         var name = RubyHelpers.Presence(RawConfig.Get("image") as string);
         if (name is null && Registry.Local)
            name = Service;

         return name;
      }
   }

   /// <summary>The proxy run config for a host (all configs for a host are validated to be identical).</summary>
   public ProxyRun? ProxyRunFor(string host) => ProxyRuns(host).FirstOrDefault();

   public string Repository => string.Join("/", new[] { Registry.Server, Image }.Where(part => part is not null));

   public string AbsoluteImage => $"{Repository}:{Version}";

   public string LatestImage => $"{Repository}:{LatestTag}";

   public string LatestTag => string.Join("-", new[] { "latest", Destination }.Where(part => part is not null));

   public string ServiceWithVersion => $"{Service}-{Version}";

   public bool RequireDestination => RawConfig.Get("require_destination") is true;

   public int RetainContainers => Convert.ToInt32(RawConfig.Get("retain_containers") ?? 5);

   public List<object> VolumeArgs
   {
      get
      {
         var volumes = RawConfig.Get("volumes");
         return RubyHelpers.IsPresent(volumes) ? KamalUtils.Argumentize("--volume", volumes) : [];
      }
   }

   public List<object> LoggingArgs => Logging.Args;

   public int ReadinessDelay => Convert.ToInt32(RawConfig.Get("readiness_delay") ?? 7);

   public int DeployTimeout => Convert.ToInt32(RawConfig.Get("deploy_timeout") ?? 30);

   public int DrainTimeout => Convert.ToInt32(RawConfig.Get("drain_timeout") ?? 30);

   public string RunDirectory => ".kamal";

   public string AppsDirectory => RubyHelpers.JoinPath(RunDirectory, "apps");

   public string AppDirectory => RubyHelpers.JoinPath(AppsDirectory, ServiceAndDestination);

   public string EnvDirectory => RubyHelpers.JoinPath(AppDirectory, "env");

   public string AssetsDirectory => RubyHelpers.JoinPath(AppDirectory, "assets");

   public string HooksPath => RawConfig.Get("hooks_path") as string ?? ".kamal/hooks";

   public string SecretsPath => RawConfig.Get("secrets_path") as string ?? ".kamal/secrets";

   public string? AssetPath => RawConfig.Get("asset_path") as string;

   public string? ErrorPagesPath => RawConfig.Get("error_pages_path") as string;

   public List<EnvTag> EnvTags
   {
      get
      {
         return _envTags ??= RubyHelpers.AsDict(RawConfig.Get("env")).Get("tags") is IDictionary<string, object?> tags
            ? tags.Select(pair => new EnvTag(pair.Key, pair.Value, Secrets)).ToList()
            : [];
      }
   }

   public EnvTag? EnvTag(string name) => EnvTags.FirstOrDefault(tag => tag.Name == name);

   /// <summary>Port of <c>hooks_output_for</c>: the configured output level for a hook ("quiet"/"verbose"), or null.</summary>
   public string? HooksOutputFor(string hook)
   {
      return RawConfig.Get("hooks_output") switch
      {
         string global => global,
         IDictionary<string, object?> perHook => perHook.Get(hook) is { } level ? RubyHelpers.RubyToS(level) : null,
         _ => null
      };
   }

   /// <summary>Port of <c>to_h</c>, used by <c>kamal config</c> (apply <see cref="KamalUtils.Redacted"/> before printing).</summary>
   public OrderedDictionary<string, object?> ToH()
   {
      var result = new OrderedDictionary<string, object?>
      {
         ["roles"] = RoleNames,
         ["hosts"] = AllHosts,
         ["primary_host"] = PrimaryHost,
         ["version"] = Version,
         ["repository"] = Repository,
         ["absolute_image"] = AbsoluteImage,
         ["service_with_version"] = ServiceWithVersion,
         ["volume_args"] = VolumeArgs,
         ["ssh_options"] = Ssh.ToH(),
         ["sshkit"] = Sshkit.ToH(),
         ["builder"] = Builder.ToH(),
         ["accessories"] = RawConfig.Get("accessories"),
         ["logging"] = LoggingArgs
      };

      // Ruby's to_h compacts nil values out of the result.
      var compacted = new OrderedDictionary<string, object?>();
      foreach (var (key, value) in result.Where(pair => pair.Value is not null))
         compacted[key] = value;

      return compacted;
   }

   private void EnsureDestinationIfRequired()
   {
      if (RequireDestination && Destination is null)
         throw new ArgumentException("You must specify a destination");
   }

   private void EnsureRequiredKeysPresent()
   {
      foreach (var key in new[] { "service", "registry" })
      {
         if (RubyHelpers.IsBlank(RawConfig.Get(key)))
            throw new KamalConfigurationError($"Missing required configuration for {key}");
      }

      if (RubyHelpers.IsBlank(Image))
         throw new KamalConfigurationError("Missing required configuration for image");

      if (RawConfig.Get("servers") is null)
      {
         if (RubyHelpers.IsBlank(RawConfig.Get("accessories")))
            throw new KamalConfigurationError("No servers or accessories specified");
      }
      else
      {
         if (Role(PrimaryRoleName) is null)
            throw new KamalConfigurationError($"The primary_role {PrimaryRoleName} isn't defined");

         if (PrimaryRole!.Hosts.Count == 0)
            throw new KamalConfigurationError($"No servers specified for the {PrimaryRole.Name} primary_role");

         if (!AllowEmptyRoles)
         {
            foreach (var role in Roles)
            {
               if (role.Hosts.Count == 0)
                  throw new KamalConfigurationError($"No servers specified for the {role.Name} role. You can ignore this with allow_empty_roles: true");
            }
         }
      }
   }

   private void EnsureValidServiceName()
   {
      if (RawConfig.Get("service") is not string service || !ServiceNameRegex().IsMatch(service))
         throw new KamalConfigurationError("Service name can only include alphanumeric characters, hyphens, and underscores");
   }

   private void EnsureValidKamalVersion()
   {
      if (MinimumVersion is not null && KamalUtils.CompareVersions(MinimumVersion, KamalVersion) > 0)
         throw new KamalConfigurationError($"Current version is {KamalVersion}, minimum required is {MinimumVersion}");
   }

   private void EnsureRetainContainersValid()
   {
      if (RetainContainers < 1)
         throw new KamalConfigurationError("Must retain at least 1 container");
   }

   private void EnsureNoTraefikRebootHooks()
   {
      var hooks = new[] { "pre-traefik-reboot", "post-traefik-reboot" }
         .Where(hookFile => File.Exists(Path.Combine(HooksPath, hookFile)))
         .ToList();

      if (hooks.Count > 0)
         throw new KamalConfigurationError($"Found {string.Join(", ", hooks)}, these should be renamed to (pre|post)-proxy-reboot");
   }

   private void EnsureOneHostForSslRoles()
   {
      foreach (var role in Roles)
         role.EnsureOneHostForSsl();
   }

   private void EnsureUniqueHostsForSslRoles()
   {
      var hosts = Roles.Where(role => role.Ssl).SelectMany(role => role.Proxy!.Hosts).ToList();
      var duplicates = hosts
         .GroupBy(host => host)
         .Where(group => group.Count() > 1)
         .Select(group => group.Key)
         .ToList();

      if (duplicates.Count > 0)
         throw new KamalConfigurationError($"Different roles can't share the same host for SSL: {string.Join(", ", duplicates)}");
   }

   private void EnsureLocalRegistryRemoteBuilderHasSshUrl()
   {
      if (Registry.Local && Builder.IsRemote)
      {
         var isSsh = Uri.TryCreate(Builder.Remote, UriKind.Absolute, out var uri) && uri.Scheme == "ssh";

         if (!isSsh)
            throw new KamalConfigurationError("Local registry with remote builder requires an SSH URL (e.g., ssh://user@host)");
      }
   }

   private void EnsureNoConflictingProxyRuns()
   {
      foreach (var host in AllHosts)
      {
         var runConfigs = ProxyRuns(host);

         // Ruby compares Run instances by identity, so two run configs on one host conflict.
         if (runConfigs.Distinct().Count() > 1)
            throw new KamalConfigurationError($"Conflicting proxy run configurations for host {host}");
      }
   }

   private List<ProxyRun> ProxyRuns(string host)
   {
      return HostRoles(host).Select(role => role.Proxy)
         .Concat(HostAccessories(host).Select(accessory => accessory.Proxy))
         .Where(proxy => proxy is not null)
         .Select(proxy => proxy!.Run)
         .Where(run => run is not null)
         .Cast<ProxyRun>()
         .ToList();
   }

   private List<string> RoleNames
   {
      get
      {
         if (RubyHelpers.AsList(RawConfig.Get("servers")) is not null)
            return ["web"];

         return RubyHelpers.AsDict(RawConfig.Get("servers"))?.Keys
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList() ?? [];
      }
   }

   private void EnsureValidHooksOutput()
   {
      switch (RawConfig.Get("hooks_output"))
      {
         case string global:
            ValidateHooksOutputLevel(global);
            break;
         case IDictionary<string, object?> perHook:
            foreach (var (hook, level) in perHook)
               ValidateHooksOutputLevel(RubyHelpers.RubyToS(level), hook);
            break;
      }
   }

   private static void ValidateHooksOutputLevel(string level, string? hook = null)
   {
      if (HooksOutputLevels.Contains(level))
         return;

      var context = hook is not null ? $" for hook '{hook}'" : "";
      throw new KamalConfigurationError($"Invalid hooks_output '{level}'{context}, must be one of: {string.Join(", ", HooksOutputLevels)}");
   }

   private string GitVersion
   {
      get
      {
         if (_gitVersion is not null)
            return _gitVersion;

         if (!Git.Used)
            throw new InvalidOperationException($"Can't use commit hash as version, no git repository found in {Directory.GetCurrentDirectory()}");

         string? uncommittedSuffix = null;
         if (RubyHelpers.IsPresent(Git.UncommittedChanges) && !Builder.GitClone)
            uncommittedSuffix = $"_uncommitted_{RandomHex(8)}";

         return _gitVersion = $"{Git.Revision}{uncommittedSuffix}";
      }
   }

   private static string RandomHex(int bytes)
   {
      var buffer = new byte[bytes];
      RandomNumberGenerator.Fill(buffer);
      return Convert.ToHexStringLower(buffer);
   }
}
