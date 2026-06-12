using Kamal.Secrets.Adapters;

namespace Kamal.Tests.Secrets;

public class LastPassAdapterTests
{
   private readonly FakeShell _shell = new();

   [Fact]
   public void Fetch()
   {
      _shell.Stub("lpass --version 2> /dev/null");
      _shell.Stub("lpass status --color never", "Logged in as email@example.com.");
      _shell.Stub(
         "lpass show SECRET1 FOLDER1/FSECRET1 FOLDER1/FSECRET2 --json",
         """
         [
           { "name": "SECRET1", "fullname": "SECRET1", "password": "secret1" },
           { "name": "FSECRET1", "fullname": "FOLDER1/FSECRET1", "password": "fsecret1" },
           { "name": "FSECRET2", "fullname": "FOLDER1/FSECRET2", "password": "fsecret2" }
         ]
         """);

      var results = RunFetch(new[] { "SECRET1", "FOLDER1/FSECRET1", "FOLDER1/FSECRET2" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["SECRET1"] = "secret1",
         ["FOLDER1/FSECRET1"] = "fsecret1",
         ["FOLDER1/FSECRET2"] = "fsecret2"
      }, results);
   }

   [Fact]
   public void FetchWithFrom()
   {
      _shell.Stub("lpass --version 2> /dev/null");
      _shell.Stub("lpass status --color never", "Logged in as email@example.com.");
      _shell.Stub(
         "lpass show FOLDER1/FSECRET1 FOLDER1/FSECRET2 --json",
         """
         [
           { "name": "FSECRET1", "fullname": "FOLDER1/FSECRET1", "password": "fsecret1" },
           { "name": "FSECRET2", "fullname": "FOLDER1/FSECRET2", "password": "fsecret2" }
         ]
         """);

      var results = RunFetch(new[] { "FSECRET1", "FSECRET2" }, from: "FOLDER1");

      Assert.Equal(new Dictionary<string, string>
      {
         ["FOLDER1/FSECRET1"] = "fsecret1",
         ["FOLDER1/FSECRET2"] = "fsecret2"
      }, results);
   }

   [Fact]
   public void FetchWithSignin()
   {
      _shell.Stub("lpass --version 2> /dev/null");
      _shell.Stub("lpass status --color never", "Not logged in.", success: false);
      _shell.Stub("lpass login email@example.com", "");
      _shell.Stub(
         "lpass show SECRET1 --json",
         """
         [{ "name": "SECRET1", "fullname": "SECRET1", "password": "secret1" }]
         """);

      var results = RunFetch(new[] { "SECRET1" });

      Assert.Equal(new Dictionary<string, string> { ["SECRET1"] = "secret1" }, results);
   }

   [Fact]
   public void FetchWithMissingItems()
   {
      _shell.Stub("lpass --version 2> /dev/null");
      _shell.Stub("lpass status --color never", "Logged in as email@example.com.");
      _shell.Stub(
         "lpass show SECRET1 SECRET2 --json",
         """
         [{ "name": "SECRET1", "fullname": "SECRET1", "password": "secret1" }]
         """);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "SECRET1", "SECRET2" }));
      Assert.Equal("Could not find SECRET2 in LastPass", error.Message);
   }

   [Fact]
   public void FetchWithoutCliInstalled()
   {
      _shell.Stub("lpass --version 2> /dev/null", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "SECRET1" }));
      Assert.Equal("LastPass CLI is not installed", error.Message);
   }

   private Dictionary<string, string> RunFetch(IReadOnlyList<string> secrets, string? from = null)
   {
      var adapter = new LastPass { Shell = _shell.Run };
      return adapter.Fetch(secrets, account: "email@example.com", from: from);
   }
}
