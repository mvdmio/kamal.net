using System.Text;

namespace Kamal.Secrets;

/// <summary>
/// Helpers replicating Ruby string semantics that the secrets layer depends on
/// (<c>String#inspect</c>/<c>String#dump</c> style quoting and <c>String#chomp</c>).
/// </summary>
public static class RubyString
{
   /// <summary>
   /// Approximates Ruby's <c>String#inspect</c>/<c>String#dump</c> for the strings used in shell
   /// filters and error messages: wraps in double quotes and escapes quotes, backslashes and
   /// control characters. Non-ASCII characters pass through unescaped (like inspect).
   /// </summary>
   public static string Inspect(string value)
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
            case '\u001B': sb.Append("\\e"); break;
            case '#':
               // Ruby escapes '#' when it would start an interpolation: #{, #$, #@
               if (i + 1 < value.Length && (value[i + 1] == '{' || value[i + 1] == '$' || value[i + 1] == '@'))
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

      return sb.Append('"').ToString();
   }

   /// <summary>
   /// Ruby's <c>Array#inspect</c> for a list of strings, e.g. <c>["a", "b"]</c>.
   /// Used to replicate error messages that interpolate Ruby arrays.
   /// </summary>
   public static string InspectList(IEnumerable<string> values)
   {
      return "[" + string.Join(", ", values.Select(Inspect)) + "]";
   }

   /// <summary>
   /// Ruby's <c>String#chomp</c>: removes a single trailing "\r\n", "\n" or "\r".
   /// </summary>
   public static string Chomp(string value)
   {
      if (value.EndsWith("\r\n", StringComparison.Ordinal))
         return value[..^2];

      if (value.Length > 0 && (value[^1] == '\n' || value[^1] == '\r'))
         return value[..^1];

      return value;
   }
}
