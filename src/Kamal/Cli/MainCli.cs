using System.Reflection;
using Kamal.Configuration.Validation;
using Kamal.Execution;
using Kamal.Utils;
using YamlDotNet.Serialization;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::Main</c>.</summary>
public sealed class MainCli : CliBase
{
   public MainCli(CliContext context) : base(context)
   {
   }

   /// <summary>Port of <c>setup</c>.</summary>
   public async Task Setup(bool skipPush = false, bool noCache = false)
   {
      await PrintRuntime(() =>
         Modify(async () =>
         {
            Say("Ensure Docker is installed...", Magenta);
            await new ServerCli(Context).Bootstrap().ConfigureAwait(false);

            await Deploy(skipPush, noCache, bootAccessories: true).ConfigureAwait(false);
         }, requireLock: true)).ConfigureAwait(false);
   }

   /// <summary>Port of <c>deploy</c>.</summary>
   public Task Deploy(bool skipPush = false, bool noCache = false, bool bootAccessories = false)
   {
      return Modify(async () =>
      {
         var runtime = await PrintRuntime(async () =>
         {
            var version = KAMAL.Config.Version;

            if (skipPush)
            {
               Say("Pull app image...", Magenta);
               await new BuildCli(Context).Pull().ConfigureAwait(false);
            }
            else
            {
               Say("Build and push app image...", Magenta);
               await new BuildCli(Context).Deliver(noCache: noCache).ConfigureAwait(false);
            }

            await Modify(async () =>
            {
               await RunHook("pre-deploy", secrets: true).ConfigureAwait(false);

               Say("Ensure kamal-proxy is running...", Magenta);
               await new ProxyCli(Context).Boot().ConfigureAwait(false);

               if (bootAccessories)
                  await new AccessoryCli(Context).Boot("all").ConfigureAwait(false);

               Say("Detect stale containers...", Magenta);
               await new AppCli(Context).StaleContainers(stop: true).ConfigureAwait(false);

               await new AppCli(Context).Boot(version).ConfigureAwait(false);

               Say("Prune old containers and images...", Magenta);
               await new PruneCli(Context).All().ConfigureAwait(false);
            }, requireLock: true).ConfigureAwait(false);
         }).ConfigureAwait(false);

         await RunHook("post-deploy", secrets: true, ("runtime", Math.Round(runtime).ToString("0"))).ConfigureAwait(false);
      });
   }

   /// <summary>Port of <c>redeploy</c>.</summary>
   public Task Redeploy(bool skipPush = false, bool noCache = false)
   {
      return Modify(async () =>
      {
         var runtime = await PrintRuntime(async () =>
         {
            var version = KAMAL.Config.Version;

            if (skipPush)
            {
               Say("Pull app image...", Magenta);
               await new BuildCli(Context).Pull().ConfigureAwait(false);
            }
            else
            {
               Say("Build and push app image...", Magenta);
               await new BuildCli(Context).Deliver(noCache: noCache).ConfigureAwait(false);
            }

            await Modify(async () =>
            {
               await RunHook("pre-deploy", secrets: true).ConfigureAwait(false);

               Say("Detect stale containers...", Magenta);
               await new AppCli(Context).StaleContainers(stop: true).ConfigureAwait(false);

               await new AppCli(Context).Boot(version).ConfigureAwait(false);
            }, requireLock: true).ConfigureAwait(false);
         }).ConfigureAwait(false);

         await RunHook("post-deploy", secrets: true, ("runtime", Math.Round(runtime).ToString("0"))).ConfigureAwait(false);
      });
   }

   /// <summary>Port of <c>rollback VERSION</c>.</summary>
   public Task Rollback(string version)
   {
      var rolledBack = false;

      return Modify(async () =>
      {
         var runtime = await PrintRuntime(() =>
            Modify(async () =>
            {
               KAMAL.Config.Version = version;

               if (await ContainerAvailable(version).ConfigureAwait(false))
               {
                  await RunHook("pre-deploy", secrets: true).ConfigureAwait(false);

                  await new AppCli(Context).Boot(version).ConfigureAwait(false);
                  rolledBack = true;
               }
               else
               {
                  Say($"The app version '{version}' is not available as a container (use 'kamal app containers' for available versions)", Red);
               }
            }, requireLock: true)).ConfigureAwait(false);

         if (rolledBack)
            await RunHook("post-deploy", secrets: true, ("runtime", Math.Round(runtime).ToString("0"))).ConfigureAwait(false);
      });
   }

   /// <summary>Port of <c>details</c>.</summary>
   public async Task Details()
   {
      await new ProxyCli(Context).Details().ConfigureAwait(false);
      await new AppCli(Context).Details().ConfigureAwait(false);
      await new AccessoryCli(Context).Details("all").ConfigureAwait(false);
   }

   /// <summary>Port of <c>audit</c>.</summary>
   public Task Audit()
   {
      var quiet = Options.Quiet;

      return On(KAMAL.Hosts, async backend =>
         PutsByHost(backend.Host, await backend.CaptureWithInfo(KAMAL.Auditor().Reveal()).ConfigureAwait(false), quiet: quiet));
   }

   /// <summary>Port of <c>config</c>.</summary>
   public Task Config()
   {
      return RunLocally(_ =>
      {
         var serializer = new SerializerBuilder().Build();
         Console.WriteLine("---");
         Console.Write(serializer.Serialize(KamalUtils.Redacted(KAMAL.Config.ToH())));

         return Task.CompletedTask;
      });
   }

   /// <summary>Port of <c>docs [SECTION]</c>.</summary>
   public Task Docs(string? section = null)
   {
      try
      {
         Console.WriteLine(ValidationDocs.Read(section ?? "configuration").TrimEnd('\n'));
      }
      catch (Exception)
      {
         Console.WriteLine($"No documentation found for {section}");
      }

      return Task.CompletedTask;
   }

   /// <summary>Port of <c>init</c>. The Ruby <c>--bundle</c> option is Gemfile-specific and not applicable here.</summary>
   public Task Init(bool bundle = false)
   {
      var deployFile = Path.GetFullPath(Path.Combine("config", "deploy.yml"));

      if (File.Exists(deployFile))
      {
         Console.WriteLine("Config file already exists in config/deploy.yml (remove first to create a new one)");
      }
      else
      {
         Directory.CreateDirectory(Path.GetDirectoryName(deployFile)!);
         File.WriteAllText(deployFile, ReadTemplate("deploy.yml"));
         Console.WriteLine("Created configuration file in config/deploy.yml");
      }

      var secretsFile = Path.GetFullPath(Path.Combine(".kamal", "secrets"));

      if (!File.Exists(secretsFile))
      {
         Directory.CreateDirectory(Path.GetDirectoryName(secretsFile)!);
         File.WriteAllText(secretsFile, ReadTemplate("secrets"));
         Console.WriteLine("Created .kamal/secrets file");
      }

      var hooksDir = Path.GetFullPath(Path.Combine(".kamal", "hooks"));

      if (!Directory.Exists(hooksDir))
      {
         Directory.CreateDirectory(hooksDir);

         foreach (var (name, content) in SampleHooks())
         {
            var hookPath = Path.Combine(hooksDir, name);
            File.WriteAllText(hookPath, content);

            if (!OperatingSystem.IsWindows())
               File.SetUnixFileMode(hookPath, (UnixFileMode)Convert.ToInt32("755", 8));
         }

         Console.WriteLine("Created sample hooks in .kamal/hooks");
      }

      if (bundle)
         Console.WriteLine("The --bundle option is Ruby/Gemfile-specific and is not applicable to the .NET port of Kamal.");

      return Task.CompletedTask;
   }

   /// <summary>Port of <c>remove</c>.</summary>
   public Task Remove()
   {
      return Confirming("This will remove all containers and images. Are you sure?", () =>
         Modify(async () =>
         {
            await new AppCli(Context).Remove().ConfigureAwait(false);
            await new ProxyCli(Context).Remove().ConfigureAwait(false);
            await new AccessoryCli(Context).Remove("all").ConfigureAwait(false);
            await new RegistryCli(Context).Remove(skipLocal: true).ConfigureAwait(false);
         }, requireLock: true));
   }

   /// <summary>Port of <c>upgrade</c>.</summary>
   public Task Upgrade(bool rolling = false)
   {
      return Confirming("This will replace Traefik with kamal-proxy and restart all accessories", () =>
         Modify(async () =>
         {
            if (rolling)
            {
               foreach (var host in KAMAL.Hosts)
               {
                  await KAMAL.WithSpecificHosts([host], async () =>
                  {
                     Say($"Upgrading {host}...", Magenta);

                     if (KAMAL.AppHosts.Contains(host))
                        await new ProxyCli(Context).Upgrade(rolling: false, confirmed: true).ConfigureAwait(false);

                     if (KAMAL.AccessoryHosts.Contains(host))
                        await new AccessoryCli(Context).Upgrade("all", rolling: false, confirmed: true).ConfigureAwait(false);

                     Say($"Upgraded {host}", Magenta);
                  }).ConfigureAwait(false);
               }
            }
            else
            {
               Say("Upgrading all hosts...", Magenta);
               await new ProxyCli(Context).Upgrade(rolling: false, confirmed: true).ConfigureAwait(false);
               await new AccessoryCli(Context).Upgrade("all", rolling: false, confirmed: true).ConfigureAwait(false);
               Say("Upgraded all hosts", Magenta);
            }
         }, requireLock: true));
   }

   /// <summary>Port of <c>version</c>.</summary>
   public Task Version()
   {
      Console.WriteLine($"{KamalVersion()} (kamal.net)");

      return Task.CompletedTask;
   }

   public static string KamalVersion()
   {
      var informational = Assembly.GetExecutingAssembly()
         .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

      var version = informational ?? Configuration.KamalConfiguration.KamalVersion;
      var plus = version.IndexOf('+');

      return plus >= 0 ? version[..plus] : version;
   }

   /// <summary>Port of <c>container_available?(version)</c>.</summary>
   private async Task<bool> ContainerAvailable(string version)
   {
      try
      {
         await On(KAMAL.AppHosts, async backend =>
         {
            foreach (var role in KAMAL.RolesOn(backend.Host))
            {
               var containerId = await backend.CaptureWithInfo(KAMAL.App(role: role, host: backend.Host).ContainerIdForVersion(version)).ConfigureAwait(false);

               if (string.IsNullOrWhiteSpace(containerId))
                  throw new InvalidOperationException("Container not found");
            }
         }).ConfigureAwait(false);
      }
      catch (Exception e) when (e is ExecuteError or MultipleExecuteError)
      {
         if (e.Message.Contains("Container not found"))
         {
            Say($"Error looking for container version {version}: {e.Message}");
            return false;
         }

         throw;
      }

      return true;
   }

   private static string ReadTemplate(string name)
   {
      var assembly = Assembly.GetExecutingAssembly();
      var resourceName = assembly.GetManifestResourceNames()
         .First(resource => resource.EndsWith($".Templates.{name}", StringComparison.Ordinal));

      using var stream = assembly.GetManifestResourceStream(resourceName)!;
      using var reader = new StreamReader(stream);

      return reader.ReadToEnd();
   }

   private static IEnumerable<(string Name, string Content)> SampleHooks()
   {
      var assembly = Assembly.GetExecutingAssembly();
      const string marker = ".Templates.sample_hooks.";

      foreach (var resourceName in assembly.GetManifestResourceNames().Where(resource => resource.Contains(marker)).OrderBy(resource => resource, StringComparer.Ordinal))
      {
         var name = resourceName[(resourceName.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];

         using var stream = assembly.GetManifestResourceStream(resourceName)!;
         using var reader = new StreamReader(stream);

         yield return (name, reader.ReadToEnd());
      }
   }
}
