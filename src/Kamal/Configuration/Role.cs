using Kamal.Configuration.Validation;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Role</c>.</summary>
public sealed class Role
{
   private readonly KamalConfiguration _config;
   private readonly Dictionary<string, Env> _envs = new();
   private readonly bool _runningProxy;

   private Logging? _logging;
   private Proxy? _proxy;
   private OrderedDictionary<string, List<string>>? _taggedHosts;
   private IDictionary<string, object?>? _roleConfig;
   private IDictionary<string, object?>? _specializations;

   public Role(string name, KamalConfiguration config)
   {
      Name = name;
      _config = config;

      new RoleValidator(
         RoleConfigRaw,
         RubyHelpers.AsDict(ValidationDocs.Doc("role").Get("servers")).Get("workers"),
         $"servers/{name}").Validate();

      SpecializedEnv = new Env(
         Specializations.Fetch("env", new OrderedDictionary<string, object?>()),
         config.Secrets,
         $"servers/{name}/env");

      SpecializedLogging = new Logging(
         Specializations.Fetch("logging", new OrderedDictionary<string, object?>()),
         $"servers/{name}/logging");

      var proxySpecializations = Specializations.Get("proxy");

      if (Primary)
      {
         // Only false means no proxy for the primary role.
         _runningProxy = proxySpecializations is not false;
      }
      else
      {
         // false and nil both mean no proxy for non-primary roles.
         _runningProxy = proxySpecializations is not (null or false);
      }

      if (_runningProxy)
      {
         var proxyConfig = proxySpecializations is true or null
            ? new OrderedDictionary<string, object?>()
            : proxySpecializations;

         SpecializedProxy = new Proxy(
            config,
            proxyConfig,
            config.Secrets,
            roleName: name,
            context: $"servers/{name}/proxy");
      }
   }

   public string Name { get; }
   public Env SpecializedEnv { get; }
   public Logging SpecializedLogging { get; }
   public Proxy? SpecializedProxy { get; }

   public override string ToString() => Name;

   public string? PrimaryHost => Hosts.FirstOrDefault();

   public List<string> Hosts => TaggedHosts.Keys.ToList();

   public List<EnvTag> EnvTags(string host)
   {
      return TaggedHosts[host]
         .Select(tag => _config.EnvTag(tag))
         .Where(tag => tag is not null)
         .Cast<EnvTag>()
         .ToList();
   }

   public object? Cmd => Specializations.Get("cmd");

   public List<object> OptionArgs =>
      Specializations.Get("options") is IDictionary<string, object?> args ? KamalUtils.Optionize(args) : [];

   public OrderedDictionary<string, object?> Labels
   {
      get
      {
         var labels = DefaultLabels;
         foreach (var (key, value) in CustomLabels)
            labels[key] = value;

         return labels;
      }
   }

   public List<object> LabelArgs => KamalUtils.Argumentize("--label", Labels);

   public List<object> LoggingArgs => Logging.Args;

   public Logging Logging => _logging ??= _config.Logging.Merge(SpecializedLogging);

   public Proxy? Proxy => RunningProxy ? _proxy ??= SpecializedProxy!.Merge(_config.Proxy) : null;

   public bool RunningProxy => _runningProxy;

   public bool Ssl => RunningProxy && Proxy!.Ssl;

   public List<object> StopArgs
   {
      get
      {
         // When deploying with the proxy, kamal-proxy will drain requests before returning so we don't need to wait.
         object? timeout = RunningProxy ? null : _config.DrainTimeout;

         return KamalUtils.Argumentize("-t", timeout is null ? null : new List<object?> { timeout });
      }
   }

   public Env Env(string host)
   {
      if (_envs.TryGetValue(host, out var env))
         return env;

      var merged = new[] { _config.Env, SpecializedEnv }
         .Concat(EnvTags(host).Select(tag => tag.Env))
         .Aggregate((a, b) => a.Merge(b));

      return _envs[host] = merged;
   }

   public List<object> EnvArgs(string host)
   {
      var args = new List<object>(Env(host).ClearArgs);
      args.AddRange(KamalUtils.Argumentize("--env-file", SecretsPath));
      return args;
   }

   public string EnvDirectory => RubyHelpers.JoinPath(_config.EnvDirectory, "roles");

   public string SecretsIo(string host) => Env(host).SecretsIo;

   public string SecretsPath => RubyHelpers.JoinPath(_config.EnvDirectory, "roles", $"{Name}.env");

   public List<object>? AssetVolumeArgs => AssetVolume()?.DockerArgs;

   public bool Primary => Name == _config.PrimaryRoleName;

   public string ContainerName(string? version = null)
   {
      return string.Join("-", new[] { ContainerPrefix, version ?? _config.Version }.Where(part => part is not null));
   }

   public string ContainerPrefix =>
      string.Join("-", new[] { _config.Service, Name, _config.Destination }.Where(part => part is not null));

   public string? AssetPath => AssetPathConfig?.Path;

   public bool Assets => RubyHelpers.IsPresent(AssetPath) && RunningProxy;

   public Volume? AssetVolume(string? version = null)
   {
      if (!Assets)
         return null;

      return new Volume(
         hostPath: AssetVolumeDirectory(version),
         containerPath: AssetPath!,
         options: AssetPathOptions);
   }

   public string? AssetPathOptions => AssetPathConfig?.Options;

   public string AssetExtractedDirectory(string? version = null)
   {
      return RubyHelpers.JoinPath(_config.AssetsDirectory, "extracted", $"{Name}-{version ?? _config.Version}");
   }

   public string AssetVolumeDirectory(string? version = null)
   {
      return RubyHelpers.JoinPath(_config.AssetsDirectory, "volumes", $"{Name}-{version ?? _config.Version}");
   }

   public void EnsureOneHostForSsl()
   {
      if (RunningProxy && Proxy!.Ssl && Hosts.Count > 1 && !Proxy.CustomSslCertificate)
      {
         throw new KamalConfigurationError(
            $"SSL is only supported on a single server unless you provide custom certificates, found {Hosts.Count} servers for role {Name}");
      }
   }

   private OrderedDictionary<string, List<string>> TaggedHosts
   {
      get
      {
         if (_taggedHosts is not null)
            return _taggedHosts;

         var taggedHosts = new OrderedDictionary<string, List<string>>();

         foreach (var hostConfig in ExtractHostsFromConfig())
         {
            if (hostConfig is IDictionary<string, object?> hostDict && hostDict.Count > 0)
            {
               var (host, tags) = hostDict.First();
               taggedHosts[host] = tags switch
               {
                  null => [],
                  string tag => [tag],
                  List<object?> list => list.Select(RubyHelpers.RubyToS).ToList(),
                  _ => [RubyHelpers.RubyToS(tags)]
               };
            }
            else if (hostConfig is string host)
            {
               taggedHosts[host] = [];
            }
         }

         return _taggedHosts = taggedHosts;
      }
   }

   private List<object?> ExtractHostsFromConfig()
   {
      if (RubyHelpers.AsList(_config.RawConfig.Get("servers")) is { } simpleServers)
         return simpleServers;

      var servers = RubyHelpers.AsDict(_config.RawConfig.Get("servers")).Get(Name);
      if (RubyHelpers.AsList(servers) is { } serverList)
         return serverList;

      return RubyHelpers.AsList(RubyHelpers.AsDict(servers).Get("hosts")) ?? [];
   }

   private OrderedDictionary<string, object?> DefaultLabels => new()
   {
      ["service"] = _config.Service,
      ["role"] = Name,
      ["destination"] = _config.Destination
   };

   private IDictionary<string, object?> Specializations =>
      _specializations ??= RubyHelpers.AsDict(RoleConfigRaw) ?? new OrderedDictionary<string, object?>();

   private object? RoleConfigRaw
   {
      get
      {
         if (_roleConfig is not null)
            return _roleConfig;

         if (RubyHelpers.AsList(_config.RawConfig.Get("servers")) is not null)
            return _roleConfig = new OrderedDictionary<string, object?>();

         var value = RubyHelpers.AsDict(_config.RawConfig.Get("servers")).Get(Name);
         if (value is IDictionary<string, object?> dict)
            _roleConfig = dict;

         return value;
      }
   }

   private OrderedDictionary<string, object?> CustomLabels
   {
      get
      {
         var labels = new OrderedDictionary<string, object?>();

         if (RubyHelpers.AsDict(_config.Labels) is { } configLabels && configLabels.Count > 0)
         {
            foreach (var (key, value) in configLabels)
               labels[key] = value;
         }

         if (Specializations.Get("labels") is IDictionary<string, object?> specializedLabels && specializedLabels.Count > 0)
         {
            foreach (var (key, value) in specializedLabels)
               labels[key] = value;
         }

         return labels;
      }
   }

   private (string Path, string? Options)? AssetPathConfig
   {
      get
      {
         var rawPath = Specializations.Get("asset_path") as string ?? _config.AssetPath;
         if (RubyHelpers.IsBlank(rawPath))
            return null;

         var parts = rawPath!.Split(':', 2);
         return (parts[0], parts.Length > 1 ? parts[1] : null);
      }
   }
}
