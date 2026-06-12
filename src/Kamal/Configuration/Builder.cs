using System.Security.Cryptography;
using System.Text;
using Kamal.Configuration.Validation;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Builder</c>.</summary>
public sealed class Builder
{
   private readonly KamalConfiguration _config;

   private List<string>? _localArches;
   private List<string>? _remoteArches;
   private string? _cloneDirectory;
   private string? _buildDirectory;

   public Builder(KamalConfiguration config)
   {
      _config = config;
      BuilderConfig = RubyHelpers.AsDict(config.RawConfig.Get("builder")) ?? new OrderedDictionary<string, object?>();

      new BuilderValidator(
         config.RawConfig.Get("builder") ?? BuilderConfig,
         ValidationDocs.Doc("builder").Get("builder"),
         "builder").Validate();
   }

   public IDictionary<string, object?> BuilderConfig { get; }

   public IDictionary<string, object?> ToH() => BuilderConfig;

   public string? Remote => BuilderConfig.Get("remote") as string;

   public List<string> Arches
   {
      get
      {
         var arch = BuilderConfig.Fetch("arch", DefaultArch);
         return arch switch
         {
            null => [],
            string s => [s],
            List<object?> list => list.Select(RubyHelpers.RubyToS).ToList(),
            List<string> list => list,
            _ => [RubyHelpers.RubyToS(arch)]
         };
      }
   }

   public List<string> LocalArches
   {
      get
      {
         return _localArches ??= LocalDisabled
            ? []
            : Remote is not null
               ? Arches.Intersect(new[] { KamalUtils.DockerArch() }).ToList()
               : Arches;
      }
   }

   public List<string> RemoteArches
   {
      get
      {
         return _remoteArches ??= Remote is not null
            ? Arches.Except(LocalArches).ToList()
            : [];
      }
   }

   public bool IsRemote => RemoteArches.Count > 0;

   public bool IsLocal => !LocalDisabled && (Arches.Count == 0 || LocalArches.Count > 0);

   public bool IsCloud => Driver.StartsWith("cloud", StringComparison.Ordinal);

   // Ruby: !!builder_config["cache"]
   public bool Cached => BuilderConfig.Get("cache") is not (null or false);

   public bool Pack => BuilderConfig.Get("pack") is not (null or false);

   public IDictionary<string, object?> Args =>
      RubyHelpers.AsDict(BuilderConfig.Get("args")) ?? new OrderedDictionary<string, object?>();

   public OrderedDictionary<string, string> Secrets
   {
      get
      {
         var secrets = new OrderedDictionary<string, string>();
         foreach (var key in RubyHelpers.AsList(BuilderConfig.Get("secrets")) ?? [])
         {
            var name = RubyHelpers.RubyToS(key);
            secrets[name] = _config.Secrets[name];
         }

         return secrets;
      }
   }

   public string Dockerfile => BuilderConfig.Get("dockerfile") as string ?? "Dockerfile";

   public string? Target => BuilderConfig.Get("target") as string;

   public string Context => BuilderConfig.Get("context") as string ?? ".";

   public string Driver => RubyHelpers.RubyToS(BuilderConfig.Fetch("driver", "docker-container"));

   public string? PackBuilder => Pack ? RubyHelpers.AsDict(BuilderConfig.Get("pack")).Get("builder") as string : null;

   public List<object?>? PackBuildpacks => Pack ? RubyHelpers.AsList(RubyHelpers.AsDict(BuilderConfig.Get("pack")).Get("buildpacks")) : null;

   public bool LocalDisabled => BuilderConfig.Get("local") is false;

   public string? CacheFrom
   {
      get
      {
         if (!Cached)
            return null;

         return CacheType switch
         {
            "gha" => CacheFromConfigForGha,
            "registry" => CacheFromConfigForRegistry,
            _ => null
         };
      }
   }

   public string? CacheTo
   {
      get
      {
         if (!Cached)
            return null;

         return CacheType switch
         {
            "gha" => CacheToConfigForGha,
            "registry" => CacheToConfigForRegistry,
            _ => null
         };
      }
   }

   public object? Ssh => BuilderConfig.Get("ssh");

   public object? Provenance => BuilderConfig.Get("provenance");

   public object? Sbom => BuilderConfig.Get("sbom");

   /// <summary>Whether the build uses a clean git clone (no explicit context configured).</summary>
   public bool GitClone => Git.Used && BuilderConfig.Get("context") is null;

   public string CloneDirectory =>
      _cloneDirectory ??= RubyHelpers.JoinPath(
         Path.GetTempPath().Replace('\\', '/').TrimEnd('/'),
         "kamal-clones",
         string.Join("-", new[] { _config.Service, PwdSha }.Where(part => part is not null)));

   public string BuildDirectory
   {
      get
      {
         return _buildDirectory ??= GitClone
            ? RubyHelpers.JoinPath(CloneDirectory, RepoBasename, RepoRelativePwd)
            : ".";
      }
   }

   public bool DockerDriver => Driver == "docker";

   private string? CacheType => RubyHelpers.AsDict(BuilderConfig.Get("cache")).Get("type") as string;

   private string CacheImage =>
      RubyHelpers.AsDict(BuilderConfig.Get("cache")).Get("image") as string ?? $"{_config.Image}-build-cache";

   private string CacheImageRef =>
      string.Join("/", new[] { _config.Registry.Server, CacheImage }.Where(part => part is not null));

   private string? CacheOptions => RubyHelpers.AsDict(BuilderConfig.Get("cache")).Get("options") as string;

   private string CacheFromConfigForGha
   {
      get
      {
         var individualOptions = CacheOptions?.Split(',') ?? [];
         var allowedOptions = individualOptions
            .Where(option => System.Text.RegularExpressions.Regex.IsMatch(option, "^(url|url_v2|token|scope|timeout)="));

         return string.Join(",", new[] { "type=gha" }.Concat(allowedOptions));
      }
   }

   private string CacheFromConfigForRegistry => string.Join(",", "type=registry", $"ref={CacheImageRef}");

   private string CacheToConfigForGha =>
      string.Join(",", new[] { "type=gha", CacheOptions }.Where(part => part is not null));

   private string CacheToConfigForRegistry =>
      string.Join(",", new[] { "type=registry", $"ref={CacheImageRef}", CacheOptions }.Where(part => part is not null));

   private string RepoBasename => Path.GetFileName(Git.Root.TrimEnd('/'));

   private string RepoRelativePwd
   {
      get
      {
         var pwd = Directory.GetCurrentDirectory().Replace('\\', '/');
         var root = Git.Root.Replace('\\', '/');
         return pwd.StartsWith(root, StringComparison.Ordinal) ? pwd[root.Length..] : pwd;
      }
   }

   private string PwdSha
   {
      get
      {
         var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Directory.GetCurrentDirectory()));
         return Convert.ToHexStringLower(hash)[..13];
      }
   }

   private List<object?>? DefaultArch => DockerDriver ? [] : ["amd64", "arm64"];
}
