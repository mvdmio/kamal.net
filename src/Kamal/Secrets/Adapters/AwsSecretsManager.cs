using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kamal.Secrets.Adapters;

/// <summary>
/// AWS Secrets Manager adapter.
/// Port of <c>Kamal::Secrets::Adapters::AwsSecretsManager</c>. Like the Ruby original this shells
/// out to the <c>aws</c> CLI (<c>aws secretsmanager batch-get-secret-value</c>); no SDK is used.
/// </summary>
public class AwsSecretsManager : AdapterBase
{
   public override bool RequiresAccount => false;

   protected override string? Login(string? account)
   {
      return null;
   }

   protected override Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session)
   {
      var results = new Dictionary<string, string>();

      foreach (var secret in GetFromSecretsManager(PrefixedSecrets(secrets, from), account))
      {
         var secretName = secret!["Name"]!.GetValue<string>();
         var secretString = secret["SecretString"]!.GetValue<string>();

         JsonObject? parsed = null;
         try
         {
            parsed = JsonNode.Parse(secretString) as JsonObject;
         }
         catch (JsonException)
         {
            // Not JSON: fall through to the raw string below.
         }

         if (parsed != null)
         {
            foreach (var (key, value) in parsed)
               results[$"{secretName}/{key}"] = value is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var s) ? s : value?.ToJsonString() ?? "";
         }
         else
         {
            results[secretName] = secretString;
         }
      }

      return results;
   }

   protected override void CheckDependencies()
   {
      if (!Run("aws --version 2> /dev/null").Success)
         throw new InvalidOperationException("AWS CLI is not installed");
   }

   private JsonArray GetFromSecretsManager(IReadOnlyList<string> secrets, string? account)
   {
      var args = new List<string> { "aws", "secretsmanager", "batch-get-secret-value", "--secret-id-list" };
      args.AddRange(secrets.Select(Shellwords.Escape));

      if (account != null)
      {
         args.Add("--profile");
         args.Add(Shellwords.Escape(account));
      }

      args.Add("--output");
      args.Add("json");

      var result = Run(string.Join(" ", args));
      if (!result.Success)
         throw new InvalidOperationException($"Could not read {result.Stdout} from AWS Secrets Manager");

      var parsed = JsonNode.Parse(result.Stdout)!;

      if (parsed["Errors"] is JsonArray { Count: > 0 } errors)
      {
         throw new InvalidOperationException(string.Join(" ",
            errors.Select(error => $"{error!["SecretId"]?.GetValue<string>()}: {error["Message"]?.GetValue<string>()}")));
      }

      return parsed["SecretValues"]!.AsArray();
   }
}
