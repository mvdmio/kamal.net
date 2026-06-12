using System.Text.Json;
using Kamal.Secrets;
using Kamal.Secrets.Adapters;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::Secrets</c>.</summary>
public sealed class SecretsCli : CliBase
{
   public SecretsCli(CliContext context) : base(context)
   {
   }

   /// <summary>Port of <c>fetch [SECRETS...]</c>. Returns the value when <paramref name="inline"/> (for in-process substitution).</summary>
   public Task<string?> Fetch(string adapter, string[] secrets, string? account = null, string? from = null, bool inline = false)
   {
      var secretsAdapter = SecretsAdapters.Lookup(adapter);

      if (secretsAdapter.RequiresAccount && string.IsNullOrWhiteSpace(account))
      {
         Console.WriteLine("No value provided for required options '--account'");
         return Task.FromResult<string?>(null);
      }

      var results = secretsAdapter.Fetch(secrets, account: account, from: from);
      var json = JsonSerializer.Serialize(results);

      return Task.FromResult(ReturnOrPuts(inline ? Shellwords.Escape(json) : json, inline));
   }

   /// <summary>Port of <c>extract FIELD SECRETS_JSON</c>.</summary>
   public Task<string?> Extract(string name, string secrets, bool inline = false)
   {
      var parsed = JsonSerializer.Deserialize<Dictionary<string, string?>>(secrets) ?? new Dictionary<string, string?>();

      string? value = null;

      if (parsed.TryGetValue(name, out var direct))
         value = direct;
      else if (parsed.FirstOrDefault(pair => pair.Key.EndsWith($"/{name}", StringComparison.Ordinal)) is { Key: not null } match)
         value = match.Value;

      if (value is null)
         throw new InvalidOperationException($"Could not find secret {name}");

      return Task.FromResult(ReturnOrPuts(value, inline));
   }

   /// <summary>Port of <c>print</c>.</summary>
   public Task Print()
   {
      foreach (var (key, value) in KAMAL.Config.Secrets.ToDictionary())
         Console.WriteLine($"{key}={value}");

      return Task.CompletedTask;
   }

   private static string? ReturnOrPuts(string value, bool inline)
   {
      if (inline)
         return value;

      Console.WriteLine(value);
      return null;
   }

   // ----- In-process `$(kamal secrets ...)` substitution ---------------------------------------

   /// <summary>
   /// Handler for <see cref="Kamal.Secrets.Dotenv.InlineCommandSubstitution.KamalSecretsCommandHandler"/>:
   /// receives the shell-split arguments after "kamal" (with "--inline" appended) and runs the
   /// secrets command in-process, returning its output (Ruby re-enters <c>Kamal::Cli::Main.start</c>).
   /// </summary>
   public static string HandleInline(string[] args)
   {
      if (args.Length == 0 || args[0] != "secrets")
         throw new ArgumentException($"Cannot inline command: kamal {string.Join(" ", args)}");

      var context = new CliContext(new CliOptions(), "secrets", args.Length > 1 ? args[1] : null);
      var cli = new SecretsCli(context);

      string? adapter = null, account = null, from = null;
      var positional = new List<string>();

      for (var i = 2; i < args.Length; i++)
      {
         switch (args[i])
         {
            case "--adapter" or "-a":
               adapter = NextValue(args, ref i, "--adapter");
               break;
            case "--account":
               account = NextValue(args, ref i, "--account");
               break;
            case "--from":
               from = NextValue(args, ref i, "--from");
               break;
            case "--inline":
               break;
            default:
               positional.Add(args[i]);
               break;
         }
      }

      switch (args.Length > 1 ? args[1] : null)
      {
         case "fetch":
            if (adapter is null)
               throw new ArgumentException("No value provided for required options '--adapter'");

            return cli.Fetch(adapter, positional.ToArray(), account: account, from: from, inline: true).GetAwaiter().GetResult() ?? "";

         case "extract":
            if (positional.Count < 2)
               throw new ArgumentException("kamal secrets extract requires FIELD and SECRETS arguments");

            return cli.Extract(positional[0], positional[1], inline: true).GetAwaiter().GetResult() ?? "";

         default:
            throw new ArgumentException($"Cannot inline command: kamal {string.Join(" ", args)}");
      }
   }

   private static string NextValue(string[] args, ref int index, string option)
   {
      if (index + 1 >= args.Length)
         throw new ArgumentException($"No value provided for option '{option}'");

      return args[++index];
   }
}
