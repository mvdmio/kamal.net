using System.Text.Json.Nodes;

namespace Kamal.Secrets.Adapters;

/// <summary>
/// Enpass adapter, shelling out to the <c>enpass-cli</c> CLI.
/// Enpass is different from most password managers, in that it's offline and doesn't need an account.
/// <c>--from</c> points at the vault path; secrets are item titles, optionally with a label
/// (e.g. <c>FooBar</c> or <c>FooBar/DB_PASSWORD</c>).
/// Port of <c>Kamal::Secrets::Adapters::Enpass</c>.
/// </summary>
public class Enpass : AdapterBase
{
   public override bool RequiresAccount => false;

   protected override string? Login(string? account)
   {
      return null;
   }

   protected override Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session)
   {
      var secretsTitles = FetchSecretTitles(secrets);

      var result = Run($"enpass-cli -json -vault {Shellwords.Escape(from ?? "")} show {string.Join(" ", secretsTitles.Select(Shellwords.Escape))}").Stdout.Trim();

      return ParseResultAndTakeSecrets(result, secrets);
   }

   protected override void CheckDependencies()
   {
      if (!Run("enpass-cli version 2> /dev/null").Success)
         throw new InvalidOperationException("Enpass CLI is not installed");
   }

   private static List<string> FetchSecretTitles(IEnumerable<string> secrets)
   {
      var titles = new List<string>();
      var seen = new HashSet<string>();

      foreach (var secret in secrets)
      {
         // Sometimes secrets contain a '/', when the intent is to fetch a single password for an item.
         // Example: FooBar/DB_PASSWORD. The other case is fetching all passwords for an item: FooBar.
         var separatorIndex = secret.LastIndexOf('/');
         var title = separatorIndex < 0 ? secret : secret[..separatorIndex];

         if (seen.Add(title))
            titles.Add(title);
      }

      return titles;
   }

   private static Dictionary<string, string> ParseResultAndTakeSecrets(string unparsedResult, IReadOnlyList<string> secrets)
   {
      var result = JsonNode.Parse(unparsedResult)!.AsArray();
      var secretsWithPasswords = new Dictionary<string, string>();

      foreach (var item in result)
      {
         var title = item!["title"]?.GetValue<string>();
         var label = item["label"]?.GetValue<string>();
         var password = item["password"]?.GetValue<string>();

         if (title == null || string.IsNullOrWhiteSpace(password))
            continue;

         var key = string.Join("/", new[] { title, label }.Where(part => !string.IsNullOrEmpty(part)));

         if (secrets.Contains(title) || secrets.Contains(key))
         {
            if (secretsWithPasswords.ContainsKey(key))
               throw new InvalidOperationException($"{key} is present more than once");

            secretsWithPasswords[key] = password;
         }
      }

      return secretsWithPasswords;
   }
}
