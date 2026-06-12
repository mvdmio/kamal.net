using Kamal.Utils;

namespace Kamal.Tests.Utils;

/// <summary>Tests for the Kamal::Tags port (Ruby has no dedicated tags test file).</summary>
public class KamalTagsTests
{
   private static KamalTags Tags() => new(new OrderedDictionary<string, object?>
   {
      ["recorded_at"] = "2026-06-12T00:00:00Z",
      ["performer"] = "user@example.com",
      ["destination"] = null,
      ["version"] = "123",
      ["service"] = "app"
   });

   [Fact]
   public void CompactsNilTags()
   {
      Assert.Equal(["recorded_at", "performer", "version", "service"], Tags().Tags.Keys);
   }

   [Fact]
   public void EnvUsesKamalPrefixedUppercaseKeys()
   {
      var env = Tags().Env;

      Assert.Equal("user@example.com", env["KAMAL_PERFORMER"]);
      Assert.Equal("123", env["KAMAL_VERSION"]);
      Assert.Equal("app", env["KAMAL_SERVICE"]);
   }

   [Fact]
   public void ToStringWrapsValuesInBrackets()
   {
      Assert.Equal("[2026-06-12T00:00:00Z] [user@example.com] [123] [app]", Tags().ToString());
   }

   [Fact]
   public void ExceptRemovesTags()
   {
      Assert.Equal(["performer", "version", "service"], Tags().Except("recorded_at").Tags.Keys);
   }
}
