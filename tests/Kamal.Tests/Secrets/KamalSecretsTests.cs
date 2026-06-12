using Kamal.Secrets;

namespace Kamal.Tests.Secrets;

[Collection("inline-command-substitution")]
public class KamalSecretsTests : IDisposable
{
   private readonly string _tmpDir;

   public KamalSecretsTests()
   {
      _tmpDir = Path.Combine(Path.GetTempPath(), "kamal-secrets-tests-" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(Path.Combine(_tmpDir, ".kamal"));
   }

   public void Dispose()
   {
      Directory.Delete(_tmpDir, recursive: true);
   }

   [Fact]
   public void Fetch()
   {
      WriteSecrets("secrets", "SECRET=ABC");

      Assert.Equal("ABC", new KamalSecrets(secretsPath: SecretsPath)["SECRET"]);
   }

   [Fact]
   public void ContainsKey()
   {
      WriteSecrets("secrets", "SECRET1=ABC");

      Assert.True(new KamalSecrets(secretsPath: SecretsPath).ContainsKey("SECRET1"));
      Assert.False(new KamalSecrets(secretsPath: SecretsPath).ContainsKey("SECRET2"));
   }

   [Fact]
   public void CommandInterpolation()
   {
      WriteSecrets("secrets", "SECRET=$(echo ABC)");

      Assert.Equal("ABC", new KamalSecrets(secretsPath: SecretsPath)["SECRET"]);
   }

   [Fact]
   public void VariableReferences()
   {
      WriteSecrets("secrets", "SECRET1=ABC\nSECRET2=${SECRET1}DEF");

      Assert.Equal("ABC", new KamalSecrets(secretsPath: SecretsPath)["SECRET1"]);
      Assert.Equal("ABCDEF", new KamalSecrets(secretsPath: SecretsPath)["SECRET2"]);
   }

   [Fact]
   public void EnvReferences()
   {
      WriteSecrets("secrets", "SECRET1=$KAMALNET_TEST_ENV_REF");
      Environment.SetEnvironmentVariable("KAMALNET_TEST_ENV_REF", "ABC");

      try
      {
         Assert.Equal("ABC", new KamalSecrets(secretsPath: SecretsPath)["SECRET1"]);
      }
      finally
      {
         Environment.SetEnvironmentVariable("KAMALNET_TEST_ENV_REF", null);
      }
   }

   [Fact]
   public void SecretsFileValueOverridesEnv()
   {
      WriteSecrets("secrets", "KAMALNET_TEST_OVERRIDE=DEF");
      Environment.SetEnvironmentVariable("KAMALNET_TEST_OVERRIDE", "ABC");

      try
      {
         Assert.Equal("DEF", new KamalSecrets(secretsPath: SecretsPath)["KAMALNET_TEST_OVERRIDE"]);
      }
      finally
      {
         Environment.SetEnvironmentVariable("KAMALNET_TEST_OVERRIDE", null);
      }
   }

   [Fact]
   public void Destinations()
   {
      WriteSecrets("secrets.dest", "SECRET=DEF");
      WriteSecrets("secrets", "SECRET=ABC");
      WriteSecrets("secrets-common", "SECRET=GHI\nSECRET2=JKL");

      Assert.Equal("ABC", new KamalSecrets(secretsPath: SecretsPath)["SECRET"]);
      Assert.Equal("DEF", new KamalSecrets(destination: "dest", secretsPath: SecretsPath)["SECRET"]);
      Assert.Equal("GHI", new KamalSecrets(destination: "nodest", secretsPath: SecretsPath)["SECRET"]);

      Assert.Equal("JKL", new KamalSecrets(secretsPath: SecretsPath)["SECRET2"]);
      Assert.Equal("JKL", new KamalSecrets(destination: "dest", secretsPath: SecretsPath)["SECRET2"]);
      Assert.Equal("JKL", new KamalSecrets(destination: "nodest", secretsPath: SecretsPath)["SECRET2"]);
   }

   [Fact]
   public void NoSecretsFiles()
   {
      var error = Assert.Throws<KeyNotFoundException>(() => new KamalSecrets(secretsPath: SecretsPath)["SECRET"]);
      Assert.Equal($"Secret 'SECRET' not found, no secret files ({SecretsPath}-common, {SecretsPath}) provided", error.Message);

      error = Assert.Throws<KeyNotFoundException>(() => new KamalSecrets(destination: "dest", secretsPath: SecretsPath)["SECRET"]);
      Assert.Equal($"Secret 'SECRET' not found, no secret files ({SecretsPath}-common, {SecretsPath}.dest) provided", error.Message);
   }

   [Fact]
   public void MissingSecretInExistingFiles()
   {
      WriteSecrets("secrets", "SECRET=ABC");

      var error = Assert.Throws<KeyNotFoundException>(() => new KamalSecrets(secretsPath: SecretsPath)["OTHER"]);
      Assert.Equal($"Secret 'OTHER' not found in {SecretsPath}", error.Message);
   }

   [Fact]
   public void CustomSecretsPath()
   {
      var customPath = Path.Combine(_tmpDir, "custom", "path", "secrets");
      Directory.CreateDirectory(Path.GetDirectoryName(customPath)!);
      File.WriteAllText(customPath, "SECRET=CUSTOM");

      Assert.Equal("CUSTOM", new KamalSecrets(secretsPath: customPath)["SECRET"]);
   }

   [Fact]
   public void CustomSecretsPathWithDestination()
   {
      var customPath = Path.Combine(_tmpDir, "custom", "path", "secrets");
      Directory.CreateDirectory(Path.GetDirectoryName(customPath)!);
      File.WriteAllText(customPath, "SECRET=BASE");
      File.WriteAllText(customPath + ".prod", "SECRET=PROD");

      Assert.Equal("BASE", new KamalSecrets(secretsPath: customPath)["SECRET"]);
      Assert.Equal("PROD", new KamalSecrets(destination: "prod", secretsPath: customPath)["SECRET"]);
   }

   [Fact]
   public void CustomSecretsPathWithCommonFile()
   {
      var customPath = Path.Combine(_tmpDir, "custom", "path", "secrets");
      Directory.CreateDirectory(Path.GetDirectoryName(customPath)!);
      File.WriteAllText(customPath + "-common", "COMMON=SHARED\nSECRET=COMMON");
      File.WriteAllText(customPath, "SECRET=OVERRIDE");

      var secrets = new KamalSecrets(secretsPath: customPath);
      Assert.Equal("SHARED", secrets["COMMON"]);
      Assert.Equal("OVERRIDE", secrets["SECRET"]);
   }

   [Fact]
   public void ToDictionaryReturnsAllSecrets()
   {
      WriteSecrets("secrets", "SECRET1=ABC\nSECRET2=DEF");

      var secrets = new KamalSecrets(secretsPath: SecretsPath).ToDictionary();

      Assert.Equal(new Dictionary<string, string> { ["SECRET1"] = "ABC", ["SECRET2"] = "DEF" }, secrets);
   }

   private string SecretsPath => Path.Combine(_tmpDir, ".kamal", "secrets");

   private void WriteSecrets(string filename, string contents)
   {
      File.WriteAllText(Path.Combine(_tmpDir, ".kamal", filename), contents);
   }
}
