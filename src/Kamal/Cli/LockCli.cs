using Kamal.Execution;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::Lock</c>.</summary>
public sealed class LockCli : CliBase
{
   public LockCli(CliContext context) : base(context)
   {
   }

   /// <summary>Port of <c>status</c>.</summary>
   public Task Status()
   {
      return HandleMissingLock(() =>
         On(KAMAL.PrimaryHost!, async backend =>
            Console.WriteLine(await backend.CaptureWithDebug(KAMAL.Lock.Status()).ConfigureAwait(false))));
   }

   /// <summary>Port of <c>acquire -m MESSAGE</c>.</summary>
   public async Task Acquire(string message)
   {
      await EnsureRunDirectory().ConfigureAwait(false);

      await RaiseIfLocked(async () =>
      {
         await On(KAMAL.PrimaryHost!, backend =>
            backend.Execute(KAMAL.Lock.Acquire(message, KAMAL.Config.Version), verbosity: Verbosity.Debug)).ConfigureAwait(false);

         Say("Acquired the deploy lock");
      }).ConfigureAwait(false);
   }

   /// <summary>Port of <c>release</c>.</summary>
   public Task Release()
   {
      return HandleMissingLock(async () =>
      {
         await On(KAMAL.PrimaryHost!, backend =>
            backend.Execute(KAMAL.Lock.Release(), verbosity: Verbosity.Debug)).ConfigureAwait(false);

         Say("Released the deploy lock");
      });
   }

   private async Task HandleMissingLock(Func<Task> action)
   {
      try
      {
         await action().ConfigureAwait(false);
      }
      catch (ExecuteError e) when (e.Message.Contains("No such file or directory"))
      {
         Say("There is no deploy lock");
      }
   }
}
