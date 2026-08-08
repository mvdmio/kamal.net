using Kamal.Configuration;
using Kamal.Utils;
using Cfg = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Kamal.Tests.Configuration;

/// <summary>Config expansion after YAML load (ADR 0002 / step 03).</summary>
[Collection("kamal-config")]
public class ConfigExpansionTests
{
   private const string BaseDeployYaml = """
      service: app
      image: dhh/app
      registry:
        username: dhh
        password: secret
      builder:
        arch: amd64
      servers:
        - 1.1.1.1
      """;

   [Fact]
   public void ExpandString_SubstitutesSetVariable()
   {
      using var env = new EnvVarScope("KAMALNET_EXPAND_SET", "from-env");

      Assert.Equal("from-env", ConfigExpansion.ExpandString("${KAMALNET_EXPAND_SET}"));
      Assert.Equal("prefix-from-env-suffix", ConfigExpansion.ExpandString("prefix-${KAMALNET_EXPAND_SET}-suffix"));
   }

   [Fact]
   public void ExpandString_BareUnsetFails()
   {
      using var env = new EnvVarScope("KAMALNET_EXPAND_MISSING", null);

      var error = Assert.Throws<KamalConfigurationError>(
         () => ConfigExpansion.ExpandString("${KAMALNET_EXPAND_MISSING}"));

      Assert.Contains("KAMALNET_EXPAND_MISSING", error.Message);
      Assert.Contains("is not set", error.Message);
   }

   [Fact]
   public void ExpandString_DefaultWhenUnset()
   {
      using var env = new EnvVarScope("KAMALNET_EXPAND_OPT", null);

      Assert.Equal("fallback", ConfigExpansion.ExpandString("${KAMALNET_EXPAND_OPT:-fallback}"));
      Assert.Equal("", ConfigExpansion.ExpandString("${KAMALNET_EXPAND_OPT:-}"));
   }

   [Fact]
   public void ExpandString_SetVariableWinsOverDefault()
   {
      using var env = new EnvVarScope("KAMALNET_EXPAND_OPT", "real");

      Assert.Equal("real", ConfigExpansion.ExpandString("${KAMALNET_EXPAND_OPT:-fallback}"));
      Assert.Equal("real", ConfigExpansion.ExpandString("${KAMALNET_EXPAND_OPT:-}"));
   }

   [Fact]
   public void ExpandString_SetEmptyIsNotUnset()
   {
      using var env = new EnvVarScope("KAMALNET_EXPAND_EMPTY", "");

      Assert.Equal("", ConfigExpansion.ExpandString("${KAMALNET_EXPAND_EMPTY}"));
      Assert.Equal("", ConfigExpansion.ExpandString("${KAMALNET_EXPAND_EMPTY:-fallback}"));
   }

   [Fact]
   public void Expand_WalksNestedStringsLeavesNonStrings()
   {
      using var env = new EnvVarScope("KAMALNET_EXPAND_NEST", "nested-value");

      var config = new Cfg
      {
         ["service"] = "app-${KAMALNET_EXPAND_NEST}",
         ["count"] = 42,
         ["enabled"] = true,
         ["missing"] = null,
         ["builder"] = new Cfg
         {
            ["arch"] = "amd64",
            ["args"] = new Cfg { ["COMMIT_SHA"] = "${KAMALNET_EXPAND_NEST}" }
         },
         ["servers"] = new List<object?> { "1.1.1.1", "${KAMALNET_EXPAND_NEST}.example" },
         ["labels"] = new Cfg { ["numeric"] = 7, ["flag"] = false }
      };

      ConfigExpansion.Expand(config);

      Assert.Equal("app-nested-value", config["service"]);
      Assert.Equal(42, config["count"]);
      Assert.Equal(true, config["enabled"]);
      Assert.Null(config["missing"]);
      Assert.Equal("nested-value", config.Dig("builder", "args", "COMMIT_SHA"));
      Assert.Equal(42, config["count"]);

      var servers = Assert.IsType<List<object?>>(config["servers"]);
      Assert.Equal("1.1.1.1", servers[0]);
      Assert.Equal("nested-value.example", servers[1]);

      var labels = Assert.IsType<Cfg>(config["labels"]);
      Assert.Equal(7, labels["numeric"]);
      Assert.Equal(false, labels["flag"]);
   }

   [Fact]
   public void LoadRawConfig_ExpandsAfterDestinationMerge()
   {
      using var fixtures = new FixtureDir();
      using var commit = new EnvVarScope("KAMALNET_EXPAND_COMMIT", "abc123");
      using var destTag = new EnvVarScope("KAMALNET_EXPAND_DEST", "world-tag");

      // Base has a placeholder that destination may override; destination introduces another.
      var configFile = fixtures.Write(
         "deploy_expand.yml",
         BaseDeployYaml + """

            env:
              COMMIT: ${KAMALNET_EXPAND_COMMIT}
              TAG: base-tag
            """);

      fixtures.Write(
         "deploy_expand.world.yml",
         """
         env:
           TAG: ${KAMALNET_EXPAND_DEST}
           EXTRA: from-destination-${KAMALNET_EXPAND_COMMIT}
         """);

      var raw = KamalConfiguration.LoadRawConfig(configFile, destination: "world");
      var env = Assert.IsAssignableFrom<IDictionary<string, object?>>(raw["env"]);

      Assert.Equal("abc123", env["COMMIT"]);
      Assert.Equal("world-tag", env["TAG"]);
      Assert.Equal("from-destination-abc123", env["EXTRA"]);
   }

   [Fact]
   public void LoadRawConfig_DestinationCanSupplyValueExpandedFromEnv()
   {
      using var fixtures = new FixtureDir();
      using var host = new EnvVarScope("KAMALNET_EXPAND_HOST", "10.0.0.9");

      var configFile = fixtures.Write("deploy_expand_host.yml", BaseDeployYaml.Replace("1.1.1.1", "1.1.1.1"));
      // Override servers only in destination with expanded host.
      fixtures.Write(
         "deploy_expand_host.staging.yml",
         """
         servers:
           - ${KAMALNET_EXPAND_HOST}
         """);

      var config = KamalConfiguration.CreateFrom(configFile, destination: "staging");
      Assert.Equal(["10.0.0.9"], config.AllHosts);
   }

   [Fact]
   public void LoadRawConfig_DoesNotExpandFromSecretsMap()
   {
      using var fixtures = new FixtureDir();
      using var secrets = new TestSecrets("MY_SECRET=from-secrets-file\n");
      using var clear = new EnvVarScope("MY_SECRET", null);

      var configFile = fixtures.Write(
         "deploy_no_secret_expand.yml",
         BaseDeployYaml + """

            env:
              plain: ${MY_SECRET}
            """);

      // Expansion uses process environment only; MY_SECRET is unset → load error even though
      // a secrets file defines the same name.
      var error = Assert.Throws<KamalConfigurationError>(
         () => KamalConfiguration.LoadRawConfig(configFile));

      Assert.Contains("MY_SECRET", error.Message);

      // Control: secrets still resolve by name-reference after a successful load.
      Assert.Equal("from-secrets-file", secrets.Secrets["MY_SECRET"]);
   }

   [Fact]
   public void CreateFrom_BareUnsetFailsAtLoad()
   {
      using var fixtures = new FixtureDir();
      using var env = new EnvVarScope("KAMALNET_EXPAND_REQUIRED", null);

      var configFile = fixtures.Write(
         "deploy_required_env.yml",
         BaseDeployYaml + """

            builder:
              arch: amd64
              args:
                SHA: ${KAMALNET_EXPAND_REQUIRED}
            """);

      var error = Assert.Throws<KamalConfigurationError>(
         () => KamalConfiguration.CreateFrom(configFile));

      Assert.Contains("KAMALNET_EXPAND_REQUIRED", error.Message);
   }

   [Fact]
   public void ExpandString_LeavesUnrelatedTextAndUnknownSyntax()
   {
      Assert.Equal("no placeholders", ConfigExpansion.ExpandString("no placeholders"));
      Assert.Equal("$HOME and ${}", ConfigExpansion.ExpandString("$HOME and ${}"));
      // ${VAR:default} without hyphen is not the supported form — left as-is.
      Assert.Equal("${VAR:default}", ConfigExpansion.ExpandString("${VAR:default}"));
   }
}
