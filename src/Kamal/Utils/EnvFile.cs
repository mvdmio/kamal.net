using System.Text;

namespace Kamal.Utils;

/// <summary>
/// Port of <c>Kamal::EnvFile</c>: encodes an env mapping as a string where secret values have
/// been looked up and all values escaped for Docker env-file consumption.
/// </summary>
public sealed class EnvFile
{
   private readonly IEnumerable<KeyValuePair<string, object?>> _env;

   public EnvFile(IEnumerable<KeyValuePair<string, object?>> env)
   {
      _env = env;
   }

   public override string ToString()
   {
      var contents = new StringBuilder();

      foreach (var (key, value) in _env)
         contents.Append(key).Append('=').Append(EscapeDockerEnvFileValue(value)).Append('\n');

      // Ensure the file has some contents to avoid the SSHKIT empty file warning.
      return contents.Length > 0 ? contents.ToString() : "\n";
   }

   /// <summary>
   /// Escapes a value to make it safe to dump in a Docker env file: Ruby's <c>String#dump</c>
   /// without the surrounding quotes, with <c>\"</c> and <c>\#</c> unescaped again
   /// (double quotes are treated literally in Docker env files). Non-ASCII runs are kept as-is.
   /// </summary>
   public static string EscapeDockerEnvFileValue(object? value)
   {
      var str = RubyHelpers.RubyToS(value);
      var sb = new StringBuilder(str.Length);

      foreach (var c in str)
      {
         switch (c)
         {
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
            default:
               if (c <= 0x7F && (c < 0x20 || c == 0x7F))
                  sb.Append($"\\x{(int)c:X2}");
               else
                  sb.Append(c); // includes literal double quotes and #
               break;
         }
      }

      return sb.ToString();
   }
}
