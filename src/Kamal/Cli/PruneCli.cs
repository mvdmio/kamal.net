using Kamal.Execution;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::Prune</c>.</summary>
public sealed class PruneCli : CliBase
{
   public PruneCli(CliContext context) : base(context)
   {
   }

   /// <summary>Port of <c>all</c>.</summary>
   public Task All()
   {
      return Modify(async () =>
      {
         await Containers().ConfigureAwait(false);
         await Images().ConfigureAwait(false);
      }, requireLock: true);
   }

   /// <summary>Port of <c>images</c>.</summary>
   public Task Images()
   {
      return Modify(() =>
         On(KAMAL.Hosts, async backend =>
         {
            await backend.Execute(KAMAL.Auditor().Record("Pruned images"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(KAMAL.Prune.DanglingImages()).ConfigureAwait(false);
            await backend.Execute(KAMAL.Prune.TaggedImages()).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>containers [--retain]</c>.</summary>
   public Task Containers(int? retain = null)
   {
      var retainCount = retain ?? KAMAL.Config.RetainContainers;

      if (retainCount < 1)
         throw new ArgumentException("retain must be at least 1");

      return Modify(() =>
         On(KAMAL.Hosts, async backend =>
         {
            await backend.Execute(KAMAL.Auditor().Record("Pruned containers"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(KAMAL.Prune.AppContainers(retain: retainCount)).ConfigureAwait(false);
         }), requireLock: true);
   }
}
