namespace Kamal.Execution;

/// <summary>
/// The permission mode an upload carries, parsed once from its octal string form
/// (e.g. <c>"0600"</c>, <c>"755"</c>, <c>"1777"</c>) into the two representations its
/// consumers need: the POSIX bitmask <see cref="Kamal.Execution.UploadMode.UnixFileMode"/>
/// (what <see cref="File.SetUnixFileMode(string, UnixFileMode)"/> takes), and
/// <see cref="Kamal.Execution.UploadMode.PermissionDigits"/>, which is what SSH.NET's
/// <c>SftpClient.ChangePermissions</c> takes.
/// </summary>
/// <remarks>
/// Both representations are derived from the same parsed value, so they cannot disagree,
/// and the only way to get an instance is <see cref="Parse(string, string)"/> — there is no
/// zero-valued default that would silently chmod something to no permissions at all.
/// </remarks>
public sealed record UploadMode
{
   private UploadMode(int bits)
   {
      UnixFileMode = (UnixFileMode)bits;
      PermissionDigits = ToDecimalDigits(bits);
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
      if (!TryReadOctalDigits(mode, out var bits))
      {
         throw new FormatException(
            $"Invalid upload mode \"{mode}\" for path \"{path}\": " +
            "expected octal permission digits such as \"0600\", \"755\", or \"1777\".");
      }

      return new UploadMode(bits);
   }

   /// <summary>
   /// <see cref="Parse(string, string)"/> for an upload's optional mode: <c>null</c> in
   /// (no mode requested) yields <c>null</c> out, anything else is parsed and validated.
   /// </summary>
   public static UploadMode? ParseOptional(string? mode, string path)
   {
      return mode is null ? null : Parse(mode, path);
   }

   /// <summary>Reads at most four octal digits into the permission bits they spell.</summary>
   private static bool TryReadOctalDigits(string mode, out int bits)
   {
      bits = 0;

      if (mode.Length is 0 or > 4)
         return false;

      foreach (var digit in mode)
      {
         if (digit is < '0' or > '7')
            return false;

         bits = (bits << 3) | (digit - '0');
      }

      return true;
   }

   /// <summary>Writes the octal digits of <paramref name="bits"/> back out as a decimal number.</summary>
   private static short ToDecimalDigits(int bits)
   {
      var digits = 0;
      var place = 1;

      for (; bits != 0; bits >>= 3)
      {
         digits += (bits & 7) * place;
         place *= 10;
      }

      return (short)digits;
   }
}
