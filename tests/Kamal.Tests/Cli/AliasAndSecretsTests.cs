using Kamal.Cli;

namespace Kamal.Tests.Cli;

/// <summary>Alias resolution (port of the dynamic command handling in cli.rb).</summary>
[Collection("kamal-config")]
public sealed class AliasTests
{
   private const string DeployWithAliases =
      """
      service: app
      image: dhh/app
      servers:
        - 1.1.1.1
      registry:
        username: user
        password: pw
      builder:
        arch: amd64
      aliases:
        ver: version
        info: details
      """;

   [Fact]
   public async Task AliasResolvesToItsCommand()
   {
      using var harness = new CliTestHarness(DeployWithAliases);

      var exitCode = await harness.Run("ver");

      Assert.Equal(0, exitCode);
      Assert.Contains("2.11.0 (kamal.net)", harness.Output);
   }

   [Fact]
   public async Task AliasPassesRemainingArguments()
   {
      using var harness = new CliTestHarness(DeployWithAliases);
      harness.RespondTo("tail -n 50 .kamal/app-audit.log", "events\n");

      // "info" resolves to "details"; remaining args still apply (use -q via global option).
      var exitCode = await harness.Run("info", "-q");

      Assert.Equal(0, exitCode);
   }

   [Fact]
   public async Task UnknownCommandWithoutAliasFails()
   {
      using var harness = new CliTestHarness(DeployWithAliases);

      var exitCode = await harness.Run("never_an_alias");

      Assert.Equal(1, exitCode);
   }
}

/// <summary>Port of the high-value parts of <c>test/cli/secrets_test.rb</c>.</summary>
[Collection("kamal-config")]
public sealed class SecretsCliTests
{
   [Fact]
   public async Task ExtractFindsDirectKey()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("secrets", "extract", "PASS", """{"PASS":"secret123"}""");

      Assert.Equal(0, exitCode);
      Assert.Contains("secret123", harness.Output);
   }

   [Fact]
   public async Task ExtractFindsPathSuffixedKey()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("secrets", "extract", "PASS", """{"vault/item/PASS":"secret123"}""");

      Assert.Equal(0, exitCode);
      Assert.Contains("secret123", harness.Output);
   }

   [Fact]
   public async Task ExtractFailsForMissingSecret()
   {
      using var harness = new CliTestHarness();

      var exitCode = await harness.Run("secrets", "extract", "MISSING", """{"PASS":"x"}""");

      Assert.Equal(1, exitCode);
      Assert.Contains("Could not find secret MISSING", harness.Output);
   }

   [Fact]
   public async Task PrintShowsConfiguredSecrets()
   {
      using var harness = new CliTestHarness();
      Directory.CreateDirectory(Path.Combine(harness.Dir, ".kamal"));
      File.WriteAllText(Path.Combine(harness.Dir, ".kamal", "secrets"), "FOO=bar\n");

      var exitCode = await harness.Run("secrets", "print");

      Assert.Equal(0, exitCode);
      Assert.Contains("FOO=bar", harness.Output);
   }

   [Fact]
   public void InlineHandlerExtractsInProcess()
   {
      var result = SecretsCli.HandleInline(["secrets", "extract", "PASS", """{"PASS":"secret123"}""", "--inline"]);

      Assert.Equal("secret123", result);
   }
}
