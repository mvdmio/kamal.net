using System.Text.Json.Nodes;

namespace Kamal.Secrets.Adapters;

/// <summary>
/// Doppler adapter, shelling out to the <c>doppler</c> CLI.
/// Port of <c>Kamal::Secrets::Adapters::Doppler</c>.
/// </summary>
public class Doppler : AdapterBase
{
   public override bool RequiresAccount => false;

   protected override string? Login(string? account)
   {
      if (!LoggedIn())
      {
         if (!Run("doppler login -y").Success)
            throw new InvalidOperationException("Failed to login to Doppler");
      }

      return null;
   }

   protected override Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session)
   {
      var prefixedSecrets = PrefixedSecrets(secrets, from);
      var flags = SecretsGetFlags(prefixedSecrets);

      var secretNames = prefixedSecrets.Select(secret => secret.Split('/').Last());

      var result = Run($"doppler secrets get {string.Join(" ", secretNames.Select(Shellwords.Escape))} --json {flags}");
      if (!result.Success)
         throw new InvalidOperationException($"Could not read {RubyString.InspectList(prefixedSecrets)} from Doppler");

      var items = JsonNode.Parse(result.Stdout)!.AsObject();

      return items.ToDictionary(
         item => item.Key,
         item => item.Value?["computed"]?.GetValue<string>() ?? "");
   }

   protected override void CheckDependencies()
   {
      if (!Run("doppler --version 2> /dev/null").Success)
         throw new InvalidOperationException("Doppler CLI is not installed");
   }

   private bool LoggedIn()
   {
      return Run("doppler me --json 2> /dev/null").Success;
   }

   private static string SecretsGetFlags(IReadOnlyList<string> secrets)
   {
      if (ServiceTokenSet())
         return "";

      var parts = secrets[0].Split('/');
      var project = parts.Length > 0 ? Presence(parts[0]) : null;
      var config = parts.Length > 1 ? Presence(parts[1]) : null;

      if (project == null || config == null)
         throw new InvalidOperationException("Missing project or config from '--from=project/config' option");

      return $"-p {Shellwords.Escape(project)} -c {Shellwords.Escape(config)}";
   }

   private static bool ServiceTokenSet()
   {
      var token = Environment.GetEnvironmentVariable("DOPPLER_TOKEN");
      return token != null && token.StartsWith("dp.st", StringComparison.Ordinal);
   }
}
