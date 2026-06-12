using System.Collections;
using System.Globalization;

namespace Kamal.Utils;

/// <summary>
/// Internal stand-ins for the Ruby/ActiveSupport idioms the Kamal port leans on:
/// <c>present?</c>/<c>blank?</c>, <c>to_s</c>, <c>File.join</c> and <c>Hash#deep_merge</c>.
/// </summary>
internal static class RubyHelpers
{
   /// <summary>ActiveSupport's <c>present?</c>.</summary>
   public static bool IsPresent(object? value) => !IsBlank(value);

   /// <summary>ActiveSupport's <c>blank?</c>.</summary>
   public static bool IsBlank(object? value)
   {
      return value switch
      {
         null => true,
         bool b => !b,
         string s => string.IsNullOrWhiteSpace(s),
         Sensitive => false,
         IDictionary d => d.Count == 0,
         ICollection c => c.Count == 0,
         IEnumerable e => !e.Cast<object?>().Any(),
         _ => false
      };
   }

   /// <summary>ActiveSupport's <c>presence</c>: the string when present, otherwise null.</summary>
   public static string? Presence(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

   /// <summary>Ruby's <c>to_s</c>: notably <c>true</c>/<c>false</c> are lowercase and nil is "".</summary>
   public static string RubyToS(object? value)
   {
      return value switch
      {
         null => "",
         true => "true",
         false => "false",
         string s => s,
         IFormattable f => f.ToString(null, CultureInfo.InvariantCulture) ?? "",
         _ => value.ToString() ?? ""
      };
   }

   /// <summary>
   /// Ruby's <c>File.join</c> for the remote unix paths Kamal builds: always "/" separated
   /// (never the Windows separator), null parts skipped (Ruby uses <c>compact</c> before joining).
   /// </summary>
   public static string JoinPath(params string?[] parts)
   {
      var present = parts.Where(part => part is not null).Cast<string>().ToList();
      if (present.Count == 0)
         return "";

      var result = present[0];
      foreach (var part in present.Skip(1))
      {
         var left = result.TrimEnd('/');
         var right = part.TrimStart('/');
         result = $"{left}/{right}";
      }

      return result;
   }

   /// <summary>Casts a config value to a mapping when it is one.</summary>
   public static IDictionary<string, object?>? AsDict(object? value) => value as IDictionary<string, object?>;

   /// <summary>Casts a config value to a list when it is one (strings and mappings excluded).</summary>
   public static List<object?>? AsList(object? value)
   {
      return value switch
      {
         null => null,
         string => null,
         IDictionary<string, object?> => null,
         List<object?> list => list,
         IEnumerable enumerable => enumerable.Cast<object?>().ToList(),
         _ => null
      };
   }

   /// <summary>Fetches a key from a mapping, null when absent.</summary>
   public static object? Get(this IDictionary<string, object?>? dict, string key)
   {
      if (dict is not null && dict.TryGetValue(key, out var value))
         return value;

      return null;
   }

   /// <summary>Ruby's <c>Hash#fetch(key, default)</c>.</summary>
   public static object? Fetch(this IDictionary<string, object?>? dict, string key, object? defaultValue)
   {
      if (dict is not null && dict.TryGetValue(key, out var value))
         return value;

      return defaultValue;
   }

   /// <summary>Ruby's <c>Hash#dig</c>.</summary>
   public static object? Dig(this IDictionary<string, object?>? dict, params string[] keys)
   {
      object? current = dict;
      foreach (var key in keys)
      {
         current = AsDict(current).Get(key);
         if (current is null)
            return null;
      }

      return current;
   }

   /// <summary>
   /// ActiveSupport's <c>Hash#deep_merge</c>: nested mappings merge recursively, anything else
   /// (including arrays) is replaced by the override value.
   /// </summary>
   public static OrderedDictionary<string, object?> DeepMerge(IDictionary<string, object?> baseDict, IDictionary<string, object?> overrideDict)
   {
      var result = new OrderedDictionary<string, object?>();

      foreach (var (key, value) in baseDict)
         result[key] = value;

      foreach (var (key, value) in overrideDict)
      {
         if (result.TryGetValue(key, out var existing) && existing is IDictionary<string, object?> existingDict && value is IDictionary<string, object?> valueDict)
            result[key] = DeepMerge(existingDict, valueDict);
         else
            result[key] = value;
      }

      return result;
   }
}
