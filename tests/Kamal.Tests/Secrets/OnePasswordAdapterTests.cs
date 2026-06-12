using Kamal.Secrets.Adapters;

namespace Kamal.Tests.Secrets;

public class OnePasswordAdapterTests
{
   private readonly FakeShell _shell = new();

   [Fact]
   public void Fetch()
   {
      _shell.Stub("op --version 2> /dev/null");
      _shell.Stub("op account get --account myaccount 2> /dev/null");
      _shell.Stub(
         "op item get myitem --vault \"myvault\" --format \"json\" --account \"myaccount\" --fields \"label=section.SECRET1,label=section.SECRET2,label=section2.SECRET3\"",
         """
         [
           { "id": "a", "type": "CONCEALED", "label": "SECRET1", "value": "VALUE1", "reference": "op://myvault/myitem/section/SECRET1" },
           { "id": "b", "type": "CONCEALED", "label": "SECRET2", "value": "VALUE2", "reference": "op://myvault/myitem/section/SECRET2" },
           { "id": "c", "type": "CONCEALED", "label": "SECRET3", "value": "VALUE3", "reference": "op://myvault/myitem/section2/SECRET3" }
         ]
         """);

      var results = RunFetch(new[] { "section/SECRET1", "section/SECRET2", "section2/SECRET3" }, from: "op://myvault/myitem");

      Assert.Equal(new Dictionary<string, string>
      {
         ["myvault/myitem/section/SECRET1"] = "VALUE1",
         ["myvault/myitem/section/SECRET2"] = "VALUE2",
         ["myvault/myitem/section2/SECRET3"] = "VALUE3"
      }, results);
   }

   [Fact]
   public void FetchWithMultipleItems()
   {
      _shell.Stub("op --version 2> /dev/null");
      _shell.Stub("op account get --account myaccount 2> /dev/null");
      _shell.Stub(
         "op item get myitem --vault \"myvault\" --format \"json\" --account \"myaccount\" --fields \"label=section.SECRET1,label=section.SECRET2\"",
         """
         [
           { "label": "SECRET1", "value": "VALUE1", "reference": "op://myvault/myitem/section/SECRET1" },
           { "label": "SECRET2", "value": "VALUE2", "reference": "op://myvault/myitem/section/SECRET2" }
         ]
         """);
      _shell.Stub(
         "op item get myitem2 --vault \"myvault\" --format \"json\" --account \"myaccount\" --fields \"label=section2.SECRET3\"",
         """
         { "label": "SECRET3", "value": "VALUE3", "reference": "op://myvault/myitem2/section/SECRET3" }
         """);

      var results = RunFetch(new[] { "myitem/section/SECRET1", "myitem/section/SECRET2", "myitem2/section2/SECRET3" }, from: "op://myvault");

      Assert.Equal(new Dictionary<string, string>
      {
         ["myvault/myitem/section/SECRET1"] = "VALUE1",
         ["myvault/myitem/section/SECRET2"] = "VALUE2",
         ["myvault/myitem2/section/SECRET3"] = "VALUE3"
      }, results);
   }

   [Fact]
   public void FetchAllFields()
   {
      _shell.Stub("op --version 2> /dev/null");
      _shell.Stub("op account get --account myaccount 2> /dev/null");
      _shell.Stub(
         "op item get myitem --vault \"myvault\" --format \"json\" --account \"myaccount\"",
         """
         {
           "id": "ucbtiii777",
           "title": "A title",
           "category": "LOGIN",
           "fields": [
             { "label": "SECRET1", "value": "VALUE1", "reference": "op://myvault/myitem/section/SECRET1" },
             { "label": "SECRET2", "value": "VALUE2", "reference": "op://myvault/myitem/section/SECRET2" }
           ]
         }
         """);

      var results = RunFetch(Array.Empty<string>(), from: "op://myvault/myitem");

      Assert.Equal(new Dictionary<string, string>
      {
         ["myvault/myitem/section/SECRET1"] = "VALUE1",
         ["myvault/myitem/section/SECRET2"] = "VALUE2"
      }, results);
   }

   [Fact]
   public void FetchWithSigninNoSession()
   {
      _shell.Stub("op --version 2> /dev/null");
      _shell.Stub("op account get --account myaccount 2> /dev/null", success: false);
      _shell.Stub("op signin --account \"myaccount\" --force --raw", "");
      _shell.Stub(
         "op item get myitem --vault \"myvault\" --format \"json\" --account \"myaccount\" --fields \"label=section.SECRET1\"",
         SingleItemJson);

      var results = RunFetch(new[] { "section/SECRET1" }, from: "op://myvault/myitem");

      Assert.Equal(new Dictionary<string, string> { ["myvault/myitem/section/SECRET1"] = "VALUE1" }, results);
   }

   [Fact]
   public void FetchWithSigninAndSession()
   {
      _shell.Stub("op --version 2> /dev/null");
      _shell.Stub("op account get --account myaccount 2> /dev/null", success: false);
      _shell.Stub("op signin --account \"myaccount\" --force --raw", "1234567890");
      _shell.Stub(
         "op item get myitem --vault \"myvault\" --format \"json\" --account \"myaccount\" --session \"1234567890\" --fields \"label=section.SECRET1\"",
         SingleItemJson);

      var results = RunFetch(new[] { "section/SECRET1" }, from: "op://myvault/myitem");

      Assert.Equal(new Dictionary<string, string> { ["myvault/myitem/section/SECRET1"] = "VALUE1" }, results);
   }

   [Fact]
   public void FetchWithoutCliInstalled()
   {
      _shell.Stub("op --version 2> /dev/null", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "section/SECRET1" }, from: "op://myvault/myitem"));
      Assert.Equal("1Password CLI is not installed", error.Message);
   }

   [Fact]
   public void FetchWithoutAccount()
   {
      var error = Assert.Throws<InvalidOperationException>(() => new OnePassword().Fetch(new[] { "x" }, account: null));
      Assert.Equal("Missing required option '--account'", error.Message);
   }

   private const string SingleItemJson =
      """
      { "label": "SECRET1", "value": "VALUE1", "reference": "op://myvault/myitem/section/SECRET1" }
      """;

   private Dictionary<string, string> RunFetch(IReadOnlyList<string> secrets, string? from = null)
   {
      var adapter = new OnePassword { Shell = _shell.Run };
      return adapter.Fetch(secrets, account: "myaccount", from: from);
   }
}
