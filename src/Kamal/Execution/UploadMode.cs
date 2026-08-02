using System.Globalization;

namespace Kamal.Execution;

/// <summary>
/// Parses the octal permission mode string an upload carries (e.g. <c>"0600"</c>,
/// <c>"755"</c>, <c>"1777"</c>) once per upload, and exposes both representations its
/// consumers need: the POSIX bitmask <see cref="Kamal.Execution.UploadMode.UnixFileMode"/>
/// (what <see cref="File.SetUnixFileMode(string, UnixFileMode)"/> takes), and
/// <see cref="Kamal.Execution.UploadMode.PermissionDigits"/>, which is what SSH.NET's
/// <c>SftpClient.ChangePermissions</c> takes.
/// </summary>
public readonly record struct UploadMode
{
   private UploadMode(UnixFileMode unixFileMode, short permissionDigits)
   {
      UnixFileMode = unixFileMode;
      PermissionDigits = permissionDigits;
   }

   /// <summary>The POSIX permission bitmask, for <see cref="File.SetUnixFileMode(string, UnixFileMode)"/>.</summary>
   public UnixFileMode UnixFileMode { get; }

   /// <summary>
   /// The permission digits read as a decimal number (<c>"0600"</c> → <c>600</c>,
   /// <c>"1777"</c> → <c>1777</c>), which is the form SSH.NET's
   /// <c>SftpClient.ChangePermissions</c> takes.
   /// </summary>
   public short PermissionDigits { get; }

   /// <summary>
   /// Parses <paramref name="mode"/> — octal digits, optionally with a leading zero and
   /// optionally with a fourth leading digit for setuid/setgid/sticky. Throws
   /// <see cref="FormatException"/> naming both <paramref name="mode"/> and
   /// <paramref name="path"/> (never an SSH.NET or BCL parameter name) when
   /// <paramref name="mode"/> is not a valid POSIX octal mode.
   /// </summary>
   public static UploadMode Parse(string mode, string path)
   {
      if (!IsValidOctalMode(mode))
      {
         throw new FormatException(
            $"Invalid upload mode \"{mode}\" for path \"{path}\": " +
            "expected octal permission digits such as \"0600\", \"755\", or \"1777\".");
      }

      var unixFileMode = (UnixFileMode)Convert.ToInt32(mode, 8);
      var permissionDigits = short.Parse(mode, NumberStyles.None, CultureInfo.InvariantCulture);

      return new UploadMode(unixFileMode, permissionDigits);
   }

   /// <summary>
   /// <see cref="Parse(string, string)"/> for an upload's optional mode: <c>null</c> in
   /// (no mode requested) yields <c>null</c> out, anything else is parsed and validated.
   /// </summary>
   public static UploadMode? ParseOptional(string? mode, string path)
   {
      return mode is null ? null : Parse(mode, path);
   }

   private static bool IsValidOctalMode(string mode)
   {
      if (mode.Length is 0 or > 4)
         return false;

      foreach (var c in mode)
      {
         if (c is < '0' or > '7')
            return false;
      }

      return true;
   }
}
