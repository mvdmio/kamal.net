using System.Text;
using Kamal.Configuration;
using Kamal.Execution;
using Kamal.Utils;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::Proxy</c>.</summary>
public sealed class ProxyCli : CliBase
{
   public ProxyCli(CliContext context) : base(context)
   {
   }

   /// <summary>Port of <c>boot</c>.</summary>
   public Task Boot()
   {
      return Modify(async () =>
      {
         await On(KAMAL.Hosts, async backend =>
         {
            try
            {
               await backend.Execute(KAMAL.Docker.CreateNetwork()).ConfigureAwait(false);
            }
            catch (ExecuteError e) when (e.Message.Contains("already exists"))
            {
            }
         }).ConfigureAwait(false);

         await On(KAMAL.ProxyHosts, async backend =>
         {
            if (KAMAL.Registry.Login() is { } login)
               await backend.Execute(login).ConfigureAwait(false);

            var version = (await backend.CaptureWithInfo(KAMAL.Proxy(backend.Host).Version()).ConfigureAwait(false)).Trim();

            if (!string.IsNullOrEmpty(version) && KamalUtils.OlderVersion(version, ProxyRun.MinimumVersion))
               throw new InvalidOperationException($"kamal-proxy version {version} is too old, run `kamal proxy reboot` in order to update to at least {ProxyRun.MinimumVersion}");

            await backend.Execute(KAMAL.Proxy(backend.Host).EnsureAppsConfigDirectory()).ConfigureAwait(false);
            await backend.Execute(KAMAL.Proxy(backend.Host).StartOrRun()).ConfigureAwait(false);
         }).ConfigureAwait(false);
      }, requireLock: true);
   }

   /// <summary>Port of <c>boot_config &lt;set|get|reset&gt;</c>.</summary>
   public async Task BootConfig(
      string subcommand,
      bool publish = true,
      string[]? publishHostIp = null,
      object? httpPort = null,
      object? httpsPort = null,
      string? logMaxSize = null,
      string? registry = null,
      string? repository = null,
      string? imageVersion = null,
      int? metricsPort = null,
      bool debug = false,
      string[]? dockerOptions = null)
   {
      Say("The proxy boot_config command is deprecated - set the config in the deploy YAML at proxy/run instead", Yellow);

      var proxyBootConfig = KAMAL.Config.ProxyBoot;

      switch (subcommand)
      {
         case "set":
         {
            var bootOptions = new List<object?>();

            if (publish)
               bootOptions.Add(proxyBootConfig.PublishArgs(httpPort ?? ProxyRun.DefaultHttpPort, httpsPort ?? ProxyRun.DefaultHttpsPort, publishHostIp?.Cast<string?>().ToList()));

            if (proxyBootConfig.LoggingArgs(logMaxSize ?? ProxyRun.DefaultLogMaxSize) is { } loggingArgs)
               bootOptions.AddRange(loggingArgs);

            if (metricsPort is not null)
               bootOptions.Add($"--expose={metricsPort}");

            foreach (var option in dockerOptions ?? [])
               bootOptions.Add($"--{option}");

            var bootOptionsJoined = string.Join(" ", bootOptions.Where(option => option is not null).Select(RenderToken));
            var defaultBootOptionsJoined = string.Join(" ", proxyBootConfig.DefaultBootOptions.Select(RenderToken));

            var image = string.Join("/", new[]
            {
               RubyPresence(registry),
               RubyPresence(repository) ?? proxyBootConfig.RepositoryName,
               proxyBootConfig.ImageName
            }.Where(part => part is not null));

            var runCommandOptions = new OrderedDictionary<string, object?>();

            if (debug)
               runCommandOptions["debug"] = true;

            if (metricsPort is not null)
               runCommandOptions["metrics-port"] = metricsPort;

            var runCommand = runCommandOptions.Count > 0
               ? $"kamal-proxy run {string.Join(" ", KamalUtils.Optionize(runCommandOptions).Select(RenderToken))}"
               : null;

            await On(KAMAL.ProxyHosts, async backend =>
            {
               var proxy = KAMAL.Proxy(backend.Host);
               await backend.Execute(proxy.EnsureProxyDirectory()).ConfigureAwait(false);

               if (bootOptionsJoined != defaultBootOptionsJoined)
                  await UploadString(backend, bootOptionsJoined, proxyBootConfig.OptionsFile).ConfigureAwait(false);
               else
                  await backend.Execute(proxy.ResetBootOptions(), raiseOnNonZeroExit: false).ConfigureAwait(false);

               if (image != proxyBootConfig.ImageDefault)
                  await UploadString(backend, image, proxyBootConfig.ImageFile).ConfigureAwait(false);
               else
                  await backend.Execute(proxy.ResetImage(), raiseOnNonZeroExit: false).ConfigureAwait(false);

               if (imageVersion is not null)
                  await UploadString(backend, imageVersion, proxyBootConfig.ImageVersionFile).ConfigureAwait(false);
               else
                  await backend.Execute(proxy.ResetImageVersion(), raiseOnNonZeroExit: false).ConfigureAwait(false);

               if (runCommand is not null)
                  await UploadString(backend, runCommand, proxyBootConfig.RunCommandFile).ConfigureAwait(false);
               else
                  await backend.Execute(proxy.ResetRunCommand(), raiseOnNonZeroExit: false).ConfigureAwait(false);
            }).ConfigureAwait(false);
            break;
         }

         case "get":
            await On(KAMAL.ProxyHosts, async backend =>
               Console.WriteLine($"Host {backend.Host}: {await backend.CaptureWithInfo(KAMAL.Proxy(backend.Host).BootConfig()).ConfigureAwait(false)}")).ConfigureAwait(false);
            break;

         case "reset":
            await On(KAMAL.ProxyHosts, async backend =>
            {
               var proxy = KAMAL.Proxy(backend.Host);
               await backend.Execute(proxy.ResetBootOptions(), raiseOnNonZeroExit: false).ConfigureAwait(false);
               await backend.Execute(proxy.ResetImage(), raiseOnNonZeroExit: false).ConfigureAwait(false);
               await backend.Execute(proxy.ResetImageVersion(), raiseOnNonZeroExit: false).ConfigureAwait(false);
               await backend.Execute(proxy.ResetRunCommand(), raiseOnNonZeroExit: false).ConfigureAwait(false);
            }).ConfigureAwait(false);
            break;

         default:
            throw new ArgumentException($"Unknown boot_config subcommand {subcommand}");
      }
   }

   /// <summary>Port of <c>reboot</c>.</summary>
   public Task Reboot(bool rolling = false)
   {
      return Confirming("This will cause a brief outage on each host. Are you sure?", () =>
         Modify(async () =>
         {
            var hostGroups = rolling
               ? KAMAL.ProxyHosts.Select(host => (List<string>)[host]).ToList()
               : [KAMAL.ProxyHosts];

            foreach (var hosts in hostGroups)
            {
               var hostList = string.Join(",", hosts);
               await RunHook("pre-proxy-reboot", false, ("hosts", hostList)).ConfigureAwait(false);

               await On(hosts, async backend =>
               {
                  var proxy = KAMAL.Proxy(backend.Host);
                  await backend.Execute(KAMAL.Auditor().Record("Rebooted proxy"), verbosity: Verbosity.Debug).ConfigureAwait(false);

                  if (KAMAL.Registry.Login() is { } login)
                     await backend.Execute(login).ConfigureAwait(false);

                  await backend.Execute(proxy.Stop(), raiseOnNonZeroExit: false).ConfigureAwait(false);
                  await backend.Execute(proxy.RemoveContainer()).ConfigureAwait(false);
                  await backend.Execute(proxy.EnsureAppsConfigDirectory()).ConfigureAwait(false);

                  await backend.Execute(proxy.Run()).ConfigureAwait(false);
               }).ConfigureAwait(false);

               await RunHook("post-proxy-reboot", false, ("hosts", hostList)).ConfigureAwait(false);
            }
         }, requireLock: true));
   }

   /// <summary>Port of <c>upgrade</c>.</summary>
   public Task Upgrade(bool rolling = false, bool? confirmed = null)
   {
      if (confirmed is true)
         Options.Confirmed = true;

      var latestTag = KAMAL.Config.LatestTag;

      return Confirming("This will cause a brief outage on each host. Are you sure?", async () =>
      {
         var hostGroups = rolling
            ? KAMAL.Hosts.Select(host => (List<string>)[host]).ToList()
            : [KAMAL.Hosts];

         foreach (var hosts in hostGroups)
         {
            var hostList = string.Join(",", hosts);
            Say($"Upgrading proxy on {hostList}...", Magenta);
            await RunHook("pre-proxy-reboot", false, ("hosts", hostList)).ConfigureAwait(false);

            await On(hosts, async backend =>
            {
               var proxy = KAMAL.Proxy(backend.Host);
               await backend.Execute(KAMAL.Auditor().Record("Rebooted proxy"), verbosity: Verbosity.Debug).ConfigureAwait(false);

               if (KAMAL.Registry.Login() is { } login)
                  await backend.Execute(login).ConfigureAwait(false);

               await backend.Execute(proxy.CleanupTraefik()).ConfigureAwait(false);

               await backend.Execute(proxy.Stop(), raiseOnNonZeroExit: false).ConfigureAwait(false);
               await backend.Execute(proxy.RemoveContainer()).ConfigureAwait(false);
               await backend.Execute(proxy.RemoveImage()).ConfigureAwait(false);
            }).ConfigureAwait(false);

            await KAMAL.WithSpecificHosts(hosts, async () =>
            {
               await Boot().ConfigureAwait(false);
               await new AppCli(Context).Boot(latestTag).ConfigureAwait(false);
               await new PruneCli(Context).All().ConfigureAwait(false);
            }).ConfigureAwait(false);

            await RunHook("post-proxy-reboot", false, ("hosts", hostList)).ConfigureAwait(false);
            Say($"Upgraded proxy on {hostList}", Magenta);
         }
      });
   }

   /// <summary>Port of <c>start</c>.</summary>
   public Task Start()
   {
      return Modify(() =>
         On(KAMAL.ProxyHosts, async backend =>
         {
            await backend.Execute(KAMAL.Auditor().Record("Started proxy"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(KAMAL.Proxy(backend.Host).Start()).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>stop</c>.</summary>
   public Task Stop()
   {
      return Modify(() =>
         On(KAMAL.ProxyHosts, async backend =>
         {
            await backend.Execute(KAMAL.Auditor().Record("Stopped proxy"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(KAMAL.Proxy(backend.Host).Stop(), raiseOnNonZeroExit: false).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>restart</c>.</summary>
   public Task Restart()
   {
      return Modify(async () =>
      {
         await Stop().ConfigureAwait(false);
         await Start().ConfigureAwait(false);
      }, requireLock: true);
   }

   /// <summary>Port of <c>details</c>.</summary>
   public Task Details()
   {
      var quiet = Options.Quiet;

      return On(KAMAL.ProxyHosts, async backend =>
         PutsByHost(backend.Host, await backend.CaptureWithInfo(KAMAL.Proxy(backend.Host).Info()).ConfigureAwait(false), type: "Proxy", quiet: quiet));
   }

   /// <summary>Port of <c>logs</c>.</summary>
   public async Task Logs(string? since = null, int? lines = null, string? grep = null, bool follow = false, bool skipTimestamps = false)
   {
      var timestamps = !skipTimestamps;

      if (follow)
      {
         await PreConnectIfRequired().ConfigureAwait(false);
         await RunLocally(_ =>
         {
            var proxy = KAMAL.Proxy(KAMAL.PrimaryHost!);
            Output.KamalOutput.Logger.Log(Verbosity.Info, $"Following logs on {KAMAL.PrimaryHost}...");
            var command = proxy.FollowLogs(host: KAMAL.PrimaryHost!, timestamps: timestamps, grep: grep);
            Output.KamalOutput.Logger.Log(Verbosity.Info, command);
            ExecLocally(command);

            return Task.CompletedTask;
         }).ConfigureAwait(false);
      }
      else
      {
         var tailLines = lines ?? (since is not null || grep is not null ? (int?)null : 100);

         await On(KAMAL.ProxyHosts, async backend =>
            PutsByHost(backend.Host, await backend.Capture(KAMAL.Proxy(backend.Host).Logs(timestamps: timestamps, since: since, lines: tailLines, grep: grep)).ConfigureAwait(false), type: "Proxy")).ConfigureAwait(false);
      }
   }

   /// <summary>Port of <c>remove</c>.</summary>
   public Task Remove(bool force = false)
   {
      return Modify(async () =>
      {
         if (await RemovalAllowed(force).ConfigureAwait(false))
         {
            await Stop().ConfigureAwait(false);
            await RemoveContainer().ConfigureAwait(false);
            await RemoveImage().ConfigureAwait(false);
            await RemoveProxyDirectory().ConfigureAwait(false);
         }
      }, requireLock: true);
   }

   /// <summary>Port of <c>remove_container</c>.</summary>
   public Task RemoveContainer()
   {
      return Modify(() =>
         On(KAMAL.ProxyHosts, async backend =>
         {
            await backend.Execute(KAMAL.Auditor().Record("Removed proxy container"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(KAMAL.Proxy(backend.Host).RemoveContainer()).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>remove_image</c>.</summary>
   public Task RemoveImage()
   {
      return Modify(() =>
         On(KAMAL.ProxyHosts, async backend =>
         {
            await backend.Execute(KAMAL.Auditor().Record("Removed proxy image"), verbosity: Verbosity.Debug).ConfigureAwait(false);
            await backend.Execute(KAMAL.Proxy(backend.Host).RemoveImage()).ConfigureAwait(false);
         }), requireLock: true);
   }

   /// <summary>Port of <c>remove_proxy_directory</c>.</summary>
   public Task RemoveProxyDirectory()
   {
      return Modify(() =>
         On(KAMAL.ProxyHosts, backend =>
            backend.Execute(KAMAL.Proxy(backend.Host).RemoveProxyDirectory(), raiseOnNonZeroExit: false)), requireLock: true);
   }

   // ----- Private helpers --------------------------------------------------------------------

   private async Task<bool> RemovalAllowed(bool force)
   {
      try
      {
         await On(KAMAL.ProxyHosts, async backend =>
         {
            var output = await backend.CaptureWithInfo(KAMAL.Server.AppDirectoryCount()).ConfigureAwait(false);
            var appCount = int.TryParse(output.Trim(), out var count) ? count : 0;

            if (appCount > 0)
               throw new InvalidOperationException($"The are other applications installed on {backend.Host}");
         }).ConfigureAwait(false);

         return true;
      }
      catch (Exception e) when (e is ExecuteError or MultipleExecuteError)
      {
         if (!e.Message.Contains("The are other applications installed on"))
            throw;

         if (force)
            Say("Forcing, so removing the proxy, even though other apps are installed", Magenta);
         else
            Say("Not removing the proxy, as other apps are installed, ignore this check with kamal proxy remove --force", Magenta);

         return force;
      }
   }

   private static async Task UploadString(IBackend backend, string content, string remotePath)
   {
      using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
      await backend.Upload(stream, remotePath).ConfigureAwait(false);
   }

   private static string RenderToken(object? token) => RubyToS(token);

   private static string RubyToS(object? value)
   {
      return value switch
      {
         null => "",
         true => "true",
         false => "false",
         _ => value.ToString() ?? ""
      };
   }

   private static string? RubyPresence(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
