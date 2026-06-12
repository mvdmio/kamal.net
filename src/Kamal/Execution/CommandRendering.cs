using Kamal.Commands;
using Kamal.Utils;

namespace Kamal.Execution;

/// <summary>
/// Renders command token arrays for execution (unredacted) and for logging (redacted),
/// mirroring SSHKit's <c>Command#to_s</c> versus its redacted log output.
/// </summary>
public static class CommandRendering
{
   /// <summary>The command line that actually runs: <see cref="Sensitive"/> tokens render their real values.</summary>
   public static string Unredacted(params object?[] command) => CommandsBase.JoinTokens(command);

   /// <summary>The command line safe for logs: <see cref="IRedactable"/> tokens render their redaction.</summary>
   public static string Redacted(params object?[] command)
   {
      return string.Join(" ", CommandsBase.Flatten(command)
         .Select(token => token is IRedactable redactable ? redactable.Redaction : RubyHelpers.RubyToS(token)));
   }
}
