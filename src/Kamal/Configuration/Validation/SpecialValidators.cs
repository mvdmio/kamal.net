using System.Text.RegularExpressions;
using Kamal.Utils;

namespace Kamal.Configuration.Validation;

/// <summary>Port of <c>Kamal::Configuration::Validator::Configuration</c>: the root validator allows x- extensions.</summary>
public class ConfigurationValidator : Validator
{
   public ConfigurationValidator(object? config, object? example, string context)
      : base(config, example, context)
   {
   }

   protected override bool AllowExtensions => true;
}

/// <summary>Port of <c>Kamal::Configuration::Validator::Accessory</c>.</summary>
public class AccessoryValidator : Validator
{
   public AccessoryValidator(object? config, object? example, string context)
      : base(config, example, context)
   {
   }

   public override void Validate()
   {
      base.Validate();

      var config = (IDictionary<string, object?>)Config!;
      var hostKeys = new[] { "host", "hosts", "role", "roles", "tag", "tags" };

      if (config.Keys.Count(hostKeys.Contains) != 1)
         Error("specify one of `host`, `hosts`, `role`, `roles`, `tag` or `tags`");

      ValidateLabels(config.Get("labels"));
      ValidateDockerOptions(config.Get("options"));
   }
}

/// <summary>Port of <c>Kamal::Configuration::Validator::Alias</c>.</summary>
public partial class AliasValidator : Validator
{
   // The built-in `Kamal::Cli::Main` commands an alias must not shadow.
   private static readonly string[] ReservedCommands =
   [
      "accessory", "app", "audit", "build", "config", "deploy", "details", "docs", "help",
      "init", "lock", "proxy", "prune", "redeploy", "registry", "remove", "rollback",
      "secrets", "server", "setup", "upgrade", "version"
   ];

   [GeneratedRegex("^[a-z0-9_-]+$")]
   private static partial Regex NameRegex();

   public AliasValidator(object? config, object? example, string context)
      : base(config, example, context)
   {
   }

   public override void Validate()
   {
      base.Validate();

      var name = Context.StartsWith("aliases/") ? Context["aliases/".Length..] : Context;

      if (!NameRegex().IsMatch(name))
         Error($"Invalid alias name: '{name}'. Must only contain lowercase letters, alphanumeric, hyphens and underscores.");

      if (ReservedCommands.Contains(name))
         Error($"Alias '{name}' conflicts with a built-in command.");
   }
}

/// <summary>Port of <c>Kamal::Configuration::Validator::Builder</c>.</summary>
public class BuilderValidator : Validator
{
   public BuilderValidator(object? config, object? example, string context)
      : base(config, example, context)
   {
   }

   public override void Validate()
   {
      base.Validate();

      var config = (IDictionary<string, object?>)Config!;

      if (config.Get("cache") is IDictionary<string, object?> cache && cache.Get("type") is not null)
      {
         var cacheType = RubyHelpers.RubyToS(cache.Get("type"));
         if (cacheType is not ("gha" or "registry"))
            Error($"Invalid cache type: {cacheType}");
      }

      if (RubyHelpers.IsBlank(config.Get("arch")))
         Error("Builder arch not set");

      if (config.Get("pack") is not null && config.Get("arch") is List<object?> { Count: > 1 })
         Error("buildpacks only support building for one arch");

      if (config.Get("local") is false && RubyHelpers.IsBlank(config.Get("remote")))
         Error("Cannot disable local builds, no remote is set");
   }
}

/// <summary>Port of <c>Kamal::Configuration::Validator::Env</c>.</summary>
public class EnvValidator : Validator
{
   private static readonly string[] SpecialKeys = ["clear", "secret", "tags"];

   public EnvValidator(object? config, object? example, string context)
      : base(config, example, context)
   {
   }

   private IDictionary<string, object?> ConfigDict => (IDictionary<string, object?>)Config!;

   private List<string> KnownKeys => ConfigDict.Keys.Where(SpecialKeys.Contains).ToList();
   private List<string> UnknownKeys => ConfigDict.Keys.Where(key => !SpecialKeys.Contains(key)).ToList();

   public override void Validate()
   {
      if (Config is not IDictionary<string, object?>)
      {
         // Ruby fails with NoMethodError here; the port reports the type mismatch instead.
         ValidateType(Config, ConfigType.Hash);
         return;
      }

      if (KnownKeys.Count > 0)
         ValidateComplexEnv();
      else
         ValidateSimpleEnv();
   }

   private void ValidateSimpleEnv()
   {
      ValidateHashOf(Config, ConfigType.String);
   }

   private void ValidateComplexEnv()
   {
      if (UnknownKeys.Count > 0)
         UnknownKeysError(UnknownKeys);

      if (ConfigDict.ContainsKey("clear"))
         WithContext("clear", () => ValidateHashOf(ConfigDict["clear"], ConfigType.String));

      if (ConfigDict.ContainsKey("secret"))
         WithContext("secret", () => ValidateArrayOf(ConfigDict["secret"], ConfigType.String));

      if (ConfigDict.ContainsKey("tags"))
         ValidateTags();
   }

   private void ValidateTags()
   {
      if (Context == "env")
      {
         WithContext("tags", () =>
         {
            ValidateType(ConfigDict["tags"], ConfigType.Hash);

            foreach (var (tag, value) in (IDictionary<string, object?>)ConfigDict["tags"]!)
            {
               WithContext(tag, () =>
               {
                  ValidateType(value, ConfigType.Hash);

                  var exampleTags = (IDictionary<string, object?>)((IDictionary<string, object?>)Example!)["tags"]!;
                  new EnvValidator(value, exampleTags.Values.Skip(1).FirstOrDefault(), Context).Validate();
               });
            }
         });
      }
      else
      {
         Error("tags are only allowed in the root env");
      }
   }
}

/// <summary>Port of <c>Kamal::Configuration::Validator::Proxy</c>.</summary>
public class ProxyValidator : Validator
{
   public ProxyValidator(object? config, object? example, string context)
      : base(config, example, context)
   {
   }

   public override void Validate()
   {
      if (Config is null)
         return;

      base.Validate();

      var config = (IDictionary<string, object?>)Config!;

      if (RubyHelpers.IsBlank(config.Get("host")) && RubyHelpers.IsBlank(config.Get("hosts")) && RubyHelpers.IsPresent(config.Get("ssl")))
         Error("Must set a host to enable automatic SSL");

      if (config.Keys.Count(key => key is "host" or "hosts") > 1)
         Error("Specify one of 'host' or 'hosts', not both");

      if (config.Get("ssl") is IDictionary<string, object?> ssl)
      {
         if (RubyHelpers.IsPresent(ssl.Get("certificate_pem")) && RubyHelpers.IsBlank(ssl.Get("private_key_pem")))
            Error("Missing private_key_pem setting (required when certificate_pem is present)");

         if (RubyHelpers.IsPresent(ssl.Get("private_key_pem")) && RubyHelpers.IsBlank(ssl.Get("certificate_pem")))
            Error("Missing certificate_pem setting (required when private_key_pem is present)");
      }

      if (config.Get("run") is IDictionary<string, object?> runConfig)
      {
         if (RubyHelpers.IsPresent(runConfig.Get("bind_ips")))
         {
            // Faithful port of the Ruby code, which checks config["bind_ips"] (always nil) here.
            EnsureValidBindIps(config.Get("bind_ips"));
         }

         if (runConfig.Get("publish") is false)
         {
            if (RubyHelpers.IsPresent(runConfig.Get("bind_ips")) || RubyHelpers.IsPresent(runConfig.Get("http_port")) || RubyHelpers.IsPresent(runConfig.Get("https_port")))
               Error("Cannot set http_port, https_port or bind_ips when publish is false");
         }
      }
   }

   private void EnsureValidBindIps(object? bindIps)
   {
      if (RubyHelpers.IsBlank(bindIps) || bindIps is not List<object?> ips)
         return;

      foreach (var ip in ips)
      {
         if (!System.Net.IPAddress.TryParse(RubyHelpers.RubyToS(ip), out _))
            Error($"Invalid publish IP address: {ip}");
      }
   }
}

/// <summary>Port of <c>Kamal::Configuration::Validator::Registry</c>.</summary>
public partial class RegistryValidator : Validator
{
   private static readonly string[] StringOrOneItemArrayKeys = ["username", "password"];

   [GeneratedRegex("^localhost[:$]")]
   private static partial Regex LocalhostRegex();

   public RegistryValidator(object? config, object? example, string context)
      : base(config, example, context)
   {
   }

   public override void Validate()
   {
      var config = (IDictionary<string, object?>)Config!;
      var example = (IDictionary<string, object?>)Example!;

      ValidateAgainstExample(
         Except(config, StringOrOneItemArrayKeys),
         Except(example, StringOrOneItemArrayKeys));

      ValidateStringOrOneItemArray("username");
      ValidateStringOrOneItemArray("password");
   }

   private static OrderedDictionary<string, object?> Except(IDictionary<string, object?> dict, string[] keys)
   {
      var result = new OrderedDictionary<string, object?>();
      foreach (var (key, value) in dict)
      {
         if (!keys.Contains(key))
            result[key] = value;
      }

      return result;
   }

   private void ValidateStringOrOneItemArray(string key)
   {
      WithContext(key, () =>
      {
         var config = (IDictionary<string, object?>)Config!;
         var value = config.Get(key);

         var server = config.Get("server") as string;
         if (server is not null && LocalhostRegex().IsMatch(server))
            return;

         if (RubyHelpers.IsBlank(value))
            Error("is required");

         if (!(value is string || (value is List<object?> { Count: 1 } list && list[0] is string)))
            Error("should be a string or an array with one string (for secret lookup)");
      });
   }
}

/// <summary>Port of <c>Kamal::Configuration::Validator::Role</c>.</summary>
public class RoleValidator : Validator
{
   public RoleValidator(object? config, object? example, string context)
      : base(config, example, context)
   {
   }

   public override void Validate()
   {
      ValidateType(Config, ConfigType.Array, ConfigType.Hash);

      if (Config is List<object?>)
      {
         ValidateServers(Config);
      }
      else
      {
         base.Validate();

         var config = (IDictionary<string, object?>)Config!;
         ValidateLabels(config.Get("labels"));
         ValidateDockerOptions(config.Get("options"));
      }
   }
}

/// <summary>Port of <c>Kamal::Configuration::Validator::Servers</c>.</summary>
public class ServersValidator : Validator
{
   public ServersValidator(object? config, object? example, string context)
      : base(config, example, context)
   {
   }

   public override void Validate()
   {
      ValidateType(Config, ConfigType.Array, ConfigType.Hash, ConfigType.Nil);

      if (Config is List<object?>)
         ValidateServers(Config);
   }
}
