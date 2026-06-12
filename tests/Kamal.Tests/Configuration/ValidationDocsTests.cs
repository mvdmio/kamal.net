using Kamal.Configuration;
using Kamal.Configuration.Validation;

namespace Kamal.Tests.Configuration;

/// <summary>Tests the embedded docs helper backing the future `kamal docs` command.</summary>
public class ValidationDocsTests
{
   [Fact]
   public void NamesListsAllEmbeddedDocs()
   {
      Assert.Equal(
         ["accessory", "alias", "boot", "builder", "configuration", "env", "logging", "output", "proxy", "registry", "role", "servers", "ssh", "sshkit"],
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
}
