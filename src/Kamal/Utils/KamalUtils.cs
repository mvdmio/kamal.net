using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using Kamal.Secrets;

namespace Kamal.Utils;

/// <summary>
/// Port of <c>Kamal::Utils</c>: shell argument building, escaping, redaction and small helpers.
/// </summary>
public static partial class KamalUtils
{
   [GeneratedRegex(@"\$(?!{[^\}]*\})")]
   private static partial Regex DollarSignWithoutShellExpansionRegex();

   /// <summary>Override used by tests; when null, <c>docker info</c> is queried (and cached).</summary>
   public static string? DockerArchOverride { get; set; }

   private static string? _dockerArchCache;

   /// <summary>
   /// Port of <c>Kamal::Utils.argumentize</c>: returns a list of escaped shell arguments using the
   /// same named argument against the passed attributes (mapping, list or scalar).
   /// Elements are strings (or <see cref="Sensitive"/> wrappers, or the raw key for valueless entries).
   /// </summary>
   public static List<object> Argumentize(string argument, object? attributes, bool sensitive = false)
   {
      var result = new List<object>();

      foreach (var (key, value) in EnumeratePairs(attributes))
      {
         if (RubyHelpers.IsPresent(value))
         {
            var attr = $"{RubyHelpers.RubyToS(key)}={EscapeShellValue(value)}";
            result.Add(argument);
            result.Add(sensitive ? new Sensitive(attr, redaction: $"{RubyHelpers.RubyToS(key)}=[REDACTED]") : attr);
         }
         else if (value is false)
         {
            result.Add(argument);
            result.Add($"{RubyHelpers.RubyToS(key)}=false");
         }
         else
         {
            result.Add(argument);
            result.Add(key!);
         }
      }

      return result;
   }

   /// <summary>
   /// Port of <c>Kamal::Utils.optionize</c>: returns a list of shell-dashed option arguments.
   /// If the value is true, it's treated like a value-less option.
   /// </summary>
   public static List<object> Optionize(IDictionary<string, object?> args, string? with = null, bool escape = true)
   {
      var options = new List<object?>();

      foreach (var (key, value) in FlattenArgs(args))
      {
         if (with is not null)
         {
            options.Add(value is true ? $"--{key}" : $"--{key}{with}{(escape ? EscapeShellValue(value) : RubyHelpers.RubyToS(value))}");
         }
         else
         {
            options.Add($"--{key}");
            options.Add(value is true ? null : escape ? EscapeShellValue(value) : value);
         }
      }

      return options.Where(option => option is not null).Cast<object>().ToList();
   }

   /// <summary>
   /// Port of <c>Kamal::Utils.flatten_args</c>: flattens a one-to-many structure into
   /// key-value pairs (list values fan out into one pair per entry).
   /// </summary>
   public static List<KeyValuePair<string, object?>> FlattenArgs(IDictionary<string, object?> args)
   {
      var result = new List<KeyValuePair<string, object?>>();

      foreach (var (key, value) in args)
      {
         if (value is not string && value is not IDictionary && value is IEnumerable enumerable)
         {
            foreach (var entry in enumerable)
               result.Add(new KeyValuePair<string, object?>(key, entry));
         }
         else
         {
            result.Add(new KeyValuePair<string, object?>(key, value));
         }
      }

      return result;
   }

   /// <summary>Marks a sensitive value for redaction in logs and human-visible output.</summary>
   public static Sensitive MakeSensitive(string value, string redaction = "[REDACTED]") => new(value, redaction);

   /// <summary>
   /// Port of <c>Kamal::Utils.redacted</c>: replaces sensitive values (recursively through
   /// mappings and lists) with their redactions.
   /// </summary>
   public static object? Redacted(object? value)
   {
      return value switch
      {
         IRedactable redactable => redactable.Redaction,
         IDictionary<string, object?> dict => RedactedDict(dict),
         string s => s,
         IEnumerable enumerable => enumerable.Cast<object?>().Select(Redacted).ToList(),
         _ => value
      };
   }

   private static OrderedDictionary<string, object?> RedactedDict(IDictionary<string, object?> dict)
   {
      var result = new OrderedDictionary<string, object?>();
      foreach (var (key, value) in dict)
         result[key] = Redacted(value);

      return result;
   }

   /// <summary>Port of <c>Kamal::Utils.escape_shell_value</c>: escape a value to make it safe for shell use.</summary>
   public static string EscapeShellValue(object? value)
   {
      var str = RubyHelpers.RubyToS(value);
      var result = new StringBuilder();

      // Ruby splits into ASCII and non-ASCII runs and only escapes the ASCII parts.
      foreach (var part in SplitAsciiRuns(str))
         result.Append(IsAsciiOnly(part) ? EscapeAsciiShellValue(part) : part);

      return result.ToString();
   }

   /// <summary>Port of <c>Kamal::Utils.escape_ascii_shell_value</c>.</summary>
   public static string EscapeAsciiShellValue(string value)
   {
      var dumped = RubyDump(value)
         .Replace("`", "\\`");

      return DollarSignWithoutShellExpansionRegex().Replace(dumped, "\\$");
   }

   /// <summary>
   /// Port of <c>Kamal::Utils.filter_specific_items</c>: applies a list of host or role filters,
   /// including wildcard matches.
   /// </summary>
   public static List<string> FilterSpecificItems(IEnumerable<string>? filters, IEnumerable<string>? items)
   {
      var matches = new List<string>();
      var itemList = items?.ToList() ?? [];

      foreach (var filter in filters ?? [])
         matches.AddRange(itemList.Where(item => FnMatch(filter, item)));

      return matches.Distinct().ToList();
   }

   /// <summary>Port of <c>Kamal::Utils.stable_sort!</c>: in-place stable sort by a key.</summary>
   public static void StableSort<T, TKey>(List<T> elements, Func<T, TKey> keySelector)
   {
      var sorted = elements.OrderBy(keySelector).ToList();
      elements.Clear();
      elements.AddRange(sorted);
   }

   /// <summary>Port of <c>Kamal::Utils.join_commands</c>.</summary>
   public static string JoinCommands(IEnumerable<string> commands) => string.Join(" ", commands.Select(command => command.Trim()));

   /// <summary>
   /// Port of <c>Kamal::Utils.docker_arch</c>: the local Docker architecture, normalized to
   /// amd64/arm64. Cached per process (deviation: Ruby shells out on every call).
   /// </summary>
   public static string DockerArch()
   {
      if (DockerArchOverride is not null)
         return DockerArchOverride;

      return _dockerArchCache ??= ResolveDockerArch();
   }

   /// <summary>
   /// Port of <c>Kamal::Utils.older_version?</c>: compares versions with RubyGems semantics
   /// (a leading "v" is ignored).
   /// </summary>
   public static bool OlderVersion(string version, string otherVersion)
   {
      return CompareVersions(TrimVersionPrefix(version), TrimVersionPrefix(otherVersion)) < 0;
   }

   /// <summary>Approximation of <c>Gem::Version</c> comparison: numeric segments compare numerically, letters sort before numbers (prerelease).</summary>
   public static int CompareVersions(string version, string otherVersion)
   {
      var left = SplitVersion(version);
      var right = SplitVersion(otherVersion);

      for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
      {
         var a = i < left.Count ? left[i] : "0";
         var b = i < right.Count ? right[i] : "0";

         var aNumeric = long.TryParse(a, out var aNum);
         var bNumeric = long.TryParse(b, out var bNum);

         int comparison;
         if (aNumeric && bNumeric)
            comparison = aNum.CompareTo(bNum);
         else if (aNumeric)
            comparison = 1; // numbers sort after prerelease strings
         else if (bNumeric)
            comparison = -1;
         else
            comparison = string.CompareOrdinal(a, b);

         if (comparison != 0)
            return comparison;
      }

      return 0;
   }

   internal static string TrimVersionPrefix(string version) => version.StartsWith('v') ? version[1..] : version;

   private static List<string> SplitVersion(string version)
   {
      // Split on dots and at letter/digit boundaries, like Gem::Version's canonical segments.
      return Regex.Matches(version, "[0-9]+|[a-zA-Z]+").Select(m => m.Value).ToList();
   }

   private static string ResolveDockerArch()
   {
      var result = ShellRunner.Run("docker info --format '{{.Architecture}}'");
      var arch = result.Success ? result.Stdout.Trim().Trim('\'') : "";

      if (arch.Contains("aarch64"))
         return "arm64";
      if (arch.Contains("x86_64"))
         return "amd64";

      return arch;
   }

   private static IEnumerable<KeyValuePair<object?, object?>> EnumeratePairs(object? attributes)
   {
      switch (attributes)
      {
         case null:
            yield break;
         case IDictionary<string, object?> dict:
            foreach (var (key, value) in dict)
               yield return new KeyValuePair<object?, object?>(key, value);
            yield break;
         case string s:
            yield return new KeyValuePair<object?, object?>(s, null);
            yield break;
         case IDictionary nonGeneric:
            foreach (DictionaryEntry entry in nonGeneric)
               yield return new KeyValuePair<object?, object?>(entry.Key, entry.Value);
            yield break;
         case IEnumerable enumerable:
            foreach (var element in enumerable)
               yield return new KeyValuePair<object?, object?>(element, null);
            yield break;
         default:
            yield return new KeyValuePair<object?, object?>(attributes, null);
            yield break;
      }
   }

   private static IEnumerable<string> SplitAsciiRuns(string value)
   {
      if (value.Length == 0)
         yield break;

      var start = 0;
      var currentAscii = value[0] <= 0x7F;

      for (var i = 1; i < value.Length; i++)
      {
         var isAscii = value[i] <= 0x7F;
         if (isAscii != currentAscii)
         {
            yield return value[start..i];
            start = i;
            currentAscii = isAscii;
         }
      }

      yield return value[start..];
   }

   private static bool IsAsciiOnly(string value) => value.All(c => c <= 0x7F);

   /// <summary>Ruby's <c>String#dump</c> for ASCII content: wraps in double quotes and escapes specials.</summary>
   private static string RubyDump(string value)
   {
      var sb = new StringBuilder(value.Length + 2);
      sb.Append('"');

      for (var i = 0; i < value.Length; i++)
      {
         var c = value[i];
         switch (c)
         {
            case '"': sb.Append("\\\""); break;
            case '\\': sb.Append("\\\\"); break;
            case '\n': sb.Append("\\n"); break;
            case '\t': sb.Append("\\t"); break;
            case '\r': sb.Append("\\r"); break;
            case '\f': sb.Append("\\f"); break;
            case '\v': sb.Append("\\v"); break;
            case '\b': sb.Append("\\b"); break;
            case '\a': sb.Append("\\a"); break;
            case (char)0x1B: sb.Append("\\e"); break;
            case '\0': sb.Append("\\0"); break;
            case '#':
               if (i + 1 < value.Length && value[i + 1] is '{' or '$' or '@')
                  sb.Append("\\#");
               else
                  sb.Append('#');
               break;
            default:
               if (c < 0x20 || c == 0x7F)
                  sb.Append($"\\x{(int)c:X2}");
               else
                  sb.Append(c);
               break;
         }
      }

      sb.Append('"');
      return sb.ToString();
   }

   /// <summary>Minimal <c>File.fnmatch</c> with <c>FNM_EXTGLOB</c>: supports *, ?, [...] and {a,b}.</summary>
   private static bool FnMatch(string pattern, string value)
   {
      var regex = new StringBuilder("^");

      for (var i = 0; i < pattern.Length; i++)
      {
         var c = pattern[i];
         switch (c)
         {
            case '*': regex.Append(".*"); break;
            case '?': regex.Append('.'); break;
            case '{': regex.Append("(?:"); break;
            case '}': regex.Append(')'); break;
            case ',': regex.Append('|'); break;
            case '[':
               var end = pattern.IndexOf(']', i + 1);
               if (end > i)
               {
                  regex.Append(pattern[i..(end + 1)]);
                  i = end;
               }
               else
               {
                  regex.Append(Regex.Escape(c.ToString()));
               }

               break;
            default:
               regex.Append(Regex.Escape(c.ToString()));
               break;
         }
      }

      regex.Append('$');
      return Regex.IsMatch(value, regex.ToString());
   }
}
