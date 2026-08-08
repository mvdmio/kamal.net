using Kamal.Cli;
using Kamal.Configuration;
using Kamal.Configuration.Validation;

namespace Kamal.Tests.Configuration;

/// <summary>Tests the embedded docs helper backing the `kamal docs` command.</summary>
public class ValidationDocsTests
{
   [Fact]
   public void NamesListsAllEmbeddedDocs()
   {
      Assert.Equal(
         ["accessory", "alias", "boot", "builder", "ci", "configuration", "env", "logging", "output", "proxy", "registry", "role", "servers", "ssh", "sshkit"],
         ValidationDocs.Names);
   }

   [Fact]
   public void ReadReturnsTheRawYamlDocumentation()
   {
      var doc = ValidationDocs.Read("boot");

      Assert.Contains("# Booting", doc);
      Assert.Contains("limit: 25%", doc);
   }

   [Fact]
   public void ReadThrowsForUnknownSections()
   {
      Assert.Throws<KamalConfigurationError>(() => ValidationDocs.Read("nope"));
   }

   [Fact]
   public void CiDocCoversInstallSshExpansionDestinationsFailureCodesRetryAndActions()
   {
      var doc = ValidationDocs.Read("ci");

      Assert.Contains("Continuous Integration (CI)", doc);
      Assert.Contains("dotnet tool install -g mvdmio.Kamal", doc);
      Assert.Contains("KAMAL_SSH_PRIVATE_KEY", doc);
      Assert.Contains("key_data", doc);
      Assert.Contains("ssh-agent", doc);
      Assert.Contains("${ENV_VAR}", doc);
      Assert.Contains("${ENV_VAR:-default}", doc);
      Assert.Contains("migrating from ERB", doc, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("destination", doc, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("kamal.failure_class=", doc);
      Assert.Contains("--retry", doc);
      Assert.Contains("mvdmio/kamal.net/actions/setup", doc);
      Assert.Contains("mvdmio/kamal.net/actions/deploy", doc);

      // Exit-code table must match the public FailureClasses contract from step 04.
      Assert.Contains($"| generic       | {FailureClasses.ExitGeneric}", doc);
      Assert.Contains($"| connect       | {FailureClasses.ExitConnect}", doc);
      Assert.Contains($"| auth          | {FailureClasses.ExitAuth}", doc);
      Assert.Contains($"| build         | {FailureClasses.ExitBuild}", doc);
      Assert.Contains($"| healthcheck   | {FailureClasses.ExitHealthcheck}", doc);
      Assert.Contains($"| lock          | {FailureClasses.ExitLock}", doc);
   }
}
