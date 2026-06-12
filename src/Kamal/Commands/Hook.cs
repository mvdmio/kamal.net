using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>
/// Port of <c>Kamal::Commands::Hook</c>: builds the hook script invocation and its env map
/// (the actual process invocation happens in the execution layer).
/// </summary>
public class Hook : CommandsBase
{
   public Hook(KamalConfiguration config) : base(config)
   {
   }

   public object[] Run(string hook) => [HookFile(hook)];

   /// <summary>Port of <c>env(secrets:, **details)</c>: the KAMAL_* env vars for a hook run.</summary>
   public OrderedDictionary<string, string> Env(bool secrets = false, params KeyValuePair<string, object?>[] details)
   {
      var env = Tags(details).Env;

      if (secrets)
      {
         foreach (var (key, value) in Config.Secrets.ToDictionary())
            env[key] = value;
      }

      return env;
   }

   public bool HookExists(string hook) => File.Exists(HookFile(hook));

   private string HookFile(string hook) => RubyHelpers.JoinPath(Config.HooksPath, hook);
}
