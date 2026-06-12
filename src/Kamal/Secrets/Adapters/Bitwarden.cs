using System.Text.Json.Nodes;

namespace Kamal.Secrets.Adapters;

/// <summary>
/// Bitwarden adapter, shelling out to the <c>bw</c> CLI.
/// Port of <c>Kamal::Secrets::Adapters::Bitwarden</c>.
/// </summary>
public class Bitwarden : AdapterBase
{
   protected override string? Login(string? account)
   {
      string? session = null;

      var status = StatusOf(RunBw("status"));

      if (status == "unauthenticated")
      {
         RunBw($"login {Shellwords.Escape(account!)}");
         status = StatusOf(RunBw("status"));
      }

      if (status == "locked")
      {
         session = Presence(RunBw("unlock --raw").Stdout.Trim());
         status = StatusOf(RunBw("status", session));
      }

      if (status != "unlocked")
         throw new InvalidOperationException("Failed to login to and unlock Bitwarden");

      if (!RunBw("sync", session).Success)
         throw new InvalidOperationException("Failed to sync Bitwarden");

      return session;
   }

   protected override Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session)
   {
      var results = new Dictionary<string, string>();

      foreach (var (item, fields) in ItemsFields(PrefixedSecrets(secrets, from)))
      {
         var result = RunBw($"get item {Shellwords.Escape(item)}", session);
         if (!result.Success)
            throw new InvalidOperationException($"Could not read {item} from Bitwarden");

         var itemJson = JsonNode.Parse(result.Stdout.Trim())!;

         if (fields.Any(field => field != null))
         {
            foreach (var (key, value) in FetchSecretsFromFields(fields, item, itemJson))
               results[key] = value;
         }
         else if (itemJson["login"]?["password"] is { } password)
         {
            results[item] = password.GetValue<string>();
         }
         else if (itemJson["fields"] is JsonArray { Count: > 0 } itemFields)
         {
            var fieldNames = itemFields.Select(field => field!["name"]?.GetValue<string>()).ToList();
            foreach (var (key, value) in FetchSecretsFromFields(fieldNames, item, itemJson))
               results[key] = value;
         }
         else
         {
            throw new InvalidOperationException($"Item {item} is not a login type item and no fields were specified");
         }
      }

      return results;
   }

   protected override void CheckDependencies()
   {
      if (!Run("bw --version 2> /dev/null").Success)
         throw new InvalidOperationException("Bitwarden CLI is not installed");
   }

   private static Dictionary<string, string> FetchSecretsFromFields(IEnumerable<string?> fields, string item, JsonNode itemJson)
   {
      var results = new Dictionary<string, string>();

      foreach (var field in fields)
      {
         var itemField = (itemJson["fields"] as JsonArray)?.FirstOrDefault(f => f?["name"]?.GetValue<string>() == field);
         if (itemField == null)
            throw new InvalidOperationException($"Could not find field {field} in item {item} in Bitwarden");

         results[$"{item}/{field}"] = itemField["value"]?.GetValue<string>() ?? "";
      }

      return results;
   }

   private static Dictionary<string, List<string?>> ItemsFields(IEnumerable<string> secrets)
   {
      var items = new Dictionary<string, List<string?>>();

      foreach (var secret in secrets)
      {
         var parts = secret.Split('/');
         var item = parts[0];
         var field = parts.Length > 1 ? parts[1] : null;

         if (!items.TryGetValue(item, out var fields))
            items[item] = fields = new List<string?>();

         fields.Add(field);
      }

      return items;
   }

   private ShellResult RunBw(string command, string? session = null)
   {
      var prefix = session != null ? $"BW_SESSION={Shellwords.Escape(session)} " : "";
      return Run($"{prefix}bw {command}");
   }

   private static string? StatusOf(ShellResult result)
   {
      return JsonNode.Parse(result.Stdout.Trim())?["status"]?.GetValue<string>();
   }
}
