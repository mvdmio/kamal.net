using Kamal.Configuration.Validation;
using Kamal.Secrets;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Env</c>: clear values plus secret keys resolved through <see cref="KamalSecrets"/>.</summary>
public sealed class Env
{
   public Env(object? config, KamalSecrets secrets, string context = "env")
   {
      var configDict = RubyHelpers.AsDict(config) ?? new OrderedDictionary<string, object?>();

      Clear = RubyHelpers.AsDict(
            configDict.ContainsKey("clear")
               ? configDict["clear"]
               : configDict.ContainsKey("secret") || configDict.ContainsKey("tags") ? new OrderedDictionary<string, object?>() : configDict)
         ?? new OrderedDictionary<string, object?>();

      Secrets = secrets;
      SecretKeys = (RubyHelpers.AsList(configDict.Fetch("secret", null)) ?? []).Select(RubyHelpers.RubyToS).ToList();
      Context = context;

      new EnvValidator(config ?? configDict, ValidationDocs.Doc("env").Get("env"), context).Validate();
   }

   public string Context { get; }
   public IDictionary<string, object?> Clear { get; }
   public KamalSecrets Secrets { get; }
   public List<string> SecretKeys { get; }

   public List<object> ClearArgs => KamalUtils.Argumentize("--env", Clear);

   /// <summary>
   /// Port of <c>secrets_io</c>: the env-file content with secrets resolved
   /// (the C# port returns the string rather than a StringIO).
   /// </summary>
   public string SecretsIo => new EnvFile(AliasedSecrets.Select(pair => new KeyValuePair<string, object?>(pair.Key, pair.Value))).ToString();

   public Env Merge(Env other)
   {
      var mergedClear = new OrderedDictionary<string, object?>();
      foreach (var (key, value) in Clear)
         mergedClear[key] = value;
      foreach (var (key, value) in other.Clear)
         mergedClear[key] = value;

      var mergedSecrets = SecretKeys.Union(other.SecretKeys).ToList();

      return new Env(
         new OrderedDictionary<string, object?>
         {
            ["clear"] = mergedClear,
            ["secret"] = mergedSecrets.Cast<object?>().ToList()
         },
         Secrets);
   }

   public OrderedDictionary<string, object?> ToH()
   {
      var result = new OrderedDictionary<string, object?>();
      foreach (var (key, value) in Clear)
         result[key] = value;
      foreach (var (key, value) in AliasedSecrets)
         result[key] = value;

      return result;
   }

   private OrderedDictionary<string, string> AliasedSecrets
   {
      get
      {
         var result = new OrderedDictionary<string, string>();
         foreach (var key in SecretKeys)
         {
            var (name, aliasedTo) = ExtractAlias(key);
            result[name] = Secrets[aliasedTo];
         }

         return result;
      }
   }

   private static (string Name, string AliasedTo) ExtractAlias(string key)
   {
      var parts = key.Split(':', 2);
      return parts.Length == 2 ? (parts[0], parts[1]) : (key, key);
   }
}
