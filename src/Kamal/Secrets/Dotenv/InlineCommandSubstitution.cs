using System.Text;
using System.Text.RegularExpressions;

namespace Kamal.Secrets.Dotenv;

/// <summary>
/// Substitutes <c>$(command)</c> in a value with the command's output.
/// Port of <c>Kamal::Secrets::Dotenv::InlineCommandSubstitution</c>, which replaces Ruby dotenv's
/// command substitution: unlike stock dotenv it does not treat backslash-escaped parentheses as
/// delimiters inside the command, and it inlines <c>kamal secrets ...</c> commands instead of
/// shelling out to a new kamal process.
/// A leading backslash escapes the substitution (<c>\$(cmd)</c> becomes <c>$(cmd)</c>).
/// </summary>
public static class InlineCommandSubstitution
{
   private static readonly Regex KamalSecretsCommandPattern = new(@"\A\s*kamal\s*secrets\s+", RegexOptions.Compiled);

   /// <summary>
   /// Handler invoked for inlined <c>kamal secrets ...</c> commands. Receives the shell-split
   /// arguments after "kamal" with "--inline" appended (mirroring Ruby's
   /// <c>Kamal::Cli::Main.start(command.shellsplit[1..] + ["--inline"])</c>) and returns the output.
   /// The CLI layer installs this when it is available; when null, the command falls back to
   /// being executed through the shell like any other command (deviation from Ruby, which always
   /// has the CLI loaded in-process).
   /// </summary>
   public static Func<string[], string>? KamalSecretsCommandHandler { get; set; }

   /// <summary>
   /// Executes a non-kamal command and returns its stdout. Defaults to running through the OS
   /// shell via <see cref="ShellRunner"/> (Ruby backticks); replaceable for testing.
   /// </summary>
   public static Func<string, string>? CommandExecutor { get; set; }

   public static string Substitute(string value, IReadOnlyDictionary<string, string>? env)
   {
      var sb = new StringBuilder(value.Length);
      var i = 0;

      while (i < value.Length)
      {
         var j = value.IndexOf("$(", i, StringComparison.Ordinal);
         if (j < 0)
         {
            sb.Append(value, i, value.Length - i);
            break;
         }

         // Find the matching close paren, honoring nesting and backslash-escaped characters.
         var k = j + 2;
         var depth = 1;
         var closed = false;
         while (k < value.Length)
         {
            var c = value[k];
            if (c == '\\' && k + 1 < value.Length)
            {
               k += 2;
               continue;
            }

            if (c == '(')
            {
               depth++;
            }
            else if (c == ')')
            {
               depth--;
               if (depth == 0)
               {
                  closed = true;
                  break;
               }
            }

            k++;
         }

         var contentLength = k - (j + 2);
         if (!closed || contentLength == 0)
         {
            // Not a valid command substitution; emit up to and including the '$' and keep scanning.
            sb.Append(value, i, j + 1 - i);
            i = j + 1;
            continue;
         }

         var escaped = j > 0 && value[j - 1] == '\\';
         var matchStart = escaped ? j - 1 : j;
         sb.Append(value, i, matchStart - i);

         if (escaped)
         {
            // Command is escaped: don't replace it, just drop the backslash.
            sb.Append(value, j, k - j + 1);
         }
         else
         {
            var command = value.Substring(j + 2, contentLength);
            command = VariableSubstitution.Substitute(command, env);

            sb.Append(KamalSecretsCommandPattern.IsMatch(command)
               ? InlineSecretsCommand(command)
               : RubyString.Chomp(ExecuteCommand(command)));
         }

         i = k + 1;
      }

      return sb.ToString();
   }

   private static string InlineSecretsCommand(string command)
   {
      var handler = KamalSecretsCommandHandler;

      if (handler == null)
      {
         // No CLI layer registered: run the command through the shell like any other command.
         return RubyString.Chomp(ExecuteCommand(command));
      }

      var args = Shellwords.Split(command).Skip(1).Append("--inline").ToArray();
      return RubyString.Chomp(handler(args));
   }

   private static string ExecuteCommand(string command)
   {
      var executor = CommandExecutor;
      return executor != null ? executor(command) : ShellRunner.Run(command).Stdout;
   }
}
