using Kamal.Execution;

namespace Kamal.Tests.Execution;

public class UploadModeTests
{
   [Theory]
   [InlineData("0600", 0x180, 600)]
   [InlineData("0644", 0x1A4, 644)]
   [InlineData("0700", 0x1C0, 700)]
   [InlineData("755", 0x1ED, 755)]
   [InlineData("1777", 0x3FF, 1777)]
   public void ParsesValidModesIntoBothRepresentations(string mode, int expectedUnixFileMode, int expectedSshOctal)
   {
      var parsed = UploadMode.Parse(mode, "/remote/path");

      Assert.Equal((UnixFileMode)expectedUnixFileMode, parsed.UnixFileMode);
      Assert.Equal(expectedSshOctal, parsed.SshOctal);
   }

   [Theory]
   [InlineData("abc")]
   [InlineData("0999")]
   [InlineData("77777")]
   [InlineData("")]
   public void RejectsInvalidModesWithModeAndPathInTheMessage(string mode)
   {
      var error = Assert.Throws<FormatException>(() => UploadMode.Parse(mode, "/remote/secrets.env"));

      Assert.Contains(mode, error.Message);
      Assert.Contains("/remote/secrets.env", error.Message);
   }
}
