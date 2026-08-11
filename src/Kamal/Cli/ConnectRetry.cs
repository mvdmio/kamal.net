using Kamal.Utils;

namespace Kamal.Cli;

/// <summary>
/// Opt-in connect-only retry for <c>kamal deploy</c>. Off by default (single attempt). When
/// enabled, re-runs the full deploy action when the failure <em>class</em> is
/// <see cref="FailureClass.Connect"/>, with exponential backoff between attempts.
/// “Connect-only” means a class filter (never auth/build/healthcheck/lock/generic) — not
/// phase-level resume of only the connect phase. Auth, build, healthcheck, lock, and generic
/// failures are never retried.
/// </summary>
public static class ConnectRetry
{
   /// <summary>Default max attempts when <c>--retry</c> is set without an explicit N.</summary>
   public const int DefaultMaxAttempts = 3;

   /// <summary>
   /// Delay used between attempts. Replace in tests to assert backoff without wall-clock waits.
   /// Restored by tests that own it; production uses <see cref="Task.Delay(TimeSpan)"/>.
   /// </summary>
   internal static Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } =
      static (delay, cancellationToken) => Task.Delay(delay, cancellationToken);

   /// <summary>
   /// Exponential backoff after the n-th failed attempt (1-based): 1s, 2s, 4s, … capped at 30s.
   /// </summary>
   public static TimeSpan BackoffDelay(int failedAttemptNumber)
   {
      if (failedAttemptNumber < 1)
         failedAttemptNumber = 1;

      var seconds = Math.Min(30, Math.Pow(2, failedAttemptNumber - 1));
      return TimeSpan.FromSeconds(seconds);
   }

   /// <summary>
   /// Resolves CLI <c>--retry</c>: absent → 1 (disabled); present without N →
   /// <see cref="DefaultMaxAttempts"/>; present with N → max(1, N).
   /// </summary>
   public static int ResolveMaxAttempts(bool optionPresent, int? explicitAttempts)
   {
      if (!optionPresent)
         return 1;

      if (explicitAttempts is null)
         return DefaultMaxAttempts;

      return Math.Max(1, explicitAttempts.Value);
   }

   /// <summary>
   /// Runs <paramref name="action"/> up to <paramref name="maxAttempts"/> times when failures
   /// classify as connect. Non-connect failures and the final connect failure rethrow unchanged
   /// so <see cref="KamalCli.ReportFailure"/> keeps the original class, exit code, and markers.
   /// </summary>
   public static async Task RunAsync(int maxAttempts, Func<Task> action, CancellationToken cancellationToken = default)
   {
      if (maxAttempts < 1)
         maxAttempts = 1;

      for (var attempt = 1; attempt <= maxAttempts; attempt++)
      {
         if (attempt > 1)
            KamalRuntime.Commander.Connected = false;

         try
         {
            await action().ConfigureAwait(false);
            return;
         }
         catch (Exception exception) when (
            attempt < maxAttempts
            && FailureClasses.Classify(exception) == FailureClass.Connect)
         {
            var delay = BackoffDelay(attempt);
            Console.WriteLine(
               $"Connect failure (attempt {attempt} of {maxAttempts}); retrying in {RetryHelpers.FormatDelay(delay)}...");
            await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
         }
      }
   }
}
