using System.Text.Json.Nodes;
using Kamal.Utils;

namespace Kamal.Secrets.Adapters;

/// <summary>
/// 1Password adapter, shelling out to the <c>op</c> CLI.
/// Port of <c>Kamal::Secrets::Adapters::OnePassword</c>.
/// </summary>
public class OnePassword : AdapterBase
{
   protected override string? Login(string? account)
   {
      if (LoggedIn(account!))
         return null;

      var result = Run($"op signin {ToOptions(("account", account), ("force", true), ("raw", true))}");
      if (!result.Success)
         throw new InvalidOperationException("Failed to login to 1Password");

      return result.Stdout;
   }

   protected override Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session)
   {
      return secrets.Count == 0
         ? FetchAllSecrets(from!, account!, session)
         : FetchSpecifiedSecrets(secrets, from, account!, session);
   }

   protected override void CheckDependencies()
   {
      if (!Run("op --version 2> /dev/null").Success)
         throw new InvalidOperationException("1Password CLI is not installed");
   }

   private bool LoggedIn(string account)
   {
      return Run($"op account get --account {Shellwords.Escape(account)} 2> /dev/null").Success;
   }

   private Dictionary<string, string> FetchSpecifiedSecrets(IReadOnlyList<string> secrets, string? from, string account, string? session)
   {
      var results = new Dictionary<string, string>();

      foreach (var (vault, items) in VaultsItemsFields(PrefixedSecrets(secrets, from)))
      {
         foreach (var (item, fields) in items)
         {
            var fieldsJson = JsonNode.Parse(OpItemGet(vault, item, fields, account, session));
            var fieldsArray = fields.Count == 1 ? new JsonArray(fieldsJson) : fieldsJson!.AsArray();

            foreach (var (key, value) in FieldsMap(fieldsArray))
               results[key] = value;
         }
      }

      return results;
   }

   private Dictionary<string, string> FetchAllSecrets(string from, string account, string? session)
   {
      var results = new Dictionary<string, string>();

      foreach (var (vault, items) in VaultItems(from))
      {
         foreach (var item in items)
         {
            var fieldsArray = JsonNode.Parse(OpItemGet(vault, item, null, account, session))!["fields"]!.AsArray();

            foreach (var (key, value) in FieldsMap(fieldsArray))
               results[key] = value;
         }
      }

      return results;
   }

   private static Dictionary<string, Dictionary<string, List<string>>> VaultsItemsFields(IEnumerable<string> secrets)
   {
      var vaults = new Dictionary<string, Dictionary<string, List<string>>>();

      foreach (var rawSecret in secrets)
      {
         var secret = DeletePrefix(rawSecret, "op://");
         var parts = secret.Split('/');
         var vault = parts[0];
         var item = parts.Length > 1 ? parts[1] : "";
         var fields = parts.Skip(2).ToList();
         if (fields.Count == 0)
            fields.Add("password");

         if (!vaults.TryGetValue(vault, out var items))
            vaults[vault] = items = new Dictionary<string, List<string>>();

         if (!items.TryGetValue(item, out var itemFields))
            items[item] = itemFields = new List<string>();

         itemFields.Add(string.Join(".", fields));
      }

      return vaults;
   }

   private static Dictionary<string, List<string>> VaultItems(string from)
   {
      var parts = DeletePrefix(from, "op://").Split('/');
      var vault = parts[0];
      var item = parts.Length > 1 ? parts[1] : "";

      return new Dictionary<string, List<string>> { [vault] = [item] };
   }

   private static Dictionary<string, string> FieldsMap(JsonArray fieldsJson)
   {
      var results = new Dictionary<string, string>();

      foreach (var fieldJson in fieldsJson)
      {
         // The reference is in the form `op://vault/item/field[/field]`
         var reference = fieldJson!["reference"]!.GetValue<string>();
         var field = DeleteSuffix(DeletePrefix(reference, "op://"), "/password");
         results[field] = fieldJson["value"]?.GetValue<string>() ?? "";
      }

      return results;
   }

   private string OpItemGet(string vault, string item, IReadOnlyList<string>? fields, string account, string? session)
   {
      var options = new List<(string Key, object? Value)>
      {
         ("vault", vault),
         ("format", "json"),
         ("account", account),
         ("session", Presence(session))
      };

      if (fields is { Count: > 0 })
         options.Add(("fields", string.Join(",", fields.Select(field => $"label={field}"))));

      var result = Run($"op item get {Shellwords.Escape(item)} {ToOptions(options.ToArray())}");
      if (!result.Success)
      {
         var fieldsPart = fields is { Count: > 0 } ? $"{string.Join(", ", fields)} " : "";
         throw new InvalidOperationException($"Could not read {fieldsPart}from {item} in the {vault} 1Password vault");
      }

      return result.Stdout;
   }

   private static string ToOptions(params (string Key, object? Value)[] options)
   {
      // Port of Kamal::Utils.optionize on a compacted hash.
      var parts = new List<string>();

      foreach (var (key, value) in options)
      {
         if (value == null)
            continue;

         if (value is true)
            parts.Add($"--{key}");
         else
            parts.Add($"--{key} {KamalUtils.EscapeShellValue(value)}");
      }

      return string.Join(" ", parts);
   }

   private static string DeletePrefix(string value, string prefix)
   {
      return value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
   }

   private static string DeleteSuffix(string value, string suffix)
   {
      return value.EndsWith(suffix, StringComparison.Ordinal) ? value[..^suffix.Length] : value;
   }
}
