using System.Text;
using System.Text.Json.Nodes;

namespace Kamal.Secrets.Adapters;

/// <summary>
/// Google Cloud Secret Manager adapter, shelling out to the <c>gcloud</c> CLI.
/// Port of <c>Kamal::Secrets::Adapters::GcpSecretManager</c>.
///
/// The account option is used for both the user and service account impersonation:
/// <c>USER["|"DELEGATION_CHAIN]</c>, where USER is "default" or an email, and DELEGATION_CHAIN
/// is a comma-separated list of service account emails. Examples:
/// "my-user@example.com", "default", "default|my-service-user@example.com",
/// "my-user@example.com|svc1@example.com,svc2@example.com".
///
/// Secrets are "[project/]secret-name[/version]" where project defaults to "default"
/// (the configured gcloud project) and version defaults to "latest".
/// </summary>
public class GcpSecretManager : AdapterBase
{
   protected override string? Login(string? account)
   {
      if (!LoggedIn())
      {
         Run("gcloud auth login");

         if (!LoggedIn())
            throw new InvalidOperationException("could not login to gcloud");
      }

      return null;
   }

   protected override Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session)
   {
      var (user, serviceAccount) = ParseAccount(account!);
      var results = new Dictionary<string, string>();

      foreach (var (project, secretName, secretVersion) in SecretsWithMetadata(PrefixedSecrets(secrets, from)))
      {
         var itemName = $"{project}/{secretName}";
         results[itemName] = FetchSecret(project, secretName, secretVersion, user, serviceAccount, itemName);
      }

      return results;
   }

   protected override void CheckDependencies()
   {
      if (!Run("gcloud --version 2> /dev/null").Success)
         throw new InvalidOperationException("gcloud CLI is not installed");
   }

   private string FetchSecret(string project, string secretName, string secretVersion, string user, string? serviceAccount, string itemName)
   {
      var secret = RunGcloud(
         $"secrets versions access {Shellwords.Escape(secretVersion)} --secret={Shellwords.Escape(secretName)}",
         project, user, serviceAccount, itemName);

      var data = secret["payload"]!["data"]!.GetValue<string>();
      return Encoding.UTF8.GetString(Convert.FromBase64String(data));
   }

   private static List<(string Project, string Name, string Version)> SecretsWithMetadata(IEnumerable<string> secrets)
   {
      var items = new List<(string, string, string)>();

      foreach (var secret in secrets)
      {
         var parts = secret.Split('/').ToList();
         if (parts.Count == 1)
            parts.Insert(0, "default");

         var project = parts[0];
         var secretName = parts.Count > 1 ? parts[1] : "";
         var secretVersion = parts.Count > 2 ? parts[2] : "latest";

         items.Add((project, secretName, secretVersion));
      }

      return items;
   }

   private JsonNode RunGcloud(string command, string project, string user, string? serviceAccount, string itemName)
   {
      var fullCommand = new List<string> { "gcloud", command };

      if (project != "default")
         fullCommand.Add($"--project={Shellwords.Escape(project)}");

      if (user != "default")
         fullCommand.Add($"--account={Shellwords.Escape(user)}");

      if (serviceAccount != null)
         fullCommand.Add($"--impersonate-service-account={Shellwords.Escape(serviceAccount)}");

      fullCommand.Add("--format=json");

      var result = Run(string.Join(" ", fullCommand));
      if (!result.Success)
         throw new InvalidOperationException($"Could not read {itemName} from Google Secret Manager");

      return JsonNode.Parse(result.Stdout.Trim())!;
   }

   private bool LoggedIn()
   {
      return JsonNode.Parse(Run("gcloud auth list --format=json").Stdout) is JsonArray { Count: > 0 };
   }

   private static (string User, string? ServiceAccount) ParseAccount(string account)
   {
      var parts = account.Split('|', 2);
      return (parts[0], parts.Length > 1 ? parts[1] : null);
   }
}
