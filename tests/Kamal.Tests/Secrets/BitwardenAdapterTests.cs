using Kamal.Secrets.Adapters;

namespace Kamal.Tests.Secrets;

public class BitwardenAdapterTests
{
   private const string MyPasswordJson =
      """
      { "object":"item", "type":1, "name":"mypassword", "login":{"username":null,"password":"secret123"} }
      """;

   private const string MyItemJson =
      """
      {
        "object":"item", "type":1, "name":"myitem",
        "fields":[
          {"name":"field1","value":"secret1","type":1},
          {"name":"field2","value":"blam","type":1},
          {"name":"field3","value":"fewgrwjgk","type":1},
          {"name":"field4","value":"auto","type":1}
        ],
        "login":{"username":null,"password":null}
      }
      """;

   private readonly FakeShell _shell = new();

   [Fact]
   public void Fetch()
   {
      _shell.Stub("bw --version 2> /dev/null");
      StubUnlocked();
      _shell.Stub("bw sync", "");
      _shell.Stub("bw get item mypassword", MyPasswordJson);

      var results = RunFetch(new[] { "mypassword" });

      Assert.Equal(new Dictionary<string, string> { ["mypassword"] = "secret123" }, results);
   }

   [Fact]
   public void FetchWithNoLogin()
   {
      _shell.Stub("bw --version 2> /dev/null");
      StubUnlocked();
      _shell.Stub("bw sync", "");
      _shell.Stub(
         "bw get item mynote",
         """
         { "object":"item", "type":2, "name":"noteitem", "notes":"NOTES", "secureNote":{"type":0} }
         """);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "mynote" }));
      Assert.Contains("not a login type item", error.Message);
   }

   [Fact]
   public void FetchWithFrom()
   {
      _shell.Stub("bw --version 2> /dev/null");
      StubUnlocked();
      _shell.Stub("bw sync", "");
      _shell.Stub("bw get item myitem", MyItemJson);

      var results = RunFetch(new[] { "field1", "field2", "field3" }, from: "myitem");

      Assert.Equal(new Dictionary<string, string>
      {
         ["myitem/field1"] = "secret1",
         ["myitem/field2"] = "blam",
         ["myitem/field3"] = "fewgrwjgk"
      }, results);
   }

   [Fact]
   public void FetchAllWithFrom()
   {
      _shell.Stub("bw --version 2> /dev/null");
      StubUnlocked();
      _shell.Stub("bw sync", "");
      _shell.Stub(
         "bw get item mynotefields",
         """
         {
           "object":"item", "type":2, "name":"noteitem", "notes":"NOTES",
           "fields":[
             {"name":"field1","value":"secret1","type":1},
             {"name":"field2","value":"blam","type":1},
             {"name":"field3","value":"fewgrwjgk","type":1},
             {"name":"field4","value":"auto","type":1}
           ],
           "secureNote":{"type":0}
         }
         """);

      var results = RunFetch(new[] { "mynotefields" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["mynotefields/field1"] = "secret1",
         ["mynotefields/field2"] = "blam",
         ["mynotefields/field3"] = "fewgrwjgk",
         ["mynotefields/field4"] = "auto"
      }, results);
   }

   [Fact]
   public void FetchWithMultipleItems()
   {
      _shell.Stub("bw --version 2> /dev/null");
      StubUnlocked();
      _shell.Stub("bw sync", "");
      _shell.Stub("bw get item mypassword", MyPasswordJson);
      _shell.Stub("bw get item myitem", MyItemJson);
      _shell.Stub(
         "bw get item myitem2",
         """
         {
           "object":"item", "type":1, "name":"myitem2",
           "fields":[ {"name":"field3","value":"fewgrwjgk","type":1} ],
           "login":{"username":null,"password":null}
         }
         """);

      var results = RunFetch(new[] { "mypassword", "myitem/field1", "myitem/field2", "myitem2/field3" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["mypassword"] = "secret123",
         ["myitem/field1"] = "secret1",
         ["myitem/field2"] = "blam",
         ["myitem2/field3"] = "fewgrwjgk"
      }, results);
   }

   [Fact]
   public void FetchUnauthenticated()
   {
      _shell.Stub("bw --version 2> /dev/null");
      _shell.Stub("bw status", """{"serverUrl":null,"lastSync":null,"status":"unauthenticated"}""");
      _shell.Stub("bw status", """{"userEmail":"email@example.com","status":"locked"}""");
      _shell.Stub("bw status", """{"userEmail":"email@example.com","status":"unlocked"}""");
      _shell.Stub("bw login email@example.com", "1234567890");
      _shell.Stub("bw unlock --raw", "");
      _shell.Stub("bw sync", "");
      _shell.Stub("bw get item mypassword", MyPasswordJson);

      var results = RunFetch(new[] { "mypassword" });

      Assert.Equal(new Dictionary<string, string> { ["mypassword"] = "secret123" }, results);
   }

   [Fact]
   public void FetchLocked()
   {
      _shell.Stub("bw --version 2> /dev/null");
      _shell.Stub("bw status", """{"userEmail":"email@example.com","status":"locked"}""");
      _shell.Stub("bw status", """{"userEmail":"email@example.com","status":"unlocked"}""");
      _shell.Stub("bw unlock --raw", "");
      _shell.Stub("bw sync", "");
      _shell.Stub("bw get item mypassword", MyPasswordJson);

      var results = RunFetch(new[] { "mypassword" });

      Assert.Equal(new Dictionary<string, string> { ["mypassword"] = "secret123" }, results);
   }

   [Fact]
   public void FetchLockedWithSession()
   {
      _shell.Stub("bw --version 2> /dev/null");
      _shell.Stub("bw status", """{"userEmail":"email@example.com","status":"locked"}""");
      _shell.Stub("BW_SESSION=0987654321 bw status", """{"userEmail":"email@example.com","status":"unlocked"}""");
      _shell.Stub("bw unlock --raw", "0987654321");
      _shell.Stub("BW_SESSION=0987654321 bw sync", "");
      _shell.Stub("BW_SESSION=0987654321 bw get item mypassword", MyPasswordJson);

      var results = RunFetch(new[] { "mypassword" });

      Assert.Equal(new Dictionary<string, string> { ["mypassword"] = "secret123" }, results);
   }

   [Fact]
   public void FetchWithoutCliInstalled()
   {
      _shell.Stub("bw --version 2> /dev/null", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "mynote" }));
      Assert.Equal("Bitwarden CLI is not installed", error.Message);
   }

   private void StubUnlocked()
   {
      _shell.Stub("bw status", """{"userEmail":"email@example.com","status":"unlocked"}""");
   }

   private Dictionary<string, string> RunFetch(IReadOnlyList<string> secrets, string? from = null)
   {
      var adapter = new Bitwarden { Shell = _shell.Run };
      return adapter.Fetch(secrets, account: "email@example.com", from: from);
   }
}
