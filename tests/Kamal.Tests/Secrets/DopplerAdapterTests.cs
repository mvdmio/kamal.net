using Kamal.Secrets.Adapters;

namespace Kamal.Tests.Secrets;

[Collection("doppler-token")]
public class DopplerAdapterTests
{
   private const string ThreeItemsJson =
      """
      {
        "SECRET1": { "computed":"secret1", "computedVisibility":"unmasked", "note":"" },
        "FSECRET1": { "computed":"fsecret1", "computedVisibility":"unmasked", "note":"" },
        "FSECRET2": { "computed":"fsecret2", "computedVisibility":"unmasked", "note":"" }
      }
      """;

   private readonly FakeShell _shell = new();

   [Fact]
   public void Fetch()
   {
      _shell.Stub("doppler --version 2> /dev/null");
      _shell.Stub("doppler me --json 2> /dev/null");
      _shell.Stub("doppler secrets get SECRET1 FSECRET1 FSECRET2 --json -p my-project -c prd", ThreeItemsJson);

      var results = RunFetch(new[] { "SECRET1", "FSECRET1", "FSECRET2" }, from: "my-project/prd");

      Assert.Equal(new Dictionary<string, string>
      {
         ["SECRET1"] = "secret1",
         ["FSECRET1"] = "fsecret1",
         ["FSECRET2"] = "fsecret2"
      }, results);
   }

   [Fact]
   public void FetchHavingDopplerToken()
   {
      Environment.SetEnvironmentVariable("DOPPLER_TOKEN", "dp.st.xxxxxxxxxxxxxxxxxxxxxx");

      try
      {
         _shell.Stub("doppler --version 2> /dev/null");
         _shell.Stub("doppler me --json 2> /dev/null");
         _shell.Stub("doppler secrets get SECRET1 FSECRET1 FSECRET2 --json ", ThreeItemsJson);

         var results = RunFetch(new[] { "SECRET1", "FSECRET1", "FSECRET2" });

         Assert.Equal(new Dictionary<string, string>
         {
            ["SECRET1"] = "secret1",
            ["FSECRET1"] = "fsecret1",
            ["FSECRET2"] = "fsecret2"
         }, results);
      }
      finally
      {
         Environment.SetEnvironmentVariable("DOPPLER_TOKEN", null);
      }
   }

   [Fact]
   public void FetchWithFolderInSecret()
   {
      _shell.Stub("doppler --version 2> /dev/null");
      _shell.Stub("doppler me --json 2> /dev/null");
      _shell.Stub("doppler secrets get SECRET1 FSECRET1 FSECRET2 --json -p my-project -c prd", ThreeItemsJson);

      var results = RunFetch(new[] { "my-project/prd/SECRET1", "my-project/prd/FSECRET1", "my-project/prd/FSECRET2" });

      Assert.Equal(new Dictionary<string, string>
      {
         ["SECRET1"] = "secret1",
         ["FSECRET1"] = "fsecret1",
         ["FSECRET2"] = "fsecret2"
      }, results);
   }

   [Fact]
   public void FetchWithoutFrom()
   {
      _shell.Stub("doppler --version 2> /dev/null");
      _shell.Stub("doppler me --json 2> /dev/null");

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "FSECRET1", "FSECRET2" }));
      Assert.Equal("Missing project or config from '--from=project/config' option", error.Message);
   }

   [Fact]
   public void FetchWithSignin()
   {
      _shell.Stub("doppler --version 2> /dev/null");
      _shell.Stub("doppler me --json 2> /dev/null", success: false);
      _shell.Stub("doppler login -y", "");
      _shell.Stub(
         "doppler secrets get SECRET1 --json -p my-project -c prd",
         """{ "SECRET1": { "computed":"secret1", "computedVisibility":"unmasked", "note":"" } }""");

      var results = RunFetch(new[] { "SECRET1" }, from: "my-project/prd");

      Assert.Equal(new Dictionary<string, string> { ["SECRET1"] = "secret1" }, results);
   }

   [Fact]
   public void FetchWithoutCliInstalled()
   {
      _shell.Stub("doppler --version 2> /dev/null", success: false);

      var error = Assert.Throws<InvalidOperationException>(() => RunFetch(new[] { "HOST", "PORT" }));
      Assert.Equal("Doppler CLI is not installed", error.Message);
   }

   private Dictionary<string, string> RunFetch(IReadOnlyList<string> secrets, string? from = null)
   {
      var adapter = new Doppler { Shell = _shell.Run };
      return adapter.Fetch(secrets, from: from);
   }
}
