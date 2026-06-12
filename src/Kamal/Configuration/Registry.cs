using System.Text.RegularExpressions;
using Kamal.Configuration.Validation;
using Kamal.Secrets;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Registry</c>.</summary>
public sealed partial class Registry
{
   [GeneratedRegex("^localhost[:$]")]
   private static partial Regex LocalhostRegex();

   private readonly IDictionary<string, object?> _registryConfig;
   private readonly KamalSecrets _secrets;

   public Registry(IDictionary<string, object?> config, KamalSecrets secrets, string context = "registry")
   {
      _registryConfig = RubyHelpers.AsDict(config.Get("registry")) ?? new OrderedDictionary<string, object?>();
      _secrets = secrets;

      new RegistryValidator(
         config.Get("registry") ?? _registryConfig,
         ValidationDocs.Doc("registry").Get("registry"),
         context).Validate();
   }

   public string? Server => _registryConfig.Get("server") as string;

   public string? Username => Lookup("username");

   /// <summary>
   /// The registry password, raw like Ruby's accessor. Command layers wrap it with
   /// <see cref="Sensitive"/> before logging (see <c>Kamal::Commands::Registry</c>).
   /// </summary>
   public string? Password => Lookup("password");

   /// <summary>The password pre-wrapped for redaction-aware consumers.</summary>
   public Sensitive? PasswordSensitive => Password is null ? null : new Sensitive(Password, redaction: "[REDACTED]");

   public bool Local => Server is not null && LocalhostRegex().IsMatch(Server);

   public int? LocalPort
   {
      get
      {
         if (!Local)
            return null;

         // Ruby: server.split(":").last.to_i (the `|| 80` fallback is unreachable there).
         return int.TryParse(Server!.Split(':').Last(), out var port) ? port : 0;
      }
   }

   private string? Lookup(string key)
   {
      var value = _registryConfig.Get(key);

      if (value is List<object?> list)
         return _secrets[RubyHelpers.RubyToS(list.First())];

      return value as string ?? (value is null ? null : RubyHelpers.RubyToS(value));
   }
}
