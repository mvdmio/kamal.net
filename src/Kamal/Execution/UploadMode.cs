using System.Globalization;

namespace Kamal.Execution;

/// <summary>
/// Parses the octal permission mode string an upload carries (e.g. <c>"0600"</c>,
/// <c>"755"</c>, <c>"1777"</c>) once, and exposes both representations its consumers need:
/// the POSIX bitmask <see cref="Kamal.Execution.UploadMode.UnixFileMode"/>
/// (what <see cref="File.SetUnixFileMode(string, UnixFileMode)"/> takes), and
/// <see cref="Kamal.Execution.UploadMode.SshOctal"/> — the octal digits read as a decimal
/// number, which is what SSH.NET's <c>SftpClient.ChangePermissions</c> expects
/// (<c>"0600"</c> → <c>600</c>, <c>"1777"</c> → <c>1777</c>).
/// </summary>
public readonly record struct UploadMode
{
   private UploadMode(UnixFileMode unixFileMode, int sshOctal)
   {
      UnixFileMode = unixFileMode;
      SshOctal = sshOctal;
   }

   /// <summary>The POSIX permission bitmask, for <see cref="File.SetUnixFileMode(string, UnixFileMode)"/>.</summary>
   public UnixFileMode UnixFileMode { get; }

   /// <summary>The octal digits read as a decimal number, for SSH.NET's <c>SftpClient.ChangePermissions</c>.</summary>
   public int SshOctal { get; }

   /// <summary>
   /// Parses <paramref name="mode"/> — octal digits, optionally with a leading zero and
   /// optionally with a fourth leading digit for setuid/setgid/sticky. Throws
   /// <see cref="FormatException"/> naming both <paramref name="mode"/> and
   /// <paramref name="remotePath"/> (never an SSH.NET or BCL parameter name) when
   /// <paramref name="mode"/> is not a valid POSIX octal mode.
   /// </summary>
   public static UploadMode Parse(string mode, string remotePath)
   {
      if (!IsValidOctalMode(mode))
      {
         throw new FormatException(
            $"Invalid upload mode \"{mode}\" for remote path \"{remotePath}\": " +
            "expected octal permission digits such as \"0600\", \"755\", or \"1777\".");
      }

      var unixFileMode = (UnixFileMode)Convert.ToInt32(mode, 8);
      var sshOctal = int.Parse(mode, NumberStyles.None, CultureInfo.InvariantCulture);

      return new UploadMode(unixFileMode, sshOctal);
   }

   private static bool IsValidOctalMode(string? mode)
   {
      if (string.IsNullOrEmpty(mode) || mode.Length > 4)
         return false;

      foreach (var c in mode)
      {
         if (c is < '0' or > '7')
            return false;
      }

      return true;
   }
}
