namespace Kamal.Execution;

/// <summary>
/// Output verbosity levels, mirroring Ruby's <c>Logger</c> severities as used by SSHKit
/// (<c>SSHKit.config.output_verbosity</c>) and Kamal (<c>:debug</c>, <c>:info</c>, <c>:error</c>).
/// </summary>
public enum Verbosity
{
   Debug = 0,
   Info = 1,
   Warn = 2,
   Error = 3,
   Fatal = 4
}

public static class VerbosityHelpers
{
   /// <summary>Parses a Ruby-style verbosity symbol ("debug", "info", "warn", "error", "fatal").</summary>
   public static Verbosity Parse(string value)
   {
      return value.ToLowerInvariant() switch
      {
         "debug" => Verbosity.Debug,
         "info" => Verbosity.Info,
         "warn" => Verbosity.Warn,
         "error" => Verbosity.Error,
         "fatal" => Verbosity.Fatal,
         _ => throw new ArgumentException($"Unknown verbosity: {value}", nameof(value))
      };
   }

   /// <summary>The right-aligned label SSHKit's pretty formatter prints (" DEBUG", "  INFO", ...).</summary>
   public static string Label(this Verbosity verbosity)
   {
      return verbosity switch
      {
         Verbosity.Debug => " DEBUG",
         Verbosity.Info => "  INFO",
         Verbosity.Warn => "  WARN",
         Verbosity.Error => " ERROR",
         _ => " FATAL"
      };
   }
}
