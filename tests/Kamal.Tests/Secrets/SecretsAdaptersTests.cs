using Kamal.Secrets.Adapters;

namespace Kamal.Tests.Secrets;

public class SecretsAdaptersTests
{
   [Theory]
   [InlineData("1password", typeof(OnePassword))]
   [InlineData("one_password", typeof(OnePassword))]
   [InlineData("lastpass", typeof(LastPass))]
   [InlineData("last_pass", typeof(LastPass))]
   [InlineData("bitwarden", typeof(Bitwarden))]
   [InlineData("bitwarden-sm", typeof(BitwardenSecretsManager))]
   [InlineData("bitwarden_secrets_manager", typeof(BitwardenSecretsManager))]
   [InlineData("doppler", typeof(Doppler))]
   [InlineData("enpass", typeof(Enpass))]
   [InlineData("gcp", typeof(GcpSecretManager))]
   [InlineData("gcp_secret_manager", typeof(GcpSecretManager))]
   [InlineData("aws_secrets_manager", typeof(AwsSecretsManager))]
   [InlineData("passbolt", typeof(Passbolt))]
   [InlineData("test", typeof(Kamal.Secrets.Adapters.Test))]
   public void LooksUpAdaptersByName(string name, Type expectedType)
   {
      Assert.IsType(expectedType, SecretsAdapters.Lookup(name));
   }

   [Fact]
   public void ThrowsForUnknownAdapters()
   {
      var error = Assert.Throws<InvalidOperationException>(() => SecretsAdapters.Lookup("unknown"));
      Assert.Equal("Unknown secrets adapter: unknown", error.Message);
   }

   [Fact]
   public void TestAdapterReversesSecrets()
   {
      var adapter = SecretsAdapters.Lookup("test");

      var results = adapter.Fetch(new[] { "SECRET1", "LPARENtestRPAREN" }, account: "myaccount", from: "FOLDER");

      Assert.Equal(new Dictionary<string, string>
      {
         ["FOLDER/SECRET1"] = "1TERCES/REDLOF",
         ["FOLDER/LPARENtestRPAREN"] = ")tset(/REDLOF"
      }, results);
   }
}
