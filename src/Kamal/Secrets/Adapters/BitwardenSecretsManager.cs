using System.Text.Json.Nodes;

namespace Kamal.Secrets.Adapters;

/// <summary>
/// Bitwarden Secrets Manager adapter, shelling out to the <c>bws</c> CLI.
/// Port of <c>Kamal::Secrets::Adapters::BitwardenSecretsManager</c>.
/// </summary>
public class BitwardenSecretsManager : AdapterBase
{
   private const string ListAllSelector = "all";
   private const string ListAllFromProjectSuffix = "/all";
   private const string ListCommand = "secret list";
   private const string GetCommand = "secret get";

   public override bool RequiresAccount => false;

   protected override string? Login(string? account)
   {
      if (!RunBws("project list").Success)
         throw new InvalidOperationException("Could not authenticate to Bitwarden Secrets Manager. Did you set a valid access token?");

      return null;
   }

   protected override Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session)
   {
      if (secrets.Count == 0)
         throw new InvalidOperationException("You must specify what to retrieve from Bitwarden Secrets Manager");

      var prefixedSecrets = PrefixedSecrets(secrets, from);
      var command = ExtractCommand(prefixedSecrets);

      var results = new Dictionary<string, string>();

      if (command == null)
      {
         foreach (var secretUuid in prefixedSecrets)
         {
            var result = RunBws($"{GetCommand} {Shellwords.Escape(secretUuid)}");
            if (!result.Success)
               throw new InvalidOperationException($"Could not read {secretUuid} from Bitwarden Secrets Manager");

            var itemJson = JsonNode.Parse(result.Stdout)!;
            results[itemJson["key"]!.GetValue<string>()] = itemJson["value"]!.GetValue<string>();
         }
      }
      else
      {
         var result = RunBws(command);
         if (!result.Success)
            throw new InvalidOperationException("Could not read secrets from Bitwarden Secrets Manager");

         foreach (var itemJson in JsonNode.Parse(result.Stdout)!.AsArray())
            results[itemJson!["key"]!.GetValue<string>()] = itemJson["value"]!.GetValue<string>();
      }

      return results;
   }

   protected override void CheckDependencies()
   {
      if (!Run("bws --version 2> /dev/null").Success)
         throw new InvalidOperationException("Bitwarden Secrets Manager CLI is not installed");
   }

   private static string? ExtractCommand(IReadOnlyList<string> secrets)
   {
      if (secrets.Count != 1)
         return null;

      if (secrets[0] == ListAllSelector)
         return ListCommand;

      if (secrets[0].EndsWith(ListAllFromProjectSuffix, StringComparison.Ordinal))
      {
         var project = secrets[0].Split(ListAllFromProjectSuffix)[0];
         return $"{ListCommand} {Shellwords.Escape(project)}";
      }

      return null;
   }

   private ShellResult RunBws(string command)
   {
      return Run($"bws {command}");
   }
}
