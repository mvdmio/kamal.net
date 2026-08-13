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
      return HandleMissingLock(async () => Console.WriteLine(await CaptureLockStatus().ConfigureAwait(false)));
   }

   /// <summary>Port of <c>acquire -m MESSAGE</c>.</summary>
   public async Task Acquire(string message)
   {
      await EnsureRunDirectory().ConfigureAwait(false);

      await RaiseIfLocked(async () =>
      {
         await ExecuteLockAcquire(message).ConfigureAwait(false);
         Say("Acquired the deploy lock");
      }).ConfigureAwait(false);
   }

   /// <summary>Port of <c>release</c>.</summary>
   public Task Release()
   {
      return HandleMissingLock(async () =>
      {
         await ExecuteLockRelease().ConfigureAwait(false);
         Say("Released the deploy lock");
      });
   }

   private async Task HandleMissingLock(Func<Task> action)
   {
      try
      {
         await action().ConfigureAwait(false);
      }
      catch (LockMissingError)
      {
         Say("There is no deploy lock");
      }
   }
}
