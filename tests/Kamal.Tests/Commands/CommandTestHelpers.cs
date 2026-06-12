using System.Collections;
using System.Globalization;
using Kamal.Commands;
using Kamal.Utils;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Commands;

/// <summary>
/// Test helpers for the commands tests: flattens and joins a command token array the way
/// SSHKit does (Sensitive values rendered unredacted), mirroring the Ruby tests' <c>.join(" ")</c>.
/// </summary>
public static class Cmd
{
   public static string Join(IEnumerable<object?>? command)
   {
      if (command is null)
         return "";

      var tokens = new List<string>();
      AddFlattened(tokens, command);

      return string.Join(" ", tokens);
   }

   /// <summary>Builds a KeyValuePair for the params-details command APIs (Ruby keyword args).</summary>
   public static KeyValuePair<string, object?> KV(string key, object? value) => new(key, value);

   /// <summary>Recursive merge mirroring ActiveSupport's <c>deep_merge</c> for test config overrides.</summary>
   public static Cfg DeepMerge(Cfg baseDict, Cfg overrideDict)
   {
      var result = new Cfg();

      foreach (var (key, value) in baseDict)
         result[key] = value;

      foreach (var (key, value) in overrideDict)
      {
         if (result.TryGetValue(key, out var existing) && existing is Cfg existingDict && value is Cfg valueDict)
            result[key] = DeepMerge(existingDict, valueDict);
         else
            result[key] = value;
      }

      return result;
   }

   private static void AddFlattened(List<string> tokens, object? token)
   {
      switch (token)
      {
         case null:
            return;
         case string s:
            tokens.Add(s);
            return;
         case Sensitive sensitive:
            tokens.Add(sensitive.Unredacted);
            return;
         case bool b:
            tokens.Add(b ? "true" : "false");
            return;
         case IEnumerable enumerable:
            foreach (var item in enumerable)
               AddFlattened(tokens, item);
            return;
         case IFormattable formattable:
            tokens.Add(formattable.ToString(null, CultureInfo.InvariantCulture));
            return;
         default:
            tokens.Add(token.ToString() ?? "");
            return;
      }
   }
}

/// <summary>Stubs <see cref="CommandsBase.StdinTty"/> (Ruby's <c>stub_stdin_tty</c> / <c>stub_stdin_file</c>).</summary>
public sealed class StdinScope : IDisposable
{
   private readonly Func<bool> _original;

   public StdinScope(bool tty)
   {
      _original = CommandsBase.StdinTty;
      CommandsBase.StdinTty = () => tty;
   }

   public void Dispose()
   {
      CommandsBase.StdinTty = _original;
   }
}

/// <summary>Stubs the Dockerfile existence check (Ruby tests stub <c>Pathname#exist?</c>).</summary>
public sealed class DockerfileScope : IDisposable
{
   private readonly Func<string, bool>? _original;

   public DockerfileScope(bool exists)
   {
      _original = Builder.Base.DockerfileExists;
      Builder.Base.DockerfileExists = _ => exists;
   }

   public void Dispose()
   {
      Builder.Base.DockerfileExists = _original;
   }
}

/// <summary>Stubs <see cref="KamalUtils.DockerArchOverride"/> for builder tests.</summary>
public sealed class DockerArchScope : IDisposable
{
   private readonly string? _original;

   public DockerArchScope(string arch)
   {
      _original = KamalUtils.DockerArchOverride;
      KamalUtils.DockerArchOverride = arch;
   }

   public void Dispose()
   {
      KamalUtils.DockerArchOverride = _original;
   }
}
