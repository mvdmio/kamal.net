using System.Text.RegularExpressions;

namespace Kamal.Secrets.Dotenv;

/// <summary>
/// Substitutes <c>$VAR</c> / <c>${VAR}</c> references in a value.
/// Port of Ruby dotenv's <c>Dotenv::Substitutions::Variable</c> (dotenv 3.x semantics):
/// the in-progress parsed hash wins over the process environment, missing variables become "".
/// A leading backslash escapes the substitution (<c>\$VAR</c> becomes <c>$VAR</c>).
/// </summary>
public static class VariableSubstitution
{
   private static readonly Regex VariablePattern = new(
      @"(\\)?(\$)(?!\()\{?([A-Z0-9_]+)?\}?",
      RegexOptions.IgnoreCase | RegexOptions.Compiled);

   public static string Substitute(string value, IReadOnlyDictionary<string, string>? env)
   {
      return VariablePattern.Replace(value, match =>
      {
         if (match.Groups[1].Success)
         {
            // Variable is escaped with a backslash: drop the backslash, keep the rest.
            return match.Value[1..];
         }

         if (match.Groups[3].Success)
         {
            var name = match.Groups[3].Value;

            if (env != null && env.TryGetValue(name, out var fromEnv))
               return fromEnv;

            return Environment.GetEnvironmentVariable(name) ?? "";
         }

         // A lone "$" (or "${}"): leave as-is.
         return match.Value;
      });
   }
}
