using Kamal.Secrets.Adapters;

namespace Kamal.Tests.Secrets;

public class EnpassAdapterTests
{
   private readonly FakeShell _shell = new();

   [Fact]
   public void FetchWithoutCliInstalled()
   {
      _shell.Stub("enpass-cli version 2> /dev/null", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "mynote" }));
      Assert.Equal("Enpass CLI is not installed", error.Message);
   }

   [Fact]
   public void FetchOneItem()
   {
      _shell.Stub("enpass-cli version 2> /dev/null");
      _shell.Stub(
         "enpass-cli -json -vault vault-path show FooBar",
         """
         [{"category":"computer","label":"SECRET_1","login":"","password":"my-password-1","title":"FooBar","type":"password"}]
         """);

      var results = RunFetch(new[] { "FooBar/SECRET_1" });

      Assert.Equal(new Dictionary<string, string> { ["FooBar/SECRET_1"] = "my-password-1" }, results);
   }

   [Fact]
   public void FetchMultipleItems()
   {
      _shell.Stub("enpass-cli version 2> /dev/null");
      _shell.Stub(
         "enpass-cli -json -vault vault-path show FooBar",
         """
         [
           {"category":"computer","label":"SECRET_1","login":"","password":"my-password-1","title":"FooBar","type":"password"},
           {"category":"computer","label":"SECRET_2","login":"","password":"my-password-2","title":"FooBar","type":"password"},
           {"category":"computer","label":"SECRET_3","login":"","password":"my-password-1","title":"Hello","type":"password"}
         ]
         """);

      var results = RunFetch(new[] { "FooBar/SECRET_1", "FooBar/SECRET_2" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["FooBar/SECRET_1"] = "my-password-1",
         ["FooBar/SECRET_2"] = "my-password-2"
      }, results);
   }

   [Fact]
   public void FetchAllWithFrom()
   {
      _shell.Stub("enpass-cli version 2> /dev/null");
      _shell.Stub(
         "enpass-cli -json -vault vault-path show FooBar",
         """
         [
           {"category":"computer","label":"SECRET_1","login":"","password":"my-password-1","title":"FooBar","type":"password"},
           {"category":"computer","label":"SECRET_2","login":"","password":"my-password-2","title":"FooBar","type":"password"},
           {"category":"computer","label":"SECRET_3","login":"","password":"my-password-1","title":"Hello","type":"password"},
           {"category":"computer","label":"","login":"","password":"my-password-3","title":"FooBar","type":"password"}
         ]
         """);

      var results = RunFetch(new[] { "FooBar" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["FooBar/SECRET_1"] = "my-password-1",
         ["FooBar/SECRET_2"] = "my-password-2",
         ["FooBar"] = "my-password-3"
      }, results);
   }

   private Dictionary<string, string> RunFetch(IReadOnlyList<string> secrets)
   {
      var adapter = new Enpass { Shell = _shell.Run };
      return adapter.Fetch(secrets, from: "vault-path");
   }
}
