using Kamal.Execution;
using Kamal.Output;
using Kamal.Utils;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::App</c>.</summary>
public sealed class AppCli : CliBase
{
   public AppCli(CliContext context) : base(context)
   {
   }

   /// <summary>Port of <c>boot</c>.</summary>
   public Task Boot(string? version = null)
   {
      version ??= Options.Version;

      return Modify(async () =>
      {
         if (version is null)
            Say("Get most recent version available as an image...", Magenta);

         await UsingVersion(version ?? KAMAL.Config.LatestTag, async bootVersion =>
         {
            Say($"Start container with version {bootVersion} (or reboot if already running)...", Magenta);

            // Assets are prepared in a separate step to ensure they are on all hosts before booting
            await On(KAMAL.AppHosts, async backend =>
            {
               await new AppErrorPages(backend).Run().ConfigureAwait(false);

               foreach (var role in KAMAL.RolesOn(backend.Host))
               {
                  await new AppAssets(backend.Host, role, backend).Run().ConfigureAwait(false);
                  await new AppSslCertificates(backend.Host, role, backend).Run().ConfigureAwait(false);
               }
            }).ConfigureAwait(false);

            // Primary hosts and roles are returned first, so they can open the barrier
            var barrier = new HealthcheckBarrier();

            foreach (var hosts in HostBootGroups())
            {
               var hostList = string.Join(",", hosts);
               await RunHook("pre-app-boot", false, ("hosts", hostList)).ConfigureAwait(false);

               await OnRoles(KAMAL.Roles, hosts,
                  (backend, role) => new AppBoot(backend.Host, role, backend, bootVersion, barrier).Run(),
                  parallel: ParallelRoles).ConfigureAwait(false);

               await RunHook("post-app-boot", false, ("hosts", hostList)).ConfigureAwait(false);

               if (KAMAL.Config.Boot.Wait is { } wait)
                  await Task.Delay(TimeSpan.FromSeconds(Convert.ToDouble(wait))).ConfigureAwait(false);
            }

            // Tag once the app booted on all hosts
            await On(KAMAL.AppHosts, async backend =>
            {
               await backend.Execute(KAMAL.Auditor().Record($"Tagging {KAMAL.Config.AbsoluteImage} as the latest image"), verbosity: Verbosity.Debug).ConfigureAwait(false);
               await backend.Execute(KAMAL.App().TagLatestImage()).ConfigureAwait(false);
            }).ConfigureAwait(false);
         }).ConfigureAwait(false);
      }, requireLock: true);
   }

   /// <summary>Port of <c>start</c>.</summary>
   public Task Start()
   {
      return Modify(() =>
         OnRoles(KAMAL.Roles, KAMAL.AppHosts, async (backend, role) =>
         {
            var app = KAMAL.App(role: role, host: backend.Host);
            await backend.Execute(KAMAL.Auditor().Record($"Started app version {KAMAL.Config.Version}"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(app.Start(), raiseOnNonZeroExit: false).ConfigureAwait(false);

            if (role.RunningProxy)
            {
               var version = (await backend.CaptureWithInfo(app.CurrentRunningVersion(), raiseOnNonZeroExit: false).ConfigureAwait(false)).Trim();
               var endpoint = (await backend.CaptureWithInfo(app.ContainerIdForVersion(version)).ConfigureAwait(false)).Trim();

               if (endpoint.Length == 0)
                  throw new BootError($"Failed to get endpoint for {role} on {backend.Host}, did the container boot?");

               await backend.Execute(app.Deploy(target: endpoint)).ConfigureAwait(false);
            }
         }, parallel: ParallelRoles), requireLock: true);
   }

   /// <summary>Port of <c>stop</c>.</summary>
   public Task Stop()
   {
      return Modify(() =>
         OnRoles(KAMAL.Roles, KAMAL.AppHosts, async (backend, role) =>
         {
            var app = KAMAL.App(role: role, host: backend.Host);
            await backend.Execute(KAMAL.Auditor(new KeyValuePair<string, object?>("role", role.Name)).Record("Stopped app"), verbosity: Verbosity.Debug).ConfigureAwait(false);

            if (role.RunningProxy)
            {
               var version = (await backend.CaptureWithInfo(app.CurrentRunningVersion(), raiseOnNonZeroExit: false).ConfigureAwait(false)).Trim();
               var endpoint = (await backend.CaptureWithInfo(app.ContainerIdForVersion(version)).ConfigureAwait(false)).Trim();

               if (endpoint.Length > 0)
                  await backend.Execute(app.Remove(), raiseOnNonZeroExit: false).ConfigureAwait(false);
            }

            await backend.Execute(app.Stop(), raiseOnNonZeroExit: false).ConfigureAwait(false);
         }, parallel: ParallelRoles), requireLock: true);
   }

   /// <summary>Port of <c>details</c>.</summary>
   public Task Details()
   {
      var quiet = Options.Quiet;

      return OnRoles(KAMAL.Roles, KAMAL.AppHosts, async (backend, role) =>
         PutsByHost(backend.Host, await backend.CaptureWithInfo(KAMAL.App(role: role, host: backend.Host).Info()).ConfigureAwait(false), quiet: quiet));
   }

   /// <summary>Port of <c>exec [CMD...]</c>.</summary>
   public async Task Exec(string[] cmd, bool interactive = false, bool reuse = false, string[]? env = null, bool detach = false)
   {
      await PreConnectIfRequired().ConfigureAwait(false);

      if (detach && (interactive || reuse))
      {
         var incompatible = new List<string>();
         if (interactive) incompatible.Add("interactive");
         if (reuse) incompatible.Add("reuse");

         throw new ArgumentException($"Detach is not compatible with {string.Join(" or ", incompatible)}");
      }

      if (cmd.Length == 0)
         throw new ArgumentException("No command provided. You must specify a command to execute.");

      var command = new object[] { KamalUtils.JoinCommands(cmd) };
      var envArgs = ParseEnvPairs(env);
      var quiet = Options.Quiet;

      if (interactive && reuse)
      {
         if (Options.Version is null)
            Say("Get current version of running container...", Magenta);

         await UsingVersion(Options.Version ?? await CurrentRunningVersion().ConfigureAwait(false), version =>
         {
            Say($"Launching interactive command with version {version} via SSH from existing container on {KAMAL.PrimaryHost}...", Magenta);
            ExecLocally(KAMAL.App(role: KAMAL.PrimaryRole, host: KAMAL.PrimaryHost).ExecuteInExistingContainerOverSsh(command, envArgs));

            return Task.CompletedTask;
         }).ConfigureAwait(false);
      }
      else if (interactive)
      {
         if (Options.Version is null)
            Say("Get most recent version available as an image...", Magenta);

         await UsingVersion(VersionOrLatest(), async version =>
         {
            Say($"Launching interactive command with version {version} via SSH from new container on {KAMAL.PrimaryHost}...", Magenta);
            await On(KAMAL.PrimaryHost!, backend => ExecuteRegistryLogin(backend)).ConfigureAwait(false);
            ExecLocally(KAMAL.App(role: KAMAL.PrimaryRole, host: KAMAL.PrimaryHost).ExecuteInNewContainerOverSsh(command, envArgs));
         }).ConfigureAwait(false);
      }
      else if (reuse)
      {
         if (Options.Version is null)
            Say("Get current version of running container...", Magenta);

         await UsingVersion(Options.Version ?? await CurrentRunningVersion().ConfigureAwait(false), version =>
         {
            Say($"Launching command with version {version} from existing container...", Magenta);

            return OnRoles(KAMAL.Roles, KAMAL.AppHosts, async (backend, role) =>
            {
               await backend.Execute(KAMAL.Auditor(new KeyValuePair<string, object?>("role", role.Name)).Record($"Executed cmd '{command[0]}' on app version {version}"), verbosity: Verbosity.Debug).ConfigureAwait(false);
               PutsByHost(backend.Host, await backend.CaptureWithInfo(KAMAL.App(role: role, host: backend.Host).ExecuteInExistingContainer(command, envArgs)).ConfigureAwait(false), quiet: quiet);
            });
         }).ConfigureAwait(false);
      }
      else
      {
         if (Options.Version is null)
            Say("Get most recent version available as an image...", Magenta);

         await UsingVersion(VersionOrLatest(), async version =>
         {
            Say($"Launching command with version {version} from new container...", Magenta);
            await On(KAMAL.AppHosts, backend => ExecuteRegistryLogin(backend)).ConfigureAwait(false);

            await OnRoles(KAMAL.Roles, KAMAL.AppHosts, async (backend, role) =>
            {
               await backend.Execute(KAMAL.Auditor().Record($"Executed cmd '{command[0]}' on app version {version}"), verbosity: Verbosity.Debug).ConfigureAwait(false);
               PutsByHost(backend.Host, await backend.CaptureWithInfo(KAMAL.App(role: role, host: backend.Host).ExecuteInNewContainer(command, envArgs, detach: detach)).ConfigureAwait(false), quiet: quiet);
            }).ConfigureAwait(false);
         }).ConfigureAwait(false);
      }
   }

   /// <summary>Port of <c>containers</c>.</summary>
   public Task Containers()
   {
      var quiet = Options.Quiet;

      return On(KAMAL.AppHosts, async backend =>
         PutsByHost(backend.Host, await backend.CaptureWithInfo(KAMAL.App().ListContainers()).ConfigureAwait(false), quiet: quiet));
   }

   /// <summary>Port of <c>stale_containers</c>.</summary>
   public Task StaleContainers(bool stop = false)
   {
      var quiet = Options.Quiet;

      return WithLockIfStopping(stop, () =>
         OnRoles(KAMAL.Roles, KAMAL.AppHosts, async (backend, role) =>
         {
            var app = KAMAL.App(role: role, host: backend.Host);
            var versions = (await backend.CaptureWithInfo(app.ListVersions(), raiseOnNonZeroExit: false).ConfigureAwait(false))
               .Split('\n', StringSplitOptions.RemoveEmptyEntries)
               .ToList();
            var current = (await backend.CaptureWithInfo(app.CurrentRunningVersion(), raiseOnNonZeroExit: false).ConfigureAwait(false)).Trim();
            versions.Remove(current);

            foreach (var version in versions)
            {
               if (stop)
               {
                  PutsByHost(backend.Host, $"Stopping stale container for role {role} with version {version}", quiet: quiet);
                  await backend.Execute(app.Stop(version: version), raiseOnNonZeroExit: false).ConfigureAwait(false);
               }
               else
               {
                  PutsByHost(backend.Host, $"Detected stale container for role {role} with version {version} (use `kamal app stale_containers --stop` to stop)", quiet: quiet);
               }
            }
         }));
   }

   /// <summary>Port of <c>images</c>.</summary>
   public Task Images()
   {
      var quiet = Options.Quiet;

      return On(KAMAL.AppHosts, async backend =>
         PutsByHost(backend.Host, await backend.CaptureWithInfo(KAMAL.App().ListImages()).ConfigureAwait(false), quiet: quiet));
   }

   /// <summary>Port of <c>logs</c>.</summary>
   public async Task Logs(string? since = null, int? lines = null, string? grep = null, string? grepOptions = null, bool follow = false, bool skipTimestamps = false, string? containerId = null)
   {
      var timestamps = !skipTimestamps;
      var quiet = Options.Quiet;

      if (follow)
      {
         var followLines = lines ?? (since is not null || grep is not null ? (int?)null : 10);

         await PreConnectIfRequired().ConfigureAwait(false);
         await RunLocally(_ =>
         {
            KamalOutput.Logger.Log(Verbosity.Info, $"Following logs on {KAMAL.PrimaryHost}...");

            if (KAMAL.SpecificRoles is null)
               KAMAL.SetSpecificRoles([KAMAL.PrimaryRole!.Name]);

            var role = KAMAL.RolesOn(KAMAL.PrimaryHost!).First();

            var app = KAMAL.App(role: role, host: KAMAL.PrimaryHost);
            var command = app.FollowLogs(host: KAMAL.PrimaryHost!, containerId: containerId, timestamps: timestamps, lines: followLines, grep: grep, grepOptions: grepOptions);
            KamalOutput.Logger.Log(Verbosity.Info, command);
            ExecLocally(command);

            return Task.CompletedTask;
         }).ConfigureAwait(false);
      }
      else
      {
         var tailLines = lines ?? (since is not null || grep is not null ? (int?)null : 100);

         await OnRoles(KAMAL.Roles, KAMAL.AppHosts, async (backend, role) =>
         {
            try
            {
               PutsByHost(backend.Host, await backend.CaptureWithInfo(
                  KAMAL.App(role: role, host: backend.Host).Logs(containerId: containerId, timestamps: timestamps, since: since, lines: tailLines, grep: grep, grepOptions: grepOptions)).ConfigureAwait(false), quiet: quiet);
            }
            catch (ExecuteError)
            {
               PutsByHost(backend.Host, "Nothing found", quiet: quiet);
            }
         }).ConfigureAwait(false);
      }
   }

   /// <summary>Port of <c>remove</c>.</summary>
   public Task Remove()
   {
      return Modify(async () =>
      {
         await Stop().ConfigureAwait(false);
         await RemoveContainers().ConfigureAwait(false);
         await RemoveImages().ConfigureAwait(false);
         await RemoveAppDirectories().ConfigureAwait(false);
      }, requireLock: true);
   }

   /// <summary>Port of <c>live</c>.</summary>
   public Task Live()
   {
      return Modify(() =>
         OnRoles(KAMAL.Roles, KAMAL.ProxyHosts, async (backend, role) =>
         {
            if (role.RunningProxy)
               await backend.Execute(KAMAL.App(role: role, host: backend.Host).Live()).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>maintenance</c>.</summary>
   public Task Maintenance(int? drainTimeout = null, string? message = null)
   {
      var effectiveDrainTimeout = drainTimeout ?? KAMAL.Config.DrainTimeout;

      return Modify(() =>
         OnRoles(KAMAL.Roles, KAMAL.ProxyHosts, async (backend, role) =>
         {
            if (role.RunningProxy)
               await backend.Execute(KAMAL.App(role: role, host: backend.Host).Maintenance(drainTimeout: effectiveDrainTimeout, message: message)).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>remove_container VERSION</c>.</summary>
   public Task RemoveContainer(string version)
   {
      return Modify(() =>
         OnRoles(KAMAL.Roles, KAMAL.AppHosts, async (backend, role) =>
         {
            await backend.Execute(KAMAL.Auditor(new KeyValuePair<string, object?>("role", role.Name)).Record($"Removed app container with version {version}"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(KAMAL.App(role: role, host: backend.Host).RemoveContainer(version: version)).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>remove_containers</c>.</summary>
   public Task RemoveContainers()
   {
      return Modify(() =>
         OnRoles(KAMAL.Roles, KAMAL.AppHosts, async (backend, role) =>
         {
            await backend.Execute(KAMAL.Auditor(new KeyValuePair<string, object?>("role", role.Name)).Record("Removed all app containers"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(KAMAL.App(role: role, host: backend.Host).RemoveContainers()).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>remove_images</c>.</summary>
   public Task RemoveImages()
   {
      return Modify(() =>
         On(HostsRemovingAllRoles(), async backend =>
         {
            await backend.Execute(KAMAL.Auditor().Record("Removed all app images"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(KAMAL.App().RemoveImages()).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>remove_app_directories</c>.</summary>
   public Task RemoveAppDirectories()
   {
      return Modify(() =>
         On(HostsRemovingAllRoles(), async backend =>
         {
            await backend.Execute(KAMAL.Server.RemoveAppDirectory(), raiseOnNonZeroExit: false).ConfigureAwait(false);
            await backend.Execute(KAMAL.Auditor().Record($"Removed {KAMAL.Config.AppDirectory}"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(KAMAL.App().RemoveProxyAppDirectory(), raiseOnNonZeroExit: false).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>version</c>.</summary>
   public Task Version()
   {
      var quiet = Options.Quiet;

      return On(KAMAL.AppHosts, async backend =>
      {
         var role = KAMAL.RolesOn(backend.Host).First();
         PutsByHost(backend.Host, (await backend.CaptureWithInfo(KAMAL.App(role: role, host: backend.Host).CurrentRunningVersion()).ConfigureAwait(false)).Trim(), quiet: quiet);
      });
   }

   // ----- Private helpers --------------------------------------------------------------------

   private static bool ParallelRoles => KAMAL.Config.Boot.ParallelRoles is not (null or false);

   private static List<string> HostsRemovingAllRoles()
   {
      return KAMAL.AppHosts
         .Where(host =>
            KAMAL.RolesOn(host).Select(role => role.Name).Order(StringComparer.Ordinal)
               .SequenceEqual(KAMAL.Config.HostRoles(host).Select(role => role.Name).Order(StringComparer.Ordinal)))
         .ToList();
   }

   internal static async Task UsingVersion(string? newVersion, Func<string, Task> action)
   {
      if (newVersion is not null)
      {
         var oldVersion = KAMAL.Config.Version;
         KAMAL.Config.Version = newVersion;

         try
         {
            await action(newVersion).ConfigureAwait(false);
         }
         finally
         {
            KAMAL.Config.Version = oldVersion;
         }
      }
      else
      {
         await action(KAMAL.Config.Version).ConfigureAwait(false);
      }
   }

   private async Task<string?> CurrentRunningVersion(string? host = null)
   {
      host ??= KAMAL.PrimaryHost!;
      string? version = null;

      await On(host, async backend =>
      {
         var role = KAMAL.RolesOn(host).First();
         version = (await backend.CaptureWithInfo(KAMAL.App(role: role, host: host).CurrentRunningVersion()).ConfigureAwait(false)).Trim();
      }).ConfigureAwait(false);

      return string.IsNullOrWhiteSpace(version) ? null : version;
   }

   private string VersionOrLatest() => Options.Version ?? KAMAL.Config.LatestTag;

   private Task WithLockIfStopping(bool stop, Func<Task> action)
   {
      return stop ? Modify(action, requireLock: true) : action();
   }

   private List<List<string>> HostBootGroups()
   {
      var limit = KAMAL.Config.Boot.Limit;

      return limit is null
         ? [KAMAL.AppHosts]
         : KAMAL.AppHosts.Chunk(Convert.ToInt32(limit)).Select(group => group.ToList()).ToList();
   }

   private static async Task ExecuteRegistryLogin(IBackend backend)
   {
      if (KAMAL.Registry.Login() is { } login)
         await backend.Execute(login).ConfigureAwait(false);
   }
}
