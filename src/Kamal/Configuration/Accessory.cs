using Kamal.Configuration.Validation;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>A file or directory mounted into an accessory container.</summary>
public sealed record AccessoryPath(string HostPath, string ContainerPath, string? Options, string? Mode, string? Owner);

/// <summary>Port of <c>Kamal::Configuration::Accessory</c>.</summary>
public sealed class Accessory
{
   public const string DefaultNetwork = "kamal";

   private readonly KamalConfiguration _config;
   private readonly IDictionary<string, object?> _accessoryConfig;

   public Accessory(string name, KamalConfiguration config)
   {
      Name = name;
      _config = config;

      var rawAccessoryConfig = RubyHelpers.AsDict(config.RawConfig.Get("accessories")).Get(name);
      _accessoryConfig = RubyHelpers.AsDict(rawAccessoryConfig) ?? new OrderedDictionary<string, object?>();

      new AccessoryValidator(
         rawAccessoryConfig,
         RubyHelpers.AsDict(ValidationDocs.Doc("accessory").Get("accessories")).Get("mysql"),
         $"accessories/{name}").Validate();

      EnsureValidRoles();

      Env = new Env(
         _accessoryConfig.Fetch("env", new OrderedDictionary<string, object?>()),
         config.Secrets,
         $"accessories/{name}/env");

      if (RunningProxy)
      {
         Proxy = new Proxy(
            config,
            _accessoryConfig.Get("proxy"),
            config.Secrets,
            context: $"accessories/{name}/proxy");
      }

      if (RubyHelpers.IsPresent(_accessoryConfig.Get("registry")))
         Registry = new Registry(_accessoryConfig, config.Secrets, $"accessories/{name}/registry");
   }

   public string Name { get; }
   public Env Env { get; }
   public Proxy? Proxy { get; }
   public Registry? Registry { get; }

   public string ServiceName => _accessoryConfig.Get("service") as string ?? $"{_config.Service}-{Name}";

   public string Image =>
      string.Join("/", new[] { Registry?.Server, RubyHelpers.RubyToS(_accessoryConfig.Get("image")) }.Where(part => part is not null));

   public List<string> Hosts =>
      HostsFromHost() ?? HostsFromHosts() ?? HostsFromRoles() ?? HostsFromTags() ?? [];

   public string? Port
   {
      get
      {
         var port = _accessoryConfig.Get("port");
         if (port is null)
            return null;

         var portString = RubyHelpers.RubyToS(port);
         return portString.Contains(':') ? portString : $"{portString}:{portString}";
      }
   }

   public List<object> NetworkArgs => KamalUtils.Argumentize("--network", Network);

   public List<object> PublishArgs => Port is null ? [] : KamalUtils.Argumentize("--publish", Port);

   public OrderedDictionary<string, object?> Labels
   {
      get
      {
         var labels = new OrderedDictionary<string, object?> { ["service"] = ServiceName };

         if (_accessoryConfig.Get("labels") is IDictionary<string, object?> custom)
         {
            foreach (var (key, value) in custom)
               labels[key] = value;
         }

         return labels;
      }
   }

   public List<object> LabelArgs => KamalUtils.Argumentize("--label", Labels);

   public List<object> EnvArgs
   {
      get
      {
         var args = new List<object>(Env.ClearArgs);
         args.AddRange(KamalUtils.Argumentize("--env-file", SecretsPath));
         return args;
      }
   }

   public string EnvDirectory => RubyHelpers.JoinPath(_config.EnvDirectory, "accessories");

   public string SecretsIo => Env.SecretsIo;

   public string SecretsPath => RubyHelpers.JoinPath(_config.EnvDirectory, "accessories", $"{Name}.env");

   /// <summary>
   /// Port of <c>files</c>, keyed by the expanded local path.
   /// DEVIATION: Ruby evaluates <c>.erb</c> files through ERB (with the accessory env loaded)
   /// and keys them by a StringIO of the rendered content; the C# port treats them as plain files.
   /// </summary>
   public OrderedDictionary<string, AccessoryPath> Files
   {
      get
      {
         var result = new OrderedDictionary<string, AccessoryPath>();

         foreach (var entry in RubyHelpers.AsList(_accessoryConfig.Get("files")) ?? [])
         {
            var (key, path) = ParsePathConfig(entry, defaultMode: "755", (local, remote) =>
               (Key: ExpandLocalFile(local), HostPath: ExpandRemoteFile(remote), ContainerPath: remote));

            result[key] = path;
         }

         return result;
      }
   }

   /// <summary>Port of <c>directories</c>, keyed by the expanded host path.</summary>
   public OrderedDictionary<string, AccessoryPath> Directories
   {
      get
      {
         var result = new OrderedDictionary<string, AccessoryPath>();

         foreach (var entry in RubyHelpers.AsList(_accessoryConfig.Get("directories")) ?? [])
         {
            var (key, path) = ParsePathConfig(entry, defaultMode: null, (local, remote) =>
               (Key: ExpandHostPath(local), HostPath: ExpandHostPathForVolume(local), ContainerPath: remote));

            result[key] = path;
         }

         return result;
      }
   }

   public List<object> VolumeArgs
   {
      get
      {
         var args = KamalUtils.Argumentize("--volume", SpecificVolumes);

         foreach (var volume in PathVolumes(Files).Concat(PathVolumes(Directories)))
            args.AddRange(volume.DockerArgs);

         return args;
      }
   }

   public List<object> OptionArgs =>
      _accessoryConfig.Get("options") is IDictionary<string, object?> args ? KamalUtils.Optionize(args) : [];

   public object? Cmd => _accessoryConfig.Get("cmd");

   public bool RunningProxy => RubyHelpers.IsPresent(_accessoryConfig.Get("proxy"));

   private static IEnumerable<Volume> PathVolumes(OrderedDictionary<string, AccessoryPath> paths)
   {
      return paths.Values.Select(path => new Volume(
         hostPath: path.HostPath,
         containerPath: path.ContainerPath,
         options: path.Options));
   }

   private (string Key, AccessoryPath Path) ParsePathConfig(
      object? config,
      string? defaultMode,
      Func<string, string, (string Key, string HostPath, string ContainerPath)> expand)
   {
      if (config is IDictionary<string, object?> dict)
      {
         var local = RubyHelpers.RubyToS(dict.Get("local"));
         var remote = RubyHelpers.RubyToS(dict.Get("remote"));
         var expanded = expand(local, remote);

         return (expanded.Key, new AccessoryPath(
            HostPath: expanded.HostPath,
            ContainerPath: expanded.ContainerPath,
            Options: dict.Get("options") as string,
            Mode: dict.Get("mode") as string ?? defaultMode,
            Owner: dict.Get("owner") as string));
      }
      else
      {
         var parts = RubyHelpers.RubyToS(config).Split(':', 3);
         var local = parts[0];
         var remote = parts.Length > 1 ? parts[1] : "";
         var options = parts.Length > 2 ? parts[2] : null;
         var expanded = expand(local, remote);

         return (expanded.Key, new AccessoryPath(
            HostPath: expanded.HostPath,
            ContainerPath: expanded.ContainerPath,
            Options: options,
            Mode: defaultMode,
            Owner: null));
      }
   }

   private string ExpandLocalFile(string localFile)
   {
      // DEVIATION: no ERB evaluation for .erb files (see Files docs).
      return Path.GetFullPath(localFile);
   }

   private string ExpandRemoteFile(string remoteFile) => ServiceName + remoteFile;

   private List<object?> SpecificVolumes => RubyHelpers.AsList(_accessoryConfig.Get("volumes")) ?? [];

   private string ExpandHostPath(string hostPath) =>
      Volume.IsAbsolutePath(hostPath) ? hostPath : RubyHelpers.JoinPath(ServiceDataDirectory, hostPath);

   private string ExpandHostPathForVolume(string hostPath) =>
      Volume.IsAbsolutePath(hostPath) ? hostPath : RubyHelpers.JoinPath(ServiceName, hostPath);

   private string ServiceDataDirectory => $"$PWD/{ServiceName}";

   private List<string>? HostsFromHost()
   {
      if (!_accessoryConfig.ContainsKey("host"))
         return null;

      return [RubyHelpers.RubyToS(_accessoryConfig["host"])];
   }

   private List<string>? HostsFromHosts()
   {
      if (!_accessoryConfig.ContainsKey("hosts"))
         return null;

      return (RubyHelpers.AsList(_accessoryConfig["hosts"]) ?? []).Select(RubyHelpers.RubyToS).ToList();
   }

   private List<string>? HostsFromRoles()
   {
      if (_accessoryConfig.ContainsKey("role"))
         return _config.Role(RubyHelpers.RubyToS(_accessoryConfig["role"]))?.Hosts;

      if (_accessoryConfig.ContainsKey("roles"))
      {
         return (RubyHelpers.AsList(_accessoryConfig["roles"]) ?? [])
            .SelectMany(role => _config.Role(RubyHelpers.RubyToS(role))?.Hosts ?? [])
            .ToList();
      }

      return null;
   }

   private List<string>? HostsFromTags()
   {
      if (_accessoryConfig.ContainsKey("tag"))
         return ExtractHostsFromConfigWithTag(RubyHelpers.RubyToS(_accessoryConfig["tag"]));

      if (_accessoryConfig.ContainsKey("tags"))
      {
         return (RubyHelpers.AsList(_accessoryConfig["tags"]) ?? [])
            .SelectMany(tag => ExtractHostsFromConfigWithTag(RubyHelpers.RubyToS(tag)))
            .ToList();
      }

      return null;
   }

   private List<string> ExtractHostsFromConfigWithTag(string tag)
   {
      var hosts = new List<string>();

      if (RubyHelpers.AsDict(_config.RawConfig.Get("servers")) is not { } serversWithRoles)
         return hosts;

      foreach (var (_, serversInRole) in serversWithRoles)
      {
         foreach (var host in RubyHelpers.AsList(serversInRole) ?? [])
         {
            if (host is not IDictionary<string, object?> hostDict || hostDict.Count == 0)
               continue;

            var (hostName, tags) = hostDict.First();

            // Ruby's include?: substring match for strings, membership for arrays.
            var matches = tags switch
            {
               string tagString => tagString.Contains(tag),
               List<object?> tagList => tagList.Select(RubyHelpers.RubyToS).Contains(tag),
               _ => false
            };

            if (matches)
               hosts.Add(hostName);
         }
      }

      return hosts;
   }

   private string Network => _accessoryConfig.Get("network") as string ?? DefaultNetwork;

   private void EnsureValidRoles()
   {
      if (RubyHelpers.AsList(_accessoryConfig.Get("roles")) is { } roles)
      {
         var missingRoles = roles
            .Select(RubyHelpers.RubyToS)
            .Where(role => _config.Roles.All(r => r.Name != role))
            .ToList();

         if (missingRoles.Count > 0)
            throw new KamalConfigurationError($"accessories/{Name}: unknown roles {string.Join(", ", missingRoles)}");
      }
      else if (_accessoryConfig.Get("role") is { } role && _config.Role(RubyHelpers.RubyToS(role)) is null)
      {
         throw new KamalConfigurationError($"accessories/{Name}: unknown role {RubyHelpers.RubyToS(role)}");
      }
   }
}
