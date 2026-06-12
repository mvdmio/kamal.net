using System.Collections.Concurrent;
using Kamal.Execution;
using Kamal.Output;
using Kamal.Utils;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::Accessory</c>.</summary>
public sealed class AccessoryCli : CliBase
{
   public AccessoryCli(CliContext context) : base(context)
   {
   }

   /// <summary>Port of <c>boot NAME</c>.</summary>
   public Task Boot(string name, bool prepare = true)
   {
      return Modify(async () =>
      {
         if (name == "all")
         {
            foreach (var accessoryName in KAMAL.AccessoryNames)
               await Boot(accessoryName).ConfigureAwait(false);
         }
         else
         {
            if (prepare)
               await Prepare(name).ConfigureAwait(false);

            await WithAccessory(name, async (accessory, hosts) =>
            {
               var bootedHosts = new ConcurrentBag<string>();

               await On(hosts, async backend =>
               {
                  var info = (await backend.CaptureWithInfo(accessory.Info(all: true, quiet: true)).ConfigureAwait(false)).Trim();

                  if (info.Length > 0)
                     bootedHosts.Add(backend.Host);
               }).ConfigureAwait(false);

               if (!bootedHosts.IsEmpty)
               {
                  Say($"Skipping booting `{name}` on {string.Join(", ", bootedHosts.Order(StringComparer.Ordinal))}, a container already exists", Yellow);
                  hosts = hosts.Where(host => !bootedHosts.Contains(host)).ToList();
               }

               await Directories(name).ConfigureAwait(false);
               await Upload(name).ConfigureAwait(false);

               await On(hosts, async backend =>
               {
                  await backend.Execute(KAMAL.Auditor().Record($"Booted {name} accessory"), verbosity: Verbosity.Debug).ConfigureAwait(false);
                  await backend.Execute(accessory.EnsureEnvDirectory()).ConfigureAwait(false);

                  using (var secrets = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(accessory.SecretsIo)))
                     await backend.Upload(secrets, accessory.SecretsPath, mode: "0600").ConfigureAwait(false);

                  await backend.Execute(accessory.Run(host: backend.Host)).ConfigureAwait(false);

                  if (accessory.RunningProxy)
                  {
                     var target = (await backend.CaptureWithInfo(accessory.ContainerIdFor(containerName: accessory.ServiceName, onlyRunning: true)).ConfigureAwait(false)).Trim();
                     await backend.Execute(accessory.Deploy(target: target)).ConfigureAwait(false);
                  }
               }).ConfigureAwait(false);
            }).ConfigureAwait(false);
         }
      }, requireLock: true);
   }

   /// <summary>Port of <c>upload NAME</c>.</summary>
   public Task Upload(string name)
   {
      return Modify(() =>
         WithAccessory(name, (accessory, hosts) =>
            On(hosts, async backend =>
            {
               foreach (var (local, pathConfig) in accessory.AccessoryConfig.Files)
               {
                  var remote = pathConfig.HostPath;
                  accessory.EnsureLocalFilePresent(local);

                  await backend.Execute(accessory.MakeDirectoryFor(remote)).ConfigureAwait(false);
                  await backend.Upload(local, remote).ConfigureAwait(false);
                  await backend.Execute(["chmod", pathConfig.Mode, remote]).ConfigureAwait(false);

                  if (pathConfig.Owner is not null)
                     await backend.Execute(["chown", pathConfig.Owner, remote]).ConfigureAwait(false);
               }
            })), requireLock: true);
   }

   /// <summary>Port of <c>directories NAME</c>.</summary>
   public Task Directories(string name)
   {
      return Modify(() =>
         WithAccessory(name, (accessory, hosts) =>
            On(hosts, async backend =>
            {
               foreach (var (local, pathConfig) in accessory.AccessoryConfig.Directories)
               {
                  await backend.Execute(accessory.MakeDirectory(local)).ConfigureAwait(false);

                  if (pathConfig.Mode is not null)
                     await backend.Execute(["chmod", pathConfig.Mode, local]).ConfigureAwait(false);

                  if (pathConfig.Owner is not null)
                     await backend.Execute(["chown", pathConfig.Owner, local]).ConfigureAwait(false);
               }
            })), requireLock: true);
   }

   /// <summary>Port of <c>reboot NAME</c>.</summary>
   public Task Reboot(string name)
   {
      return Modify(async () =>
      {
         if (name == "all")
         {
            foreach (var accessoryName in KAMAL.AccessoryNames)
               await Reboot(accessoryName).ConfigureAwait(false);
         }
         else
         {
            await Prepare(name).ConfigureAwait(false);
            await PullImage(name).ConfigureAwait(false);
            await Stop(name).ConfigureAwait(false);
            await RemoveContainer(name).ConfigureAwait(false);
            await Boot(name, prepare: false).ConfigureAwait(false);
         }
      }, requireLock: true);
   }

   /// <summary>Port of <c>start NAME</c>.</summary>
   public Task Start(string name)
   {
      return Modify(() =>
         WithAccessory(name, (accessory, hosts) =>
            On(hosts, async backend =>
            {
               await backend.Execute(KAMAL.Auditor().Record($"Started {name} accessory"), verbosity: Verbosity.Debug).ConfigureAwait(false);
               await backend.Execute(accessory.Start()).ConfigureAwait(false);

               if (accessory.RunningProxy)
               {
                  var target = (await backend.CaptureWithInfo(accessory.ContainerIdFor(containerName: accessory.ServiceName, onlyRunning: true)).ConfigureAwait(false)).Trim();
                  await backend.Execute(accessory.Deploy(target: target)).ConfigureAwait(false);
               }
            })), requireLock: true);
   }

   /// <summary>Port of <c>stop NAME</c>.</summary>
   public Task Stop(string name)
   {
      return Modify(() =>
         WithAccessory(name, (accessory, hosts) =>
            On(hosts, async backend =>
            {
               await backend.Execute(KAMAL.Auditor().Record($"Stopped {name} accessory"), verbosity: Verbosity.Debug).ConfigureAwait(false);
               await backend.Execute(accessory.Stop(), raiseOnNonZeroExit: false).ConfigureAwait(false);

               if (accessory.RunningProxy)
               {
                  var target = (await backend.CaptureWithInfo(accessory.ContainerIdFor(containerName: accessory.ServiceName, onlyRunning: true)).ConfigureAwait(false)).Trim();

                  if (target.Length > 0)
                     await backend.Execute(accessory.Remove()).ConfigureAwait(false);
               }
            })), requireLock: true);
   }

   /// <summary>Port of <c>restart NAME</c>.</summary>
   public Task Restart(string name)
   {
      return Modify(async () =>
      {
         await Stop(name).ConfigureAwait(false);
         await Start(name).ConfigureAwait(false);
      }, requireLock: true);
   }

   /// <summary>Port of <c>details NAME</c>.</summary>
   public async Task Details(string name)
   {
      var quiet = Options.Quiet;

      if (name == "all")
      {
         foreach (var accessoryName in KAMAL.AccessoryNames)
            await Details(accessoryName).ConfigureAwait(false);
      }
      else
      {
         var type = $"Accessory {name}";

         await WithAccessory(name, (accessory, hosts) =>
            On(hosts, async backend =>
               PutsByHost(backend.Host, await backend.CaptureWithInfo(accessory.Info()).ConfigureAwait(false), type: type, quiet: quiet))).ConfigureAwait(false);
      }
   }

   /// <summary>Port of <c>exec NAME [CMD...]</c>.</summary>
   public async Task Exec(string name, string[] cmd, bool interactive = false, bool reuse = false)
   {
      await PreConnectIfRequired().ConfigureAwait(false);

      var command = new object[] { KamalUtils.JoinCommands(cmd) };
      var quiet = Options.Quiet;

      await WithAccessory(name, async (accessory, hosts) =>
      {
         if (interactive && reuse)
         {
            Say("Launching interactive command via SSH from existing container...", Magenta);
            ExecLocally(accessory.ExecuteInExistingContainerOverSsh(command));
         }
         else if (interactive)
         {
            Say("Launching interactive command via SSH from new container...", Magenta);
            await On(accessory.Hosts.First(), backend => ExecuteRegistryLogin(backend)).ConfigureAwait(false);
            ExecLocally(accessory.ExecuteInNewContainerOverSsh(command));
         }
         else if (reuse)
         {
            Say("Launching command from existing container...", Magenta);
            await On(hosts, async backend =>
            {
               await backend.Execute(KAMAL.Auditor().Record($"Executed cmd '{command[0]}' on {name} accessory"), verbosity: Verbosity.Debug).ConfigureAwait(false);
               PutsByHost(backend.Host, await backend.CaptureWithInfo(accessory.ExecuteInExistingContainer(command)).ConfigureAwait(false), quiet: quiet);
            }).ConfigureAwait(false);
         }
         else
         {
            Say("Launching command from new container...", Magenta);
            await On(hosts, async backend =>
            {
               await ExecuteRegistryLogin(backend).ConfigureAwait(false);
               await backend.Execute(KAMAL.Auditor().Record($"Executed cmd '{command[0]}' on {name} accessory"), verbosity: Verbosity.Debug).ConfigureAwait(false);
               PutsByHost(backend.Host, await backend.CaptureWithInfo(accessory.ExecuteInNewContainer(command)).ConfigureAwait(false), quiet: quiet);
            }).ConfigureAwait(false);
         }
      }).ConfigureAwait(false);
   }

   /// <summary>Port of <c>logs NAME</c>.</summary>
   public Task Logs(string name, string? since = null, int? lines = null, string? grep = null, string? grepOptions = null, bool follow = false, bool skipTimestamps = false)
   {
      return WithAccessory(name, async (accessory, hosts) =>
      {
         var timestamps = !skipTimestamps;

         if (follow)
         {
            await PreConnectIfRequired().ConfigureAwait(false);
            await RunLocally(_ =>
            {
               KamalOutput.Logger.Log(Verbosity.Info, $"Following logs on {string.Join(", ", hosts)}...");
               var command = accessory.FollowLogs(timestamps: timestamps, grep: grep, grepOptions: grepOptions);
               KamalOutput.Logger.Log(Verbosity.Info, command);
               ExecLocally(command);

               return Task.CompletedTask;
            }).ConfigureAwait(false);
         }
         else
         {
            var tailLines = lines ?? (since is not null || grep is not null ? (int?)null : 100);

            await On(hosts, async backend =>
               Console.WriteLine(await backend.CaptureWithInfo(accessory.Logs(timestamps: timestamps, since: since, lines: tailLines, grep: grep, grepOptions: grepOptions)).ConfigureAwait(false))).ConfigureAwait(false);
         }
      });
   }

   /// <summary>Port of <c>pull_image NAME</c>.</summary>
   public Task PullImage(string name)
   {
      return Modify(() =>
         WithAccessory(name, (accessory, hosts) =>
            On(hosts, async backend =>
            {
               await backend.Execute(KAMAL.Auditor().Record($"Pull {name} accessory image"), verbosity: Verbosity.Debug).ConfigureAwait(false);
               await backend.Execute(accessory.PullImage()).ConfigureAwait(false);
            })), requireLock: true);
   }

   /// <summary>Port of <c>remove NAME</c>.</summary>
   public Task Remove(string name)
   {
      return Confirming($"This will remove all containers, images and data directories for {name}. Are you sure?", () =>
         Modify(async () =>
         {
            if (name == "all")
            {
               foreach (var accessoryName in KAMAL.AccessoryNames)
                  await RemoveAccessory(accessoryName).ConfigureAwait(false);
            }
            else
            {
               await RemoveAccessory(name).ConfigureAwait(false);
            }
         }, requireLock: true));
   }

   /// <summary>Port of <c>remove_container NAME</c>.</summary>
   public Task RemoveContainer(string name)
   {
      return Modify(() =>
         WithAccessory(name, (accessory, hosts) =>
            On(hosts, async backend =>
            {
               await backend.Execute(KAMAL.Auditor().Record($"Remove {name} accessory container"), verbosity: Verbosity.Debug).ConfigureAwait(false);
               await backend.Execute(accessory.RemoveContainer()).ConfigureAwait(false);
            })), requireLock: true);
   }

   /// <summary>Port of <c>remove_image NAME</c>.</summary>
   public Task RemoveImage(string name)
   {
      return Modify(() =>
         WithAccessory(name, (accessory, hosts) =>
            On(hosts, async backend =>
            {
               await backend.Execute(KAMAL.Auditor().Record($"Removed {name} accessory image"), verbosity: Verbosity.Debug).ConfigureAwait(false);
               await backend.Execute(accessory.RemoveImage()).ConfigureAwait(false);
            })), requireLock: true);
   }

   /// <summary>Port of <c>remove_service_directory NAME</c>.</summary>
   public Task RemoveServiceDirectory(string name)
   {
      return Modify(() =>
         WithAccessory(name, (accessory, hosts) =>
            On(hosts, backend => backend.Execute(accessory.RemoveServiceDirectory()))), requireLock: true);
   }

   /// <summary>Port of <c>upgrade NAME</c>.</summary>
   public Task Upgrade(string name, bool rolling = false, bool? confirmed = null)
   {
      if (confirmed is true)
         Options.Confirmed = true;

      return Confirming("This will restart all accessories", () =>
         Modify(async () =>
         {
            var hostGroups = rolling
               ? KAMAL.AccessoryHosts.Select(host => (List<string>)[host]).ToList()
               : [KAMAL.AccessoryHosts];

            foreach (var hosts in hostGroups)
            {
               var hostList = string.Join(",", hosts);

               await KAMAL.WithSpecificHosts(hosts, async () =>
               {
                  Say($"Upgrading {name} accessories on {hostList}...", Magenta);
                  await Reboot(name).ConfigureAwait(false);
                  Say($"Upgraded {name} accessories on {hostList}...", Magenta);
               }).ConfigureAwait(false);
            }
         }, requireLock: true));
   }

   // ----- Private helpers --------------------------------------------------------------------

   private async Task WithAccessory(string name, Func<Commands.Accessory, List<string>, Task> action)
   {
      if (KAMAL.Config.Accessory(name) is not null)
      {
         var accessory = KAMAL.Accessory(name);
         await action(accessory, AccessoryHosts(accessory)).ConfigureAwait(false);
      }
      else
      {
         ErrorOnMissingAccessory(name);
      }
   }

   private static void ErrorOnMissingAccessory(string name)
   {
      var names = KAMAL.AccessoryNames;

      ErrorOut(
         $"No accessory by the name of '{name}'" +
         (names.Count > 0 ? $" (options: {ToSentence(names)})" : ""));
   }

   private static List<string> AccessoryHosts(Commands.Accessory accessory)
   {
      return KAMAL.AccessoryHosts.Where(accessory.Hosts.Contains).ToList();
   }

   private async Task RemoveAccessory(string name)
   {
      await Stop(name).ConfigureAwait(false);
      await RemoveContainer(name).ConfigureAwait(false);
      await RemoveImage(name).ConfigureAwait(false);
      await RemoveServiceDirectory(name).ConfigureAwait(false);
   }

   private Task Prepare(string name)
   {
      return WithAccessory(name, (accessory, hosts) =>
         On(hosts, async backend =>
         {
            if (KAMAL.Registry.Login(registryConfig: accessory.Registry) is { } login)
               await backend.Execute(login).ConfigureAwait(false);

            try
            {
               await backend.Execute(KAMAL.Docker.CreateNetwork()).ConfigureAwait(false);
            }
            catch (ExecuteError e) when (e.Message.Contains("already exists"))
            {
            }
         }));
   }

   private static async Task ExecuteRegistryLogin(IBackend backend)
   {
      if (KAMAL.Registry.Login() is { } login)
         await backend.Execute(login).ConfigureAwait(false);
   }
}
