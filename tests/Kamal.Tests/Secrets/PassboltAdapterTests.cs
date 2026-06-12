using Kamal.Secrets.Adapters;

namespace Kamal.Tests.Secrets;

public class PassboltAdapterTests
{
   private readonly FakeShell _shell = new();

   [Fact]
   public void Fetch()
   {
      StubVerified();
      _shell.Stub(
         "passbolt list resources --filter 'Name == \"SECRET1\" || Name == \"FSECRET1\" || Name == \"FSECRET2\"'  --json",
         """
         [
           { "id": "4c116996", "folder_parent_id": "", "name": "FSECRET1", "password": "fsecret1" },
           { "id": "62949b26", "folder_parent_id": "", "name": "FSECRET2", "password": "fsecret2" },
           { "id": "dd32963c", "folder_parent_id": "", "name": "SECRET1", "password": "secret1" }
         ]
         """);

      var results = RunFetch(new[] { "SECRET1", "FSECRET1", "FSECRET2" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["SECRET1"] = "secret1",
         ["FSECRET1"] = "fsecret1",
         ["FSECRET2"] = "fsecret2"
      }, results);
   }

   [Fact]
   public void FetchWithFrom()
   {
      StubVerified();
      _shell.Stub(
         "passbolt list folders --filter 'Name == \"my-project\"' --json",
         """
         [ { "id": "dcbe0e39-42d8-42db-9637-8256b9f2f8e3", "folder_parent_id": "", "name": "my-project" } ]
         """);
      _shell.Stub(
         "passbolt list resources --filter '(Name == \"SECRET1\" && FolderParentID == \"dcbe0e39-42d8-42db-9637-8256b9f2f8e3\") || (Name == \"FSECRET1\" && FolderParentID == \"dcbe0e39-42d8-42db-9637-8256b9f2f8e3\") || (Name == \"FSECRET2\" && FolderParentID == \"dcbe0e39-42d8-42db-9637-8256b9f2f8e3\")' --folder dcbe0e39-42d8-42db-9637-8256b9f2f8e3 --json",
         """
         [
           { "id": "4c116996", "folder_parent_id": "dcbe0e39-42d8-42db-9637-8256b9f2f8e3", "name": "FSECRET1", "password": "fsecret1" },
           { "id": "62949b26", "folder_parent_id": "dcbe0e39-42d8-42db-9637-8256b9f2f8e3", "name": "FSECRET2", "password": "fsecret2" },
           { "id": "dd32963c", "folder_parent_id": "dcbe0e39-42d8-42db-9637-8256b9f2f8e3", "name": "SECRET1", "password": "secret1" }
         ]
         """);

      var results = RunFetch(new[] { "SECRET1", "FSECRET1", "FSECRET2" }, from: "my-project");

      Assert.Equal(new Dictionary<string, string>
      {
         ["SECRET1"] = "secret1",
         ["FSECRET1"] = "fsecret1",
         ["FSECRET2"] = "fsecret2"
      }, results);
   }

   [Fact]
   public void FetchFromMultipleFolders()
   {
      StubVerified();
      _shell.Stub(
         "passbolt list folders --filter 'Name == \"my-project\" || Name == \"other-project\"' --json",
         """
         [
           { "id": "dcbe0e39-42d8-42db-9637-8256b9f2f8e3", "folder_parent_id": "", "name": "my-project" },
           { "id": "14e11dd8-b279-4689-8bd9-fa33ebb527da", "folder_parent_id": "", "name": "other-project" }
         ]
         """);
      _shell.Stub(
         "passbolt list resources --filter '(Name == \"SECRET1\" && FolderParentID == \"dcbe0e39-42d8-42db-9637-8256b9f2f8e3\") || (Name == \"FSECRET1\" && FolderParentID == \"dcbe0e39-42d8-42db-9637-8256b9f2f8e3\") || (Name == \"FSECRET2\" && FolderParentID == \"14e11dd8-b279-4689-8bd9-fa33ebb527da\")' --folder dcbe0e39-42d8-42db-9637-8256b9f2f8e3 --folder 14e11dd8-b279-4689-8bd9-fa33ebb527da --json",
         """
         [
           { "id": "4c116996", "folder_parent_id": "dcbe0e39-42d8-42db-9637-8256b9f2f8e3", "name": "FSECRET1", "password": "fsecret1" },
           { "id": "62949b26", "folder_parent_id": "14e11dd8-b279-4689-8bd9-fa33ebb527da", "name": "FSECRET2", "password": "fsecret2" },
           { "id": "dd32963c", "folder_parent_id": "dcbe0e39-42d8-42db-9637-8256b9f2f8e3", "name": "SECRET1", "password": "secret1" }
         ]
         """);

      var results = RunFetch(new[] { "my-project/SECRET1", "my-project/FSECRET1", "other-project/FSECRET2" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["SECRET1"] = "secret1",
         ["FSECRET1"] = "fsecret1",
         ["FSECRET2"] = "fsecret2"
      }, results);
   }

   [Fact]
   public void FetchFromNestedFolder()
   {
      StubVerified();
      _shell.Stub(
         "passbolt list folders --filter 'Name == \"my-project\"' --json",
         """
         [ { "id": "dcbe0e39-42d8-42db-9637-8256b9f2f8e3", "folder_parent_id": "", "name": "my-project" } ]
         """);
      _shell.Stub(
         "passbolt list folders --filter 'Name == \"subfolder\" && FolderParentID == \"dcbe0e39-42d8-42db-9637-8256b9f2f8e3\"' --json",
         """
         [ { "id": "6a3f21fc-aa40-4ba9-852c-7477fdd0310d", "folder_parent_id": "dcbe0e39-42d8-42db-9637-8256b9f2f8e3", "name": "subfolder" } ]
         """);
      _shell.Stub(
         "passbolt list resources --filter '(Name == \"SECRET1\" && FolderParentID == \"6a3f21fc-aa40-4ba9-852c-7477fdd0310d\") || (Name == \"FSECRET1\" && FolderParentID == \"6a3f21fc-aa40-4ba9-852c-7477fdd0310d\") || (Name == \"FSECRET2\" && FolderParentID == \"6a3f21fc-aa40-4ba9-852c-7477fdd0310d\")' --folder dcbe0e39-42d8-42db-9637-8256b9f2f8e3 --folder 6a3f21fc-aa40-4ba9-852c-7477fdd0310d --json",
         """
         [
           { "id": "4c116996", "folder_parent_id": "6a3f21fc-aa40-4ba9-852c-7477fdd0310d", "name": "FSECRET1", "password": "fsecret1" },
           { "id": "62949b26", "folder_parent_id": "6a3f21fc-aa40-4ba9-852c-7477fdd0310d", "name": "FSECRET2", "password": "fsecret2" },
           { "id": "dd32963c", "folder_parent_id": "6a3f21fc-aa40-4ba9-852c-7477fdd0310d", "name": "SECRET1", "password": "secret1" }
         ]
         """);

      var results = RunFetch(new[] { "SECRET1", "FSECRET1", "FSECRET2" }, from: "my-project/subfolder");

      Assert.Equal(new Dictionary<string, string>
      {
         ["SECRET1"] = "secret1",
         ["FSECRET1"] = "fsecret1",
         ["FSECRET2"] = "fsecret2"
      }, results);
   }

   [Fact]
   public void FetchWithMissingSecrets()
   {
      StubVerified();
      _shell.Stub(
         "passbolt list resources --filter 'Name == \"SECRET1\" || Name == \"SECRET2\"'  --json",
         """
         [ { "id": "dd32963c", "folder_parent_id": "", "name": "SECRET1", "password": "secret1" } ]
         """);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "SECRET1", "SECRET2" }));
      Assert.Equal("Could not find the following secrets in Passbolt: SECRET2", error.Message);
   }

   [Fact]
   public void FetchWithoutCliInstalled()
   {
      _shell.Stub("passbolt --version 2> /dev/null", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "SECRET1" }));
      Assert.Equal("Passbolt CLI is not installed", error.Message);
   }

   [Fact]
   public void FetchWithFailedVerify()
   {
      _shell.Stub("passbolt --version 2> /dev/null");
      _shell.Stub("passbolt verify", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "SECRET1" }));
      Assert.Equal("Failed to login to Passbolt", error.Message);
   }

   private void StubVerified()
   {
      _shell.Stub("passbolt --version 2> /dev/null");
      _shell.Stub("passbolt verify");
   }

   private Dictionary<string, string> RunFetch(IReadOnlyList<string> secrets, string? from = null)
   {
      var adapter = new Passbolt { Shell = _shell.Run };
      return adapter.Fetch(secrets, from: from);
   }
}
