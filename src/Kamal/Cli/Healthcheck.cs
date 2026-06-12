using Kamal.Execution;
using Kamal.Output;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::Healthcheck::Error</c>.</summary>
public sealed class HealthcheckError : Exception
{
   public HealthcheckError(string message) : base(message)
   {
   }
}

/// <summary>
/// Port of <c>Kamal::Cli::Healthcheck::Barrier</c> (a Concurrent::IVar): the first primary-role
/// boot opens (or closes) the barrier exactly once; other roles wait on it before booting.
/// </summary>
public sealed class HealthcheckBarrier
{
   private readonly TaskCompletionSource<bool> _state = new(TaskCreationOptions.RunContinuationsAsynchronously);

   /// <summary>Closes the barrier; returns whether this call set the state.</summary>
   public bool Close() => Set(false);

   /// <summary>Opens the barrier; returns whether this call set the state.</summary>
   public bool Open() => Set(true);

   /// <summary>Blocks until the barrier is set; raises when it was closed.</summary>
   public async Task Wait()
   {
      if (!await _state.Task.ConfigureAwait(false))
         throw new HealthcheckError("Halted at barrier");
   }

   private bool Set(bool value) => _state.TrySetResult(value);
}

/// <summary>Port of <c>Kamal::Cli::Healthcheck::Poller</c>.</summary>
public static class HealthcheckPoller
{
   /// <summary>Test hook: replaces the real delays.</summary>
   public static Func<TimeSpan, Task> Sleeper { get; set; } = delay => Task.Delay(delay);

   public static async Task WaitForHealthy(Func<Task<string>> status)
   {
      var kamal = KamalRuntime.Commander;
      var attempt = 1;
      var deployTimeout = kamal.Config.DeployTimeout;
      var timeoutAt = DateTime.UtcNow.AddSeconds(deployTimeout);
      var readinessDelay = kamal.Config.ReadinessDelay;

      while (true)
      {
         try
         {
            var current = await status().ConfigureAwait(false);

            if (current == "running" && readinessDelay > 0)
            {
               // Wait for the readiness delay and confirm it is still running
               Info($"Container is running, waiting for readiness delay of {readinessDelay} seconds");
               await Sleeper(TimeSpan.FromSeconds(readinessDelay)).ConfigureAwait(false);
               current = await status().ConfigureAwait(false);
            }

            if (current is not ("running" or "healthy"))
               throw new HealthcheckError($"container not ready after {deployTimeout} seconds ({current})");

            break;
         }
         catch (HealthcheckError)
         {
            var timeLeft = timeoutAt - DateTime.UtcNow;

            if (timeLeft > TimeSpan.Zero)
            {
               await Sleeper(TimeSpan.FromSeconds(Math.Min(attempt, timeLeft.TotalSeconds))).ConfigureAwait(false);
               attempt++;
            }
            else
            {
               throw;
            }
         }
      }

      Info("Container is healthy!");
   }

   private static void Info(string message) => KamalOutput.Logger.Log(Verbosity.Info, message);
}
