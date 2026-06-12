namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::Registry</c>.</summary>
public sealed class RegistryCli : CliBase
{
   public RegistryCli(CliContext context) : base(context)
   {
   }

   /// <summary>Port of <c>setup</c>.</summary>
   public async Task Setup(bool skipLocal = false, bool skipRemote = false)
   {
      if (!skipLocal)
         await EnsureDockerInstalled().ConfigureAwait(false);

      if (KAMAL.Registry.Local)
      {
         if (!skipLocal)
            await RunLocally(backend => backend.Execute(KAMAL.Registry.Setup())).ConfigureAwait(false);
      }
      else
      {
         if (!skipLocal)
            await RunLocally(backend => backend.Execute(KAMAL.Registry.Login()!)).ConfigureAwait(false);

         if (!skipRemote)
            await On(KAMAL.Hosts, backend => backend.Execute(KAMAL.Registry.Login()!)).ConfigureAwait(false);
      }
   }

   /// <summary>Port of <c>remove</c>.</summary>
   public async Task Remove(bool skipLocal = false, bool skipRemote = false)
   {
      if (KAMAL.Registry.Local)
      {
         if (!skipLocal)
            await RunLocally(backend => backend.Execute(KAMAL.Registry.Remove(), raiseOnNonZeroExit: false)).ConfigureAwait(false);
      }
      else
      {
         if (!skipLocal)
            await RunLocally(backend => backend.Execute(KAMAL.Registry.Logout())).ConfigureAwait(false);

         if (!skipRemote)
            await On(KAMAL.Hosts, backend => backend.Execute(KAMAL.Registry.Logout())).ConfigureAwait(false);
      }
   }

   /// <summary>Port of <c>login</c>.</summary>
   public Task Login(bool skipLocal = false, bool skipRemote = false)
   {
      if (KAMAL.Registry.Local)
         throw new InvalidOperationException("Cannot use login command with a local registry. Use `kamal registry setup` instead.");

      return Setup(skipLocal: skipLocal, skipRemote: skipRemote);
   }

   /// <summary>Port of <c>logout</c>.</summary>
   public Task Logout(bool skipLocal = false, bool skipRemote = false)
   {
      if (KAMAL.Registry.Local)
         throw new InvalidOperationException("Cannot use logout command with a local registry. Use `kamal registry remove` instead.");

      return Remove(skipLocal: skipLocal, skipRemote: skipRemote);
   }
}
