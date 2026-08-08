using System.Collections;
using System.Text.RegularExpressions;

namespace Kamal.Configuration;

/// <summary>
/// Expands <c>${ENV_VAR}</c> and <c>${ENV_VAR:-default}</c> in every string scalar of the
/// loaded config tree from the process environment (ADR 0002). Not full ERB; not dotenv rules
/// (bare <c>${VAR}</c> with <c>VAR</c> unset is an error, not empty).
/// </summary>
internal static partial class ConfigExpansion
{
   // ${NAME} or ${NAME:-default} — name matches typical shell/env identifiers.
   [GeneratedRegex(@"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::-([^}]*))?\}", RegexOptions.CultureInvariant)]
   private static partial Regex PlaceholderRegex();

   /// <summary>
   /// Walks the config tree in place, expanding string scalars. Nested mappings and sequences
   /// are visited fully; non-string nodes are left unchanged.
   /// </summary>
   public static IDictionary<string, object?> Expand(IDictionary<string, object?> config)
   {
      ExpandNode(config);
      return config;
   }

   /// <summary>Expands placeholders in a single string using the process environment.</summary>
   public static string ExpandString(string value, Func<string, string?>? getEnv = null)
   {
      getEnv ??= Environment.GetEnvironmentVariable;

      return PlaceholderRegex().Replace(value, match =>
      {
         var name = match.Groups[1].Value;
         var hasDefault = match.Groups[2].Success;
         var envValue = getEnv(name);

         if (envValue is not null)
            return envValue;

         if (hasDefault)
            return match.Groups[2].Value;

         throw new KamalConfigurationError(
            $"Environment variable '{name}' is not set (referenced as ${{{name}}} in configuration). " +
            $"Use ${{{name}:-default}} for optional values.");
      });
   }

   private static object? ExpandNode(object? node)
   {
      switch (node)
      {
         case string s:
            return ExpandString(s);

         case IDictionary<string, object?> dict:
         {
            // Snapshot keys so mutation during enumeration is safe.
            foreach (var key in dict.Keys.ToList())
               dict[key] = ExpandNode(dict[key]);
            return dict;
         }

         case IList list:
         {
            for (var i = 0; i < list.Count; i++)
               list[i] = ExpandNode(list[i]);
            return list;
         }

         default:
            return node;
      }
   }
}
