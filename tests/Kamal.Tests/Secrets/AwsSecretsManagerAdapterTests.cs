using Kamal.Secrets.Adapters;

namespace Kamal.Tests.Secrets;

public class AwsSecretsManagerAdapterTests
{
   private readonly FakeShell _shell = new();

   [Fact]
   public void FailsWhenErrorsArePresent()
   {
      _shell.Stub("aws --version 2> /dev/null");
      _shell.Stub(
         "aws secretsmanager batch-get-secret-value --secret-id-list unknown1 unknown2 --profile default --output json",
         """
         {
           "SecretValues": [],
           "Errors": [
             { "SecretId": "unknown1", "ErrorCode": "ResourceNotFoundException", "Message": "Secrets Manager can't find the specified secret." },
             { "SecretId": "unknown2", "ErrorCode": "ResourceNotFoundException", "Message": "Secrets Manager can't find the specified secret." }
           ]
         }
         """);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "unknown1", "unknown2" }));
      Assert.Equal(
         "unknown1: Secrets Manager can't find the specified secret. unknown2: Secrets Manager can't find the specified secret.",
         error.Message);
   }

   [Fact]
   public void Fetch()
   {
      _shell.Stub("aws --version 2> /dev/null");
      _shell.Stub(
         "aws secretsmanager batch-get-secret-value --secret-id-list secret/KEY1 secret/KEY2 secret2/KEY3 --profile default --output json",
         """
         {
           "SecretValues": [
             { "Name": "secret", "SecretString": "{\"KEY1\":\"VALUE1\", \"KEY2\":\"VALUE2\"}" },
             { "Name": "secret2", "SecretString": "{\"KEY3\":\"VALUE3\"}" }
           ],
           "Errors": []
         }
         """);

      var results = RunFetch(new[] { "secret/KEY1", "secret/KEY2", "secret2/KEY3" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["secret/KEY1"] = "VALUE1",
         ["secret/KEY2"] = "VALUE2",
         ["secret2/KEY3"] = "VALUE3"
      }, results);
   }

   [Fact]
   public void FetchWithStringValue()
   {
      _shell.Stub("aws --version 2> /dev/null");
      _shell.Stub(
         "aws secretsmanager batch-get-secret-value --secret-id-list secret secret2/KEY1 --profile default --output json",
         """
         {
           "SecretValues": [
             { "Name": "secret", "SecretString": "a-string-secret" },
             { "Name": "secret2", "SecretString": "{\"KEY2\":\"VALUE2\"}" }
           ],
           "Errors": []
         }
         """);

      var results = RunFetch(new[] { "secret", "secret2/KEY1" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["secret"] = "a-string-secret",
         ["secret2/KEY2"] = "VALUE2"
      }, results);
   }

   [Fact]
   public void FetchWithSecretNames()
   {
      _shell.Stub("aws --version 2> /dev/null");
      _shell.Stub(
         "aws secretsmanager batch-get-secret-value --secret-id-list secret/KEY1 secret/KEY2 --profile default --output json",
         """
         {
           "SecretValues": [
             { "Name": "secret", "SecretString": "{\"KEY1\":\"VALUE1\", \"KEY2\":\"VALUE2\"}" }
           ],
           "Errors": []
         }
         """);

      var results = RunFetch(new[] { "KEY1", "KEY2" }, from: "secret");

      Assert.Equal(new Dictionary<string, string>
      {
         ["secret/KEY1"] = "VALUE1",
         ["secret/KEY2"] = "VALUE2"
      }, results);
   }

   [Fact]
   public void FetchWithoutCliInstalled()
   {
      _shell.Stub("aws --version 2> /dev/null", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "SECRET1" }));
      Assert.Equal("AWS CLI is not installed", error.Message);
   }

   [Fact]
   public void FetchWithoutAccountOptionOmitsProfile()
   {
      _shell.Stub("aws --version 2> /dev/null");
      _shell.Stub(
         "aws secretsmanager batch-get-secret-value --secret-id-list secret/KEY1 secret/KEY2 --output json",
         """
         {
           "SecretValues": [
             { "Name": "secret", "SecretString": "{\"KEY1\":\"VALUE1\", \"KEY2\":\"VALUE2\"}" }
           ],
           "Errors": []
         }
         """);

      var results = RunFetch(new[] { "KEY1", "KEY2" }, from: "secret", account: null);

      Assert.Equal(new Dictionary<string, string>
      {
         ["secret/KEY1"] = "VALUE1",
         ["secret/KEY2"] = "VALUE2"
      }, results);
   }

   private Dictionary<string, string> RunFetch(IReadOnlyList<string> secrets, string? from = null, string? account = "default")
   {
      var adapter = new AwsSecretsManager { Shell = _shell.Run };
      return adapter.Fetch(secrets, account: account, from: from);
   }
}
