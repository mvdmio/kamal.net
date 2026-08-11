namespace Kamal.Utils;

/// <summary>
/// Pure helpers shared by distinct retry policies (SSH session open vs deploy connect retry).
/// Does not merge those policies into one API.
/// </summary>
internal static class RetryHelpers
{
   /// <summary>Human-readable delay for always-on retry console lines (e.g. <c>1s</c>).</summary>
   public static string FormatDelay(TimeSpan delay)
   {
      if (delay.TotalSeconds >= 1 && Math.Abs(delay.TotalSeconds - Math.Round(delay.TotalSeconds)) < 0.001)
         return $"{(int)Math.Round(delay.TotalSeconds)}s";

      return delay.ToString();
   }
}
