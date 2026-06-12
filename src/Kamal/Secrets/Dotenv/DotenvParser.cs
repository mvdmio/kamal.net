using System.Text.RegularExpressions;

namespace Kamal.Secrets.Dotenv;

/// <summary>
/// Parses the .env file format into key/value pairs.
/// Port of Ruby dotenv 3.x's <c>Dotenv::Parser</c> with Kamal's
/// <see cref="InlineCommandSubstitution"/> installed in place of stock command substitution.
///
/// Supported syntax (matching Ruby dotenv semantics):
/// - KEY=value lines, optional "export " prefix, ":" separator (KEY: value), trailing # comments
/// - single-quoted values (literal, no unescaping or substitution)
/// - double-quoted values, including multiline values; "\n"/"\r" stay literal unless
///   DOTENV_LINEBREAK_MODE=legacy is set (in the file or the environment)
/// - backslash unescaping for unquoted and double-quoted values (except "\$", which escapes substitution)
/// - $VAR / ${VAR} variable expansion (parsed values win over the process environment) and
///   $(command) command substitution
/// - "export KEY" without a value raises <see cref="DotenvFormatException"/> unless KEY was already defined
/// </summary>
public static class DotenvParser
{
   private static readonly Regex LinePattern = new(
      @"(?:^|\A)\s*(?<export>export\s+)?(?<key>[A-Za-z0-9_.]+)(?:(?:\s*=\s*?|:\s+?)(?<value>\s*'(?:\\'|[^'])*'|\s*""(?:\\""|[^""])*""|[^#\n]+)?)?\s*(?:\#.*)?(?:$|\z)",
      RegexOptions.Multiline | RegexOptions.Compiled);

   private static readonly Regex QuotedStringPattern = new(
      @"\A(['""])(.*)\1\z",
      RegexOptions.Singleline | RegexOptions.Compiled);

   /// <summary>
   /// Parses .env file content into a dictionary.
   /// </summary>
   /// <param name="contents">The raw file content.</param>
   /// <param name="overwrite">
   /// When false (Ruby dotenv default), keys already present in the process environment keep their
   /// environment value. When true (used for Kamal secrets files), file values win.
   /// </param>
   /// <param name="interpolate">
   /// When true, performs $VAR/${VAR} variable expansion and $(command) command substitution.
   /// When false, values are still unquoted/unescaped but substitutions are left verbatim.
   /// </param>
   public static Dictionary<string, string> Parse(string contents, bool overwrite = false, bool interpolate = true)
   {
      // Convert line breaks to the same format.
      var content = contents.Replace("\r\n", "\n").Replace('\r', '\n');
      var hash = new Dictionary<string, string>();

      foreach (Match match in LinePattern.Matches(content))
      {
         var key = match.Groups["key"].Value;

         if (Existing(key, overwrite))
         {
            // Use value from the already defined environment variable.
            hash[key] = Environment.GetEnvironmentVariable(key)!;
         }
         else if (match.Groups["export"].Success && !match.Groups["value"].Success)
         {
            // Check for exported variable with no value.
            if (!hash.ContainsKey(key))
               throw new DotenvFormatException($"Line {RubyString.Inspect(match.Value)} has an unset variable");
         }
         else
         {
            hash[key] = ParseValue(match.Groups["value"].Success ? match.Groups["value"].Value : "", hash, interpolate);
         }
      }

      return hash;
   }

   /// <summary>
   /// Reads and parses a .env format file. See <see cref="Parse"/>.
   /// </summary>
   public static Dictionary<string, string> ParseFile(string path, bool overwrite = false, bool interpolate = true)
   {
      return Parse(File.ReadAllText(path), overwrite, interpolate);
   }

   private static bool Existing(string key, bool overwrite)
   {
      return !overwrite
         && key != "DOTENV_LINEBREAK_MODE"
         && Environment.GetEnvironmentVariable(key) != null;
   }

   private static string ParseValue(string value, Dictionary<string, string> hash, bool interpolate)
   {
      // Remove surrounding quotes.
      value = value.Trim();
      char? maybeQuote = null;

      var quoted = QuotedStringPattern.Match(value);
      if (quoted.Success)
      {
         maybeQuote = quoted.Groups[1].Value[0];
         value = quoted.Groups[2].Value;
      }

      // Expand new lines in double quoted values.
      if (maybeQuote == '"')
         value = ExpandNewlines(value, hash);

      // Unescape characters and perform substitutions unless value is single quoted.
      if (maybeQuote != '\'')
      {
         value = UnescapeCharacters(value);

         if (interpolate)
         {
            value = InlineCommandSubstitution.Substitute(value, hash);
            value = VariableSubstitution.Substitute(value, hash);
         }
      }

      return value;
   }

   private static string UnescapeCharacters(string value)
   {
      // Remove the backslash before any character except '$' (kept so \$ can escape substitution).
      return Regex.Replace(value, @"\\([^$])", "$1");
   }

   private static string ExpandNewlines(string value, Dictionary<string, string> hash)
   {
      var mode = hash.TryGetValue("DOTENV_LINEBREAK_MODE", out var fromFile)
         ? fromFile
         : Environment.GetEnvironmentVariable("DOTENV_LINEBREAK_MODE");

      if (mode == "legacy")
         return value.Replace("\\n", "\n").Replace("\\r", "\r");

      // Double the backslash so the subsequent unescape pass leaves a literal \n / \r.
      return value.Replace("\\n", "\\\\n").Replace("\\r", "\\\\r");
   }
}
