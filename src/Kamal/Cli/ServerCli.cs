using System.Collections.Concurrent;
using Kamal.Execution;
using Kamal.Output;
using Kamal.Utils;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::Server</c>.</summary>
public sealed class ServerCli : CliBase
{
   public ServerCli(CliContext context) : base(context)
   {
   }

   /// <summary>Port of <c>exec CMD</c>.</summary>
   public async Task Exec(string[] cmd, bool interactive = false)
   {
      await PreConnectIfRequired().ConfigureAwait(false);

      var command = KamalUtils.JoinCommands(cmd);
      var hosts = KAMAL.Hosts;
      var quiet = Options.Quiet;

      if (interactive)
      {
         var host = KAMAL.PrimaryHost!;

         Say($"Running '{command}' on {host} interactively...", Magenta);

         ExecLocally(KAMAL.Server.RunOverSsh(command, host: host));
      }
      else
      {
         Say($"Running '{command}' on {string.Join(", ", hosts)}...", Magenta);

         await On(hosts, async backend =>
         {
            await backend.Execute(KAMAL.Auditor().Record($"Executed cmd '{command}' on {backend.Host}"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            PutsByHost(backend.Host, await backend.CaptureWithInfo([command]).ConfigureAwait(false), quiet: quiet);
         }).ConfigureAwait(false);
      }
   }

   /// <summary>Port of <c>bootstrap</c>.</summary>
   public Task Bootstrap()
   {
      return Modify(async () =>
      {
         var missing = new ConcurrentBag<string>();

         await On(KAMAL.Hosts, async backend =>
         {
            if (!await backend.Test(KAMAL.Docker.Installed()).ConfigureAwait(false))
            {
               if (await backend.Test(KAMAL.Docker.Superuser()).ConfigureAwait(false))
               {
                  KamalOutput.Logger.Log(Verbosity.Info, $"Missing Docker on {backend.Host}. Installing…", backend.Host);
                  await backend.Execute(KAMAL.Docker.Install()).ConfigureAwait(false);

                  if (!await backend.Test(KAMAL.Docker.Root()).ConfigureAwait(false) &&
                      !await backend.Test(KAMAL.Docker.InDockerGroup()).ConfigureAwait(false))
                  {
                     await backend.Execute(KAMAL.Docker.AddToDockerGroup()).ConfigureAwait(false);

                     try
                     {
                        await backend.Execute(KAMAL.Docker.RefreshSession()).ConfigureAwait(false);
                     }
                     catch (ExecuteError)
                     {
                        // The session is killed by the HUP; Ruby rescues the resulting IOError.
                        KamalOutput.Logger.Log(Verbosity.Info, "Session refreshed due to group change.", backend.Host);
                     }
                  }
               }
               else
               {
                  missing.Add(backend.Host);
               }
            }
         }).ConfigureAwait(false);

         if (!missing.IsEmpty)
         {
            throw new InvalidOperationException(
               $"Docker is not installed on {string.Join(", ", missing.Order(StringComparer.Ordinal))} and can't be automatically installed without having root access and either `wget` or `curl`. Install Docker manually: https://docs.docker.com/engine/install/");
         }

         await RunHook("docker-setup").ConfigureAwait(false);
      }, requireLock: true);
   }
}
