namespace Kamal.Secrets.Adapters;

/// <summary>
/// Adapter lookup. Port of <c>Kamal::Secrets::Adapters.lookup</c> (named SecretsAdapters to avoid
/// clashing with the Kamal.Secrets.Adapters namespace).
/// </summary>
public static class SecretsAdapters
{
   public static AdapterBase Lookup(string name)
   {
      name = name.ToLowerInvariant() switch
      {
         "1password" => "one_password",
         "lastpass" => "last_pass",
         "gcp" => "gcp_secret_manager",
         "bitwarden-sm" => "bitwarden_secrets_manager",
         _ => name
      };

      return name.ToLowerInvariant() switch
      {
         "one_password" => new OnePassword(),
         "last_pass" => new LastPass(),
         "bitwarden" => new Bitwarden(),
         "bitwarden_secrets_manager" => new BitwardenSecretsManager(),
         "doppler" => new Doppler(),
         "enpass" => new Enpass(),
         "gcp_secret_manager" => new GcpSecretManager(),
         "aws_secrets_manager" => new AwsSecretsManager(),
         "passbolt" => new Passbolt(),
         "test" => new Test(),
         _ => throw new InvalidOperationException($"Unknown secrets adapter: {name}")
      };
   }
}
