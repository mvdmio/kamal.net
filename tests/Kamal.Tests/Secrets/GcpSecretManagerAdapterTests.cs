using Kamal.Secrets.Adapters;

namespace Kamal.Tests.Secrets;

public class GcpSecretManagerAdapterTests
{
   private readonly FakeShell _shell = new();

   [Fact]
   public void Fetch()
   {
      StubGcloudVersion();
      StubAuthenticated();
      _shell.Stub(
         "gcloud secrets versions access latest --secret=mypassword --format=json",
         """
         {
           "name": "projects/000000000/secrets/mypassword/versions/1",
           "payload": { "data": "c2VjcmV0MTIz", "dataCrc32c": "2522602764" }
         }
         """);

      var results = RunFetch(new[] { "mypassword" });

      Assert.Equal(new Dictionary<string, string> { ["default/mypassword"] = "secret123" }, results);
   }

   [Fact]
   public void FetchUnauthenticated()
   {
      _shell.Stub("gcloud --version 2> /dev/null");
      _shell.Stub("gcloud auth list --format=json", "[]");
      _shell.Stub("gcloud auth login", """{ "expired": false, "valid": true }""");

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "mypassword" }));
      Assert.Contains("could not login to gcloud", error.Message);
   }

   [Fact]
   public void FetchWithFrom()
   {
      StubGcloudVersion();
      StubAuthenticated();
      StubItem(0, project: "other-project");
      StubItem(1, project: "other-project");
      StubItem(2, project: "other-project");

      var results = RunFetch(new[] { "item1", "item2", "item3" }, from: "other-project");

      Assert.Equal(new Dictionary<string, string>
      {
         ["other-project/item1"] = "secret1",
         ["other-project/item2"] = "secret2",
         ["other-project/item3"] = "secret3"
      }, results);
   }

   [Fact]
   public void FetchWithMultipleProjects()
   {
      StubGcloudVersion();
      StubAuthenticated();
      StubItem(0, project: "some-project");
      StubItem(1, project: "project-confidence");
      StubItem(2, project: "manhattan-project");

      var results = RunFetch(new[] { "some-project/item1", "project-confidence/item2", "manhattan-project/item3" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["some-project/item1"] = "secret1",
         ["project-confidence/item2"] = "secret2",
         ["manhattan-project/item3"] = "secret3"
      }, results);
   }

   [Fact]
   public void FetchWithSpecificVersion()
   {
      StubGcloudVersion();
      StubAuthenticated();
      StubItem(0, project: "some-project", version: "123");

      var results = RunFetch(new[] { "some-project/item1/123" });

      Assert.Equal(new Dictionary<string, string> { ["some-project/item1"] = "secret1" }, results);
   }

   [Fact]
   public void FetchWithNonDefaultAccount()
   {
      StubGcloudVersion();
      StubAuthenticated();
      StubItem(0, project: "some-project", version: "123", account: "email@example.com");

      var results = RunFetch(new[] { "some-project/item1/123" }, account: "email@example.com");

      Assert.Equal(new Dictionary<string, string> { ["some-project/item1"] = "secret1" }, results);
   }

   [Fact]
   public void FetchWithServiceAccountImpersonation()
   {
      StubGcloudVersion();
      StubAuthenticated();
      StubItem(0, project: "some-project", version: "123", impersonateServiceAccount: "service-user@example.com");

      var results = RunFetch(new[] { "some-project/item1/123" }, account: "default|service-user@example.com");

      Assert.Equal(new Dictionary<string, string> { ["some-project/item1"] = "secret1" }, results);
   }

   [Fact]
   public void FetchWithDelegationChainAndSpecificUser()
   {
      StubGcloudVersion();
      StubAuthenticated();
      StubItem(0, project: "some-project", version: "123", account: "user@example.com", impersonateServiceAccount: "service-user@example.com,service-user2@example.com");

      var results = RunFetch(new[] { "some-project/item1/123" }, account: "user@example.com|service-user@example.com,service-user2@example.com");

      Assert.Equal(new Dictionary<string, string> { ["some-project/item1"] = "secret1" }, results);
   }

   [Fact]
   public void FetchWithoutCliInstalled()
   {
      StubGcloudVersion(succeed: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "item1" }));
      Assert.Equal("gcloud CLI is not installed", error.Message);
   }

   private void StubGcloudVersion(bool succeed = true)
   {
      _shell.Stub("gcloud --version 2> /dev/null", success: succeed);
   }

   private void StubAuthenticated()
   {
      _shell.Stub("gcloud auth list --format=json", """[ { "account": "email@example.com", "status": "ACTIVE" } ]""");
   }

   private void StubItem(int n, string? project = null, string? account = null, string version = "latest", string? impersonateServiceAccount = null)
   {
      var payloads = new[] { "c2VjcmV0MQ==", "c2VjcmV0Mg==", "c2VjcmV0Mw==" };

      var command = $"gcloud secrets versions access {version} --secret=item{n + 1}"
         + (project != null ? $" --project={project}" : "")
         + (account != null ? $" --account={account}" : "")
         + (impersonateServiceAccount != null ? $" --impersonate-service-account={impersonateServiceAccount}" : "")
         + " --format=json";

      _shell.Stub(command,
         $$"""
         {
           "name": "projects/000000001/secrets/item1/versions/1",
           "payload": { "data": "{{payloads[n]}}", "dataCrc32c": "0" }
         }
         """);
   }

   private Dictionary<string, string> RunFetch(IReadOnlyList<string> secrets, string account = "default", string? from = null)
   {
      var adapter = new GcpSecretManager { Shell = _shell.Run };
      return adapter.Fetch(secrets, account: account, from: from);
   }
}
