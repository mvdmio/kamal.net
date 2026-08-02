using Kamal.Execution;

namespace Kamal.Tests.Execution;

public class UploadModeTests
{
   public static TheoryData<string, UnixFileMode, short> ValidModes => new()
   {
      { "0600", UnixFileMode.UserRead | UnixFileMode.UserWrite, 600 },
      { "0644", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, 644 },
      { "0700", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, 700 },
      {
         "755",
         UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute,
         755
      },
      {
         "1777",
         UnixFileMode.StickyBit
            | UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute,
         1777
      }
   };

   [Theory]
   [MemberData(nameof(ValidModes))]
   public void ParsesValidModesIntoBothRepresentations(string mode, UnixFileMode expectedUnixFileMode, short expectedPermissionDigits)
   {
      var parsed = UploadMode.Parse(mode, "/remote/path");

      Assert.Equal(expectedUnixFileMode, parsed.UnixFileMode);
      Assert.Equal(expectedPermissionDigits, parsed.PermissionDigits);
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

   [Fact]
   public void ParseOptionalPassesNullThrough()
   {
      Assert.Null(UploadMode.ParseOptional(null, "/remote/secrets.env"));
   }

   [Fact]
   public void ParseOptionalValidatesANonNullMode()
   {
      Assert.Equal((short?)600, UploadMode.ParseOptional("0600", "/remote/secrets.env")?.PermissionDigits);
      Assert.Throws<FormatException>(() => UploadMode.ParseOptional("0999", "/remote/secrets.env"));
   }
}
