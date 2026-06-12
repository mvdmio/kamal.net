using System.Text.Json.Nodes;

namespace Kamal.Secrets.Adapters;

/// <summary>
/// LastPass adapter, shelling out to the <c>lpass</c> CLI.
/// Port of <c>Kamal::Secrets::Adapters::LastPass</c>.
/// </summary>
public class LastPass : AdapterBase
{
   protected override string? Login(string? account)
   {
      if (!LoggedIn(account!))
      {
         if (!Run($"lpass login {Shellwords.Escape(account!)}").Success)
            throw new InvalidOperationException("Failed to login to LastPass");
      }

      return null;
   }

   protected override Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session)
   {
      var prefixedSecrets = PrefixedSecrets(secrets, from);

      var result = Run($"lpass show {string.Join(" ", prefixedSecrets.Select(Shellwords.Escape))} --json");
      if (!result.Success)
         throw new InvalidOperationException($"Could not read {RubyString.InspectList(prefixedSecrets)} from LastPass");

      var items = JsonNode.Parse(result.Stdout)!.AsArray();

      var results = new Dictionary<string, string>();
      foreach (var item in items)
         results[item!["fullname"]!.GetValue<string>()] = item["password"]?.GetValue<string>() ?? "";

      var missingItems = prefixedSecrets.Where(secret => !results.ContainsKey(secret)).ToList();
      if (missingItems.Count > 0)
         throw new InvalidOperationException($"Could not find {string.Join(", ", missingItems)} in LastPass");

      return results;
   }

   protected override void CheckDependencies()
   {
      if (!Run("lpass --version 2> /dev/null").Success)
         throw new InvalidOperationException("LastPass CLI is not installed");
   }

   private bool LoggedIn(string account)
   {
      return Run("lpass status --color never").Stdout.Trim() == $"Logged in as {account}.";
   }
}
