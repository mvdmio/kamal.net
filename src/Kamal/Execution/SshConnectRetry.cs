using Kamal.Cli;
using Kamal.Utils;

namespace Kamal.Execution;

/// <summary>
/// Automatic re-attempts of opening an SSH session after a <see cref="FailureClass.Connect"/>
/// failure (SSH connect retry). Fixed three attempts with short backoff; never retries auth.
/// Distinct from <see cref="ConnectRetry"/> (deploy connect retry), which re-runs the full deploy.
/// </summary>
public static class SshConnectRetry
{
   /// <summary>Total session-open attempts before surfacing the last connect failure.</summary>
   public const int MaxAttempts = 3;

   /// <summary>
   /// Delay used between attempts. Replace in tests to assert backoff without wall-clock waits.
   /// Restored by tests that own it; production uses <see cref="Task.Delay(TimeSpan)"/>.
   /// </summary>
   internal static Func<TimeSpan, CancellationToken, Task> DelayAsync { get; set; } =
      static (delay, cancellationToken) => Task.Delay(delay, cancellationToken);

   /// <summary>
   /// Fixed backoff after the n-th failed attempt (1-based): 1s after the first failure, 2s after
   /// the second. Further values double (unused with <see cref="MaxAttempts"/> = 3).
   /// </summary>
   public static TimeSpan BackoffDelay(int failedAttemptNumber)
   {
      if (failedAttemptNumber < 1)
         failedAttemptNumber = 1;

      var seconds = Math.Pow(2, failedAttemptNumber - 1);
      return TimeSpan.FromSeconds(seconds);
   }

   /// <summary>
   /// Runs <paramref name="open"/> up to <see cref="MaxAttempts"/> times when failures classify as
   /// connect. Auth and other non-connect failures fail on the first attempt. The final connect
   /// failure rethrows unchanged so CLI exit codes and markers stay the same.
   /// </summary>
   public static async Task<T> RunAsync<T>(
      string host,
      Func<CancellationToken, Task<T>> open,
      CancellationToken cancellationToken = default)
   {
      for (var attempt = 1; attempt <= MaxAttempts; attempt++)
      {
         try
         {
            return await open(cancellationToken).ConfigureAwait(false);
         }
         catch (Exception exception) when (
            attempt < MaxAttempts
            && FailureClasses.Classify(exception) == FailureClass.Connect)
         {
            var delay = BackoffDelay(attempt);
            Console.WriteLine(
               $"SSH connect failure for {host} (attempt {attempt} of {MaxAttempts}); retrying in {RetryHelpers.FormatDelay(delay)}...");
            await DelayAsync(delay, cancellationToken).ConfigureAwait(false);
         }
      }

      // Unreachable: the last attempt either returns or throws outside the when filter.
      throw new InvalidOperationException("SSH connect retry exhausted without a result or exception.");
   }
}
