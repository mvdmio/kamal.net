using Kamal.Secrets.Adapters;

namespace Kamal.Tests.Secrets;

public class BitwardenSecretsManagerAdapterTests
{
   private readonly FakeShell _shell = new();

   [Fact]
   public void FetchWithNoParameters()
   {
      _shell.Stub("bws --version 2> /dev/null");
      StubLogin();

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(Array.Empty<string>()));
      Assert.Equal("You must specify what to retrieve from Bitwarden Secrets Manager", error.Message);
   }

   [Fact]
   public void FetchAll()
   {
      _shell.Stub("bws --version 2> /dev/null");
      StubLogin();
      _shell.Stub(
         "bws secret list",
         """
         [
           { "key": "KAMAL_REGISTRY_PASSWORD", "value": "some_password" },
           { "key": "MY_OTHER_SECRET", "value": "my=wierd\"secret" }
         ]
         """);

      var results = RunFetch(new[] { "all" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["KAMAL_REGISTRY_PASSWORD"] = "some_password",
         ["MY_OTHER_SECRET"] = "my=wierd\"secret"
      }, results);
   }

   [Fact]
   public void FetchAllWithFrom()
   {
      _shell.Stub("bws --version 2> /dev/null");
      StubLogin();
      _shell.Stub(
         "bws secret list 82aeb5bd-6958-4a89-8197-eacab758acce",
         """
         [
           { "key": "KAMAL_REGISTRY_PASSWORD", "value": "some_password" },
           { "key": "MY_OTHER_SECRET", "value": "my=wierd\"secret" }
         ]
         """);

      var results = RunFetch(new[] { "all" }, from: "82aeb5bd-6958-4a89-8197-eacab758acce");

      Assert.Equal(new Dictionary<string, string>
      {
         ["KAMAL_REGISTRY_PASSWORD"] = "some_password",
         ["MY_OTHER_SECRET"] = "my=wierd\"secret"
      }, results);
   }

   [Fact]
   public void FetchItem()
   {
      _shell.Stub("bws --version 2> /dev/null");
      StubLogin();
      _shell.Stub(
         "bws secret get 82aeb5bd-6958-4a89-8197-eacab758acce",
         """{ "key": "KAMAL_REGISTRY_PASSWORD", "value": "some_password" }""");

      var results = RunFetch(new[] { "82aeb5bd-6958-4a89-8197-eacab758acce" });

      Assert.Equal(new Dictionary<string, string> { ["KAMAL_REGISTRY_PASSWORD"] = "some_password" }, results);
   }

   [Fact]
   public void FetchWithMultipleItems()
   {
      _shell.Stub("bws --version 2> /dev/null");
      StubLogin();
      _shell.Stub(
         "bws secret get 82aeb5bd-6958-4a89-8197-eacab758acce",
         """{ "key": "KAMAL_REGISTRY_PASSWORD", "value": "some_password" }""");
      _shell.Stub(
         "bws secret get 6f8cdf27-de2b-4c77-a35d-07df8050e332",
         """{ "key": "MY_OTHER_SECRET", "value": "my=wierd\"secret" }""");

      var results = RunFetch(new[] { "82aeb5bd-6958-4a89-8197-eacab758acce", "6f8cdf27-de2b-4c77-a35d-07df8050e332" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["KAMAL_REGISTRY_PASSWORD"] = "some_password",
         ["MY_OTHER_SECRET"] = "my=wierd\"secret"
      }, results);
   }

   [Fact]
   public void FetchAllEmpty()
   {
      _shell.Stub("bws --version 2> /dev/null");
      StubLogin();
      _shell.Stub("bws secret list", "Error:\n0: Received error message from server", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "all" }));
      Assert.Equal("Could not read secrets from Bitwarden Secrets Manager", error.Message);
   }

   [Fact]
   public void FetchNonexistentItem()
   {
      _shell.Stub("bws --version 2> /dev/null");
      StubLogin();
      _shell.Stub("bws secret get 82aeb5bd-6958-4a89-8197-eacab758acce", "Error:\n0: Received error message from server", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "82aeb5bd-6958-4a89-8197-eacab758acce" }));
      Assert.Equal("Could not read 82aeb5bd-6958-4a89-8197-eacab758acce from Bitwarden Secrets Manager", error.Message);
   }

   [Fact]
   public void FetchItemWithLinebreakInValue()
   {
      _shell.Stub("bws --version 2> /dev/null");
      StubLogin();
      _shell.Stub(
         "bws secret get 82aeb5bd-6958-4a89-8197-eacab758acce",
         """{ "key": "SSH_PRIVATE_KEY", "value": "some_key\nwith_linebreak" }""");

      var results = RunFetch(new[] { "82aeb5bd-6958-4a89-8197-eacab758acce" });

      Assert.Equal(new Dictionary<string, string> { ["SSH_PRIVATE_KEY"] = "some_key\nwith_linebreak" }, results);
   }

   [Fact]
   public void FetchWithNoAccessToken()
   {
      _shell.Stub("bws --version 2> /dev/null");
      _shell.Stub("bws project list", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "all" }));
      Assert.Equal("Could not authenticate to Bitwarden Secrets Manager. Did you set a valid access token?", error.Message);
   }

   [Fact]
   public void FetchWithoutCliInstalled()
   {
      _shell.Stub("bws --version 2> /dev/null", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(Array.Empty<string>()));
      Assert.Equal("Bitwarden Secrets Manager CLI is not installed", error.Message);
   }

   private void StubLogin()
   {
      _shell.Stub("bws project list", "OK");
   }

   private Dictionary<string, string> RunFetch(IReadOnlyList<string> secrets, string? from = null)
   {
      var adapter = new BitwardenSecretsManager { Shell = _shell.Run };
      return adapter.Fetch(secrets, from: from);
   }
}
