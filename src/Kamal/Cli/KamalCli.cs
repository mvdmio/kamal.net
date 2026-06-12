using System.CommandLine;
using System.CommandLine.Help;
using Kamal.Execution;
using Kamal.Output;
using Kamal.Secrets;

namespace Kamal.Cli;

/// <summary>
/// The System.CommandLine tree mirroring the Thor command structure of <c>Kamal::Cli::Main</c>,
/// plus the top-level start logic from <c>bin/kamal</c> / <c>lib/kamal/cli.rb</c>
/// (alias resolution and error reporting).
/// </summary>
public static class KamalCli
{
   // Global (Thor class) options.
   private static readonly Option<bool> VerboseOption = new("--verbose", "-v") { Description = "Detailed logging", Recursive = true };
   private static readonly Option<bool> QuietOption = new("--quiet", "-q") { Description = "Minimal logging", Recursive = true };
   private static readonly Option<string?> VersionOption = new("--version") { Description = "Run commands against a specific app version", Recursive = true };
   private static readonly Option<bool> PrimaryOption = new("--primary", "-p") { Description = "Run commands only on primary host instead of all", Recursive = true };
   private static readonly Option<string?> HostsOption = new("--hosts", "-h") { Description = "Run commands on these hosts instead of all (separate by comma, supports wildcards with *)", Recursive = true };
   private static readonly Option<string?> RolesOption = new("--roles", "-r") { Description = "Run commands on these roles instead of all (separate by comma, supports wildcards with *)", Recursive = true };
   private static readonly Option<string> ConfigFileOption = new("--config-file", "-c") { Description = "Path to config file", Recursive = true, DefaultValueFactory = _ => "config/deploy.yml" };
   private static readonly Option<string?> DestinationOption = new("--destination", "-d") { Description = "Specify destination to be used for config file (staging -> deploy.staging.yml)", Recursive = true };
   private static readonly Option<bool> SkipHooksOption = new("--skip-hooks", "-H") { Description = "Don't run hooks", Recursive = true };

   /// <summary>Entry point used by Program.cs and the tests: parse (resolving aliases) and invoke.</summary>
   public static async Task<int> Start(string[] args)
   {
      var root = BuildRootCommand();

      try
      {
         args = ResolveAliases(args, root);

         var parseResult = root.Parse(args);

         return await parseResult.InvokeAsync(new InvocationConfiguration { EnableDefaultExceptionHandler = false }).ConfigureAwait(false);
      }
      catch (Exception e) when (e is ExecuteError or MultipleExecuteError)
      {
         var causeName = (e.InnerException ?? e).GetType().Name;
         PrintError($"  ERROR ({causeName}): {e.Message}");
         PrintBacktraceIfVerbose(e);

         return 1;
      }
      catch (Exception e)
      {
         PrintError($"  ERROR ({e.GetType().Name}): {e.Message}");
         PrintBacktraceIfVerbose(e);

         return 1;
      }
   }

   private static void PrintError(string message)
   {
      Console.WriteLine(CliBaseColors.Red(message));
   }

   private static void PrintBacktraceIfVerbose(Exception e)
   {
      if (Environment.GetEnvironmentVariable("VERBOSE") is not null)
         Console.WriteLine(e.StackTrace);
   }

   // ----- Alias resolution (port of Kamal::Cli::Alias::Command) -------------------------------

   private static string[] ResolveAliases(string[] args, RootCommand root)
   {
      var knownCommands = root.Subcommands.SelectMany(command => command.Aliases.Append(command.Name)).ToHashSet(StringComparer.Ordinal);
      var seen = new HashSet<string>(StringComparer.Ordinal);

      while (args.Length > 0 && !args[0].StartsWith('-') && !knownCommands.Contains(args[0]))
      {
         if (!seen.Add(args[0]))
            throw new InvalidOperationException($"Recursive alias detected: {args[0]}");

         string? aliasCommand;

         try
         {
            ConfigureCommanderForAliasLookup(args);
            aliasCommand = KamalRuntime.Commander.ResolveAlias(args[0]) as string;
         }
         catch (Exception)
         {
            // No readable config: let the parser report the unknown command.
            break;
         }

         if (aliasCommand is null)
            break;

         KamalRuntime.Commander.Reset();
         args = Shellwords.Split(aliasCommand).Concat(args.Skip(1)).ToArray();
      }

      return args;
   }

   private static void ConfigureCommanderForAliasLookup(string[] args)
   {
      if (KamalRuntime.Commander.Configured)
         return;

      string? configFile = null, destination = null;

      for (var i = 0; i < args.Length - 1; i++)
      {
         switch (args[i])
         {
            case "-c" or "--config-file":
               configFile = args[i + 1];
               break;
            case "-d" or "--destination":
               destination = args[i + 1];
               break;
         }
      }

      KamalRuntime.Commander.Configure(
         configFile: Path.GetFullPath(configFile ?? Path.Combine("config", "deploy.yml")),
         destination: destination);
   }

   // ----- Command tree -------------------------------------------------------------------------

   public static RootCommand BuildRootCommand()
   {
      var root = new RootCommand("Kamal: Deploy web apps anywhere");

      // Kamal claims -h for --hosts and --version for the app version, so drop/trim the defaults.
      for (var i = root.Options.Count - 1; i >= 0; i--)
      {
         switch (root.Options[i])
         {
            case System.CommandLine.VersionOption:
               root.Options.RemoveAt(i);
               break;
            case HelpOption help:
               help.Aliases.Remove("-h");
               break;
         }
      }

      foreach (var option in new Option[] { VerboseOption, QuietOption, VersionOption, PrimaryOption, HostsOption, RolesOption, ConfigFileOption, DestinationOption, SkipHooksOption })
         root.Add(option);

      AddMainCommands(root);
      root.Add(AccessoryCommands());
      root.Add(AppCommands());
      root.Add(BuildCommands());
      root.Add(LockCommands());
      root.Add(ProxyCommands());
      root.Add(PruneCommands());
      root.Add(RegistryCommands());
      root.Add(SecretsCommands());
      root.Add(ServerCommands());

      return root;
   }

   private static void AddMainCommands(RootCommand root)
   {
      var skipPush = new Option<bool>("--skip-push", "-P") { Description = "Skip image build and push" };
      var noCache = new Option<bool>("--no-cache") { Description = "Build without using Docker's build cache" };

      var setup = new Command("setup", "Setup all accessories, push the env, and deploy app to servers") { skipPush, noCache };
      SetAction(setup, "setup", null, (parse, context) => new MainCli(context).Setup(parse.GetValue(skipPush), parse.GetValue(noCache)));
      root.Add(setup);

      var deploy = new Command("deploy", "Deploy app to servers") { skipPush, noCache };
      SetAction(deploy, "deploy", null, (parse, context) => new MainCli(context).Deploy(parse.GetValue(skipPush), parse.GetValue(noCache)));
      root.Add(deploy);

      var redeploy = new Command("redeploy", "Deploy app to servers without bootstrapping servers, starting kamal-proxy and pruning") { skipPush, noCache };
      SetAction(redeploy, "redeploy", null, (parse, context) => new MainCli(context).Redeploy(parse.GetValue(skipPush), parse.GetValue(noCache)));
      root.Add(redeploy);

      var rollbackVersion = new Argument<string>("version") { Description = "The version to roll back to" };
      var rollback = new Command("rollback", "Rollback app to VERSION") { rollbackVersion };
      SetAction(rollback, "rollback", null, (parse, context) => new MainCli(context).Rollback(parse.GetRequiredValue(rollbackVersion)));
      root.Add(rollback);

      var details = new Command("details", "Show details about all containers");
      SetAction(details, "details", null, (_, context) => new MainCli(context).Details());
      root.Add(details);

      var audit = new Command("audit", "Show audit log from servers");
      SetAction(audit, "audit", null, (_, context) => new MainCli(context).Audit());
      root.Add(audit);

      var config = new Command("config", "Show combined config (including secrets!)");
      SetAction(config, "config", null, (_, context) => new MainCli(context).Config());
      root.Add(config);

      var docsSection = new Argument<string?>("section") { Arity = ArgumentArity.ZeroOrOne, Description = "Configuration section" };
      var docs = new Command("docs", "Show Kamal configuration documentation") { docsSection };
      SetAction(docs, "docs", null, (parse, context) => new MainCli(context).Docs(parse.GetValue(docsSection)), requiresCommander: false);
      root.Add(docs);

      var bundle = new Option<bool>("--bundle") { Description = "Not applicable to kamal.net (Ruby/Gemfile-specific)" };
      var init = new Command("init", "Create config stub in config/deploy.yml and secrets stub in .kamal") { bundle };
      SetAction(init, "init", null, (parse, context) => new MainCli(context).Init(parse.GetValue(bundle)), requiresCommander: false);
      root.Add(init);

      var removeConfirmed = ConfirmedOption();
      var remove = new Command("remove", "Remove kamal-proxy, app, accessories, and registry session from servers") { removeConfirmed };
      SetAction(remove, "remove", null, (parse, context) =>
      {
         context.Options.Confirmed = parse.GetValue(removeConfirmed);
         return new MainCli(context).Remove();
      });
      root.Add(remove);

      var upgradeConfirmed = ConfirmedOption();
      var upgradeRolling = new Option<bool>("--rolling") { Description = "Upgrade one host at a time" };
      var upgrade = new Command("upgrade", "Upgrade from Kamal 1.x to 2.0") { upgradeConfirmed, upgradeRolling };
      SetAction(upgrade, "upgrade", null, (parse, context) =>
      {
         context.Options.Confirmed = parse.GetValue(upgradeConfirmed);
         return new MainCli(context).Upgrade(rolling: parse.GetValue(upgradeRolling));
      });
      root.Add(upgrade);

      var version = new Command("version", "Show Kamal version");
      SetAction(version, "version", null, (_, context) => new MainCli(context).Version(), requiresCommander: false);
      root.Add(version);
   }

   private static Command AccessoryCommands()
   {
      var accessory = new Command("accessory", "Manage accessories (db/redis/search)");

      Argument<string> NameArg() => new("name") { Description = "Accessory name ('all' for all accessories)" };

      var bootName = NameArg();
      var boot = new Command("boot", "Boot new accessory service on host (use NAME=all to boot all accessories)") { bootName };
      SetAction(boot, "accessory", "boot", (parse, context) => new AccessoryCli(context).Boot(parse.GetRequiredValue(bootName)));
      accessory.Add(boot);

      var uploadName = NameArg();
      var upload = new Command("upload", "Upload accessory files to host") { uploadName };
      upload.Hidden = true;
      SetAction(upload, "accessory", "upload", (parse, context) => new AccessoryCli(context).Upload(parse.GetRequiredValue(uploadName)));
      accessory.Add(upload);

      var directoriesName = NameArg();
      var directories = new Command("directories", "Create accessory directories on host") { directoriesName };
      directories.Hidden = true;
      SetAction(directories, "accessory", "directories", (parse, context) => new AccessoryCli(context).Directories(parse.GetRequiredValue(directoriesName)));
      accessory.Add(directories);

      var rebootName = NameArg();
      var reboot = new Command("reboot", "Reboot existing accessory on host (stop container, remove container, start new container; use NAME=all to boot all accessories)") { rebootName };
      SetAction(reboot, "accessory", "reboot", (parse, context) => new AccessoryCli(context).Reboot(parse.GetRequiredValue(rebootName)));
      accessory.Add(reboot);

      var startName = NameArg();
      var start = new Command("start", "Start existing accessory container on host") { startName };
      SetAction(start, "accessory", "start", (parse, context) => new AccessoryCli(context).Start(parse.GetRequiredValue(startName)));
      accessory.Add(start);

      var stopName = NameArg();
      var stop = new Command("stop", "Stop existing accessory container on host") { stopName };
      SetAction(stop, "accessory", "stop", (parse, context) => new AccessoryCli(context).Stop(parse.GetRequiredValue(stopName)));
      accessory.Add(stop);

      var restartName = NameArg();
      var restart = new Command("restart", "Restart existing accessory container on host") { restartName };
      SetAction(restart, "accessory", "restart", (parse, context) => new AccessoryCli(context).Restart(parse.GetRequiredValue(restartName)));
      accessory.Add(restart);

      var detailsName = NameArg();
      var details = new Command("details", "Show details about accessory on host (use NAME=all to show all accessories)") { detailsName };
      SetAction(details, "accessory", "details", (parse, context) => new AccessoryCli(context).Details(parse.GetRequiredValue(detailsName)));
      accessory.Add(details);

      var execName = NameArg();
      var execCmd = new Argument<string[]>("cmd") { Arity = ArgumentArity.ZeroOrMore, Description = "Command to execute" };
      var execInteractive = new Option<bool>("--interactive", "-i") { Description = "Execute command over ssh for an interactive shell (use for console/bash)" };
      var execReuse = new Option<bool>("--reuse") { Description = "Reuse currently running container instead of starting a new one" };
      var exec = new Command("exec", "Execute a custom command on servers within the accessory container (use --help to show options)") { execName, execCmd, execInteractive, execReuse };
      SetAction(exec, "accessory", "exec", (parse, context) =>
         new AccessoryCli(context).Exec(parse.GetRequiredValue(execName), parse.GetValue(execCmd) ?? [], interactive: parse.GetValue(execInteractive), reuse: parse.GetValue(execReuse)));
      accessory.Add(exec);

      var logsName = NameArg();
      var logsSince = new Option<string?>("--since", "-s") { Description = "Show logs since timestamp (e.g. 2013-01-02T13:23:37Z) or relative (e.g. 42m for 42 minutes)" };
      var logsLines = new Option<int?>("--lines", "-n") { Description = "Number of log lines to pull from each server" };
      var logsGrep = new Option<string?>("--grep", "-g") { Description = "Show lines with grep match only (use this to fetch specific requests by id)" };
      var logsGrepOptions = new Option<string?>("--grep-options") { Description = "Additional options supplied to grep" };
      var logsFollow = new Option<bool>("--follow", "-f") { Description = "Follow logs on primary server (or specific host set by --hosts)" };
      var logsSkipTimestamps = new Option<bool>("--skip-timestamps", "-T") { Description = "Skip appending timestamps to logging output" };
      var logs = new Command("logs", "Show log lines from accessory on host (use --help to show options)") { logsName, logsSince, logsLines, logsGrep, logsGrepOptions, logsFollow, logsSkipTimestamps };
      SetAction(logs, "accessory", "logs", (parse, context) =>
         new AccessoryCli(context).Logs(parse.GetRequiredValue(logsName), since: parse.GetValue(logsSince), lines: parse.GetValue(logsLines), grep: parse.GetValue(logsGrep), grepOptions: parse.GetValue(logsGrepOptions), follow: parse.GetValue(logsFollow), skipTimestamps: parse.GetValue(logsSkipTimestamps)));
      accessory.Add(logs);

      var pullImageName = NameArg();
      var pullImage = new Command("pull_image", "Pull accessory image on host") { pullImageName };
      pullImage.Hidden = true;
      pullImage.Aliases.Add("pull-image");
      SetAction(pullImage, "accessory", "pull_image", (parse, context) => new AccessoryCli(context).PullImage(parse.GetRequiredValue(pullImageName)));
      accessory.Add(pullImage);

      var removeName = NameArg();
      var removeConfirmed = ConfirmedOption();
      var remove = new Command("remove", "Remove accessory container, image and data directory from host (use NAME=all to remove all accessories)") { removeName, removeConfirmed };
      SetAction(remove, "accessory", "remove", (parse, context) =>
      {
         context.Options.Confirmed = parse.GetValue(removeConfirmed);
         return new AccessoryCli(context).Remove(parse.GetRequiredValue(removeName));
      });
      accessory.Add(remove);

      var removeContainerName = NameArg();
      var removeContainer = new Command("remove_container", "Remove accessory container from host") { removeContainerName };
      removeContainer.Hidden = true;
      removeContainer.Aliases.Add("remove-container");
      SetAction(removeContainer, "accessory", "remove_container", (parse, context) => new AccessoryCli(context).RemoveContainer(parse.GetRequiredValue(removeContainerName)));
      accessory.Add(removeContainer);

      var removeImageName = NameArg();
      var removeImage = new Command("remove_image", "Remove accessory image from host") { removeImageName };
      removeImage.Hidden = true;
      removeImage.Aliases.Add("remove-image");
      SetAction(removeImage, "accessory", "remove_image", (parse, context) => new AccessoryCli(context).RemoveImage(parse.GetRequiredValue(removeImageName)));
      accessory.Add(removeImage);

      var removeServiceDirectoryName = NameArg();
      var removeServiceDirectory = new Command("remove_service_directory", "Remove accessory directory used for uploaded files and data directories from host") { removeServiceDirectoryName };
      removeServiceDirectory.Hidden = true;
      removeServiceDirectory.Aliases.Add("remove-service-directory");
      SetAction(removeServiceDirectory, "accessory", "remove_service_directory", (parse, context) => new AccessoryCli(context).RemoveServiceDirectory(parse.GetRequiredValue(removeServiceDirectoryName)));
      accessory.Add(removeServiceDirectory);

      var upgradeName = NameArg();
      var upgradeRolling = new Option<bool>("--rolling") { Description = "Upgrade one host at a time" };
      var upgradeConfirmed = ConfirmedOption();
      var upgrade = new Command("upgrade", "Upgrade accessories from Kamal 1.x to 2.0 (restart them in 'kamal' network)") { upgradeName, upgradeRolling, upgradeConfirmed };
      SetAction(upgrade, "accessory", "upgrade", (parse, context) =>
      {
         context.Options.Confirmed = parse.GetValue(upgradeConfirmed);
         return new AccessoryCli(context).Upgrade(parse.GetRequiredValue(upgradeName), rolling: parse.GetValue(upgradeRolling));
      });
      accessory.Add(upgrade);

      return accessory;
   }

   private static Command AppCommands()
   {
      var app = new Command("app", "Manage application");

      var boot = new Command("boot", "Boot app on servers (or reboot app if already running)");
      SetAction(boot, "app", "boot", (_, context) => new AppCli(context).Boot());
      app.Add(boot);

      var start = new Command("start", "Start existing app container on servers");
      SetAction(start, "app", "start", (_, context) => new AppCli(context).Start());
      app.Add(start);

      var stop = new Command("stop", "Stop app container on servers");
      SetAction(stop, "app", "stop", (_, context) => new AppCli(context).Stop());
      app.Add(stop);

      var details = new Command("details", "Show details about app containers");
      SetAction(details, "app", "details", (_, context) => new AppCli(context).Details());
      app.Add(details);

      var execCmd = new Argument<string[]>("cmd") { Arity = ArgumentArity.ZeroOrMore, Description = "Command to execute" };
      var execInteractive = new Option<bool>("--interactive", "-i") { Description = "Execute command over ssh for an interactive shell (use for console/bash)" };
      var execReuse = new Option<bool>("--reuse") { Description = "Reuse currently running container instead of starting a new one" };
      var execEnv = new Option<string[]>("--env", "-e") { Description = "Set environment variables for the command (NAME=value pairs)", AllowMultipleArgumentsPerToken = true };
      var execDetach = new Option<bool>("--detach") { Description = "Execute command in a detached container" };
      var exec = new Command("exec", "Execute a custom command on servers within the app container (use --help to show options)") { execCmd, execInteractive, execReuse, execEnv, execDetach };
      SetAction(exec, "app", "exec", (parse, context) =>
         new AppCli(context).Exec(parse.GetValue(execCmd) ?? [], interactive: parse.GetValue(execInteractive), reuse: parse.GetValue(execReuse), env: parse.GetValue(execEnv), detach: parse.GetValue(execDetach)));
      app.Add(exec);

      var containers = new Command("containers", "Show app containers on servers");
      SetAction(containers, "app", "containers", (_, context) => new AppCli(context).Containers());
      app.Add(containers);

      var staleStop = new Option<bool>("--stop", "-s") { Description = "Stop the stale containers found" };
      var staleContainers = new Command("stale_containers", "Detect app stale containers") { staleStop };
      staleContainers.Aliases.Add("stale-containers");
      SetAction(staleContainers, "app", "stale_containers", (parse, context) => new AppCli(context).StaleContainers(stop: parse.GetValue(staleStop)));
      app.Add(staleContainers);

      var images = new Command("images", "Show app images on servers");
      SetAction(images, "app", "images", (_, context) => new AppCli(context).Images());
      app.Add(images);

      var logsSince = new Option<string?>("--since", "-s") { Description = "Show lines since timestamp (e.g. 2013-01-02T13:23:37Z) or relative (e.g. 42m for 42 minutes)" };
      var logsLines = new Option<int?>("--lines", "-n") { Description = "Number of lines to show from each server" };
      var logsGrep = new Option<string?>("--grep", "-g") { Description = "Show lines with grep match only (use this to fetch specific requests by id)" };
      var logsGrepOptions = new Option<string?>("--grep-options") { Description = "Additional options supplied to grep" };
      var logsFollow = new Option<bool>("--follow", "-f") { Description = "Follow log on primary server (or specific host set by --hosts)" };
      var logsSkipTimestamps = new Option<bool>("--skip-timestamps", "-T") { Description = "Skip appending timestamps to logging output" };
      var logsContainerId = new Option<string?>("--container-id") { Description = "Docker container ID to fetch logs" };
      var logs = new Command("logs", "Show log lines from app on servers (use --help to show options)") { logsSince, logsLines, logsGrep, logsGrepOptions, logsFollow, logsSkipTimestamps, logsContainerId };
      SetAction(logs, "app", "logs", (parse, context) =>
         new AppCli(context).Logs(since: parse.GetValue(logsSince), lines: parse.GetValue(logsLines), grep: parse.GetValue(logsGrep), grepOptions: parse.GetValue(logsGrepOptions), follow: parse.GetValue(logsFollow), skipTimestamps: parse.GetValue(logsSkipTimestamps), containerId: parse.GetValue(logsContainerId)));
      app.Add(logs);

      var remove = new Command("remove", "Remove app containers and images from servers");
      SetAction(remove, "app", "remove", (_, context) => new AppCli(context).Remove());
      app.Add(remove);

      var live = new Command("live", "Set the app to live mode");
      SetAction(live, "app", "live", (_, context) => new AppCli(context).Live());
      app.Add(live);

      var drainTimeout = new Option<int?>("--drain-timeout") { Description = "How long to allow in-flight requests to complete (defaults to drain_timeout from config)" };
      var maintenanceMessage = new Option<string?>("--message") { Description = "Message to display to clients while stopped" };
      var maintenance = new Command("maintenance", "Set the app to maintenance mode") { drainTimeout, maintenanceMessage };
      SetAction(maintenance, "app", "maintenance", (parse, context) =>
         new AppCli(context).Maintenance(drainTimeout: parse.GetValue(drainTimeout), message: parse.GetValue(maintenanceMessage)));
      app.Add(maintenance);

      var removeContainerVersion = new Argument<string>("version") { Description = "Container version to remove" };
      var removeContainer = new Command("remove_container", "Remove app container with given version from servers") { removeContainerVersion };
      removeContainer.Hidden = true;
      removeContainer.Aliases.Add("remove-container");
      SetAction(removeContainer, "app", "remove_container", (parse, context) => new AppCli(context).RemoveContainer(parse.GetRequiredValue(removeContainerVersion)));
      app.Add(removeContainer);

      var removeContainers = new Command("remove_containers", "Remove all app containers from servers");
      removeContainers.Hidden = true;
      removeContainers.Aliases.Add("remove-containers");
      SetAction(removeContainers, "app", "remove_containers", (_, context) => new AppCli(context).RemoveContainers());
      app.Add(removeContainers);

      var removeImages = new Command("remove_images", "Remove all app images from servers");
      removeImages.Hidden = true;
      removeImages.Aliases.Add("remove-images");
      SetAction(removeImages, "app", "remove_images", (_, context) => new AppCli(context).RemoveImages());
      app.Add(removeImages);

      var removeAppDirectories = new Command("remove_app_directories", "Remove the app directories from servers");
      removeAppDirectories.Hidden = true;
      removeAppDirectories.Aliases.Add("remove-app-directories");
      SetAction(removeAppDirectories, "app", "remove_app_directories", (_, context) => new AppCli(context).RemoveAppDirectories());
      app.Add(removeAppDirectories);

      var version = new Command("version", "Show app version currently running on servers");
      SetAction(version, "app", "version", (_, context) => new AppCli(context).Version());
      app.Add(version);

      return app;
   }

   private static Command BuildCommands()
   {
      var build = new Command("build", "Build application image");

      var deliver = new Command("deliver", "Build app and push app image to registry then pull image on servers");
      SetAction(deliver, "build", "deliver", (_, context) => new BuildCli(context).Deliver());
      build.Add(deliver);

      var pushOutput = new Option<string>("--output") { Description = "Exported type for the build result, and may be any exported type supported by 'buildx --output'.", DefaultValueFactory = _ => "registry", HelpName = "export_type" };
      var pushNoCache = new Option<bool>("--no-cache") { Description = "Build without using Docker's build cache" };
      var push = new Command("push", "Build and push app image to registry") { pushOutput, pushNoCache };
      SetAction(push, "build", "push", (parse, context) => new BuildCli(context).Push(parse.GetValue(pushOutput) ?? "registry", noCache: parse.GetValue(pushNoCache)));
      build.Add(push);

      var pull = new Command("pull", "Pull app image from registry onto servers");
      SetAction(pull, "build", "pull", (_, context) => new BuildCli(context).Pull());
      build.Add(pull);

      var create = new Command("create", "Create a build setup");
      SetAction(create, "build", "create", (_, context) => new BuildCli(context).Create());
      build.Add(create);

      var remove = new Command("remove", "Remove build setup");
      SetAction(remove, "build", "remove", (_, context) => new BuildCli(context).Remove());
      build.Add(remove);

      var details = new Command("details", "Show build setup");
      SetAction(details, "build", "details", (_, context) => new BuildCli(context).Details());
      build.Add(details);

      var devOutput = new Option<string>("--output") { Description = "Exported type for the build result, and may be any exported type supported by 'buildx --output'.", DefaultValueFactory = _ => "docker", HelpName = "export_type" };
      var devNoCache = new Option<bool>("--no-cache") { Description = "Build without using Docker's build cache" };
      var dev = new Command("dev", "Build using the working directory, tag it as dirty, and push to local image store.") { devOutput, devNoCache };
      SetAction(dev, "build", "dev", (parse, context) => new BuildCli(context).Dev(parse.GetValue(devOutput) ?? "docker", noCache: parse.GetValue(devNoCache)));
      build.Add(dev);

      return build;
   }

   private static Command LockCommands()
   {
      var lockCommand = new Command("lock", "Manage the deploy lock");

      var status = new Command("status", "Report lock status");
      SetAction(status, "lock", "status", (_, context) => new LockCli(context).Status());
      lockCommand.Add(status);

      var message = new Option<string>("--message", "-m") { Description = "A lock message", Required = true };
      var acquire = new Command("acquire", "Acquire the deploy lock") { message };
      SetAction(acquire, "lock", "acquire", (parse, context) => new LockCli(context).Acquire(parse.GetRequiredValue(message)));
      lockCommand.Add(acquire);

      var release = new Command("release", "Release the deploy lock");
      SetAction(release, "lock", "release", (_, context) => new LockCli(context).Release());
      lockCommand.Add(release);

      return lockCommand;
   }

   private static Command ProxyCommands()
   {
      var proxy = new Command("proxy", "Manage kamal-proxy");

      var boot = new Command("boot", "Boot proxy on servers");
      SetAction(boot, "proxy", "boot", (_, context) => new ProxyCli(context).Boot());
      proxy.Add(boot);

      var bootConfigSubcommand = new Argument<string>("subcommand") { Description = "set, get or reset" };
      var publish = new Option<bool>("--publish") { Description = "Publish the proxy ports on the host", DefaultValueFactory = _ => true };
      var publishHostIp = new Option<string[]>("--publish-host-ip") { Description = "Host IP address to bind HTTP/HTTPS traffic to. Defaults to all interfaces" };
      var httpPort = new Option<int>("--http-port") { Description = "HTTP port to publish on the host", DefaultValueFactory = _ => Configuration.ProxyRun.DefaultHttpPort };
      var httpsPort = new Option<int>("--https-port") { Description = "HTTPS port to publish on the host", DefaultValueFactory = _ => Configuration.ProxyRun.DefaultHttpsPort };
      var logMaxSize = new Option<string>("--log-max-size") { Description = "Max size of proxy logs", DefaultValueFactory = _ => Configuration.ProxyRun.DefaultLogMaxSize };
      var registryOpt = new Option<string?>("--registry") { Description = "Registry to use for the proxy image" };
      var repository = new Option<string?>("--repository") { Description = "Repository for the proxy image" };
      var imageVersion = new Option<string?>("--image-version") { Description = "Version of the proxy to run" };
      var metricsPort = new Option<int?>("--metrics-port") { Description = "Port to report prometheus metrics on" };
      var debug = new Option<bool>("--debug") { Description = "Whether to run the proxy in debug mode" };
      var dockerOptions = new Option<string[]>("--docker-options") { Description = "Docker options to pass to the proxy container (option=value option2=value2)", AllowMultipleArgumentsPerToken = true };
      var bootConfig = new Command("boot_config", "Manage kamal-proxy boot configuration")
      {
         bootConfigSubcommand, publish, publishHostIp, httpPort, httpsPort, logMaxSize, registryOpt, repository, imageVersion, metricsPort, debug, dockerOptions
      };
      bootConfig.Aliases.Add("boot-config");
      SetAction(bootConfig, "proxy", "boot_config", (parse, context) =>
         new ProxyCli(context).BootConfig(
            parse.GetRequiredValue(bootConfigSubcommand),
            publish: parse.GetValue(publish),
            publishHostIp: parse.GetValue(publishHostIp),
            httpPort: parse.GetValue(httpPort),
            httpsPort: parse.GetValue(httpsPort),
            logMaxSize: parse.GetValue(logMaxSize),
            registry: parse.GetValue(registryOpt),
            repository: parse.GetValue(repository),
            imageVersion: parse.GetValue(imageVersion),
            metricsPort: parse.GetValue(metricsPort),
            debug: parse.GetValue(debug),
            dockerOptions: parse.GetValue(dockerOptions)));
      proxy.Add(bootConfig);

      var rebootRolling = new Option<bool>("--rolling") { Description = "Reboot proxy on hosts in sequence, rather than in parallel" };
      var rebootConfirmed = ConfirmedOption();
      var reboot = new Command("reboot", "Reboot proxy on servers (stop container, remove container, start new container)") { rebootRolling, rebootConfirmed };
      SetAction(reboot, "proxy", "reboot", (parse, context) =>
      {
         context.Options.Confirmed = parse.GetValue(rebootConfirmed);
         return new ProxyCli(context).Reboot(rolling: parse.GetValue(rebootRolling));
      });
      proxy.Add(reboot);

      var upgradeRolling = new Option<bool>("--rolling") { Description = "Reboot proxy on hosts in sequence, rather than in parallel" };
      var upgradeConfirmed = ConfirmedOption();
      var upgrade = new Command("upgrade", "Upgrade to kamal-proxy on servers (stop container, remove container, start new container, reboot app)") { upgradeRolling, upgradeConfirmed };
      upgrade.Hidden = true;
      SetAction(upgrade, "proxy", "upgrade", (parse, context) =>
      {
         context.Options.Confirmed = parse.GetValue(upgradeConfirmed);
         return new ProxyCli(context).Upgrade(rolling: parse.GetValue(upgradeRolling));
      });
      proxy.Add(upgrade);

      var start = new Command("start", "Start existing proxy container on servers");
      SetAction(start, "proxy", "start", (_, context) => new ProxyCli(context).Start());
      proxy.Add(start);

      var stop = new Command("stop", "Stop existing proxy container on servers");
      SetAction(stop, "proxy", "stop", (_, context) => new ProxyCli(context).Stop());
      proxy.Add(stop);

      var restart = new Command("restart", "Restart existing proxy container on servers");
      SetAction(restart, "proxy", "restart", (_, context) => new ProxyCli(context).Restart());
      proxy.Add(restart);

      var details = new Command("details", "Show details about proxy container from servers");
      SetAction(details, "proxy", "details", (_, context) => new ProxyCli(context).Details());
      proxy.Add(details);

      var logsSince = new Option<string?>("--since", "-s") { Description = "Show logs since timestamp (e.g. 2013-01-02T13:23:37Z) or relative (e.g. 42m for 42 minutes)" };
      var logsLines = new Option<int?>("--lines", "-n") { Description = "Number of log lines to pull from each server" };
      var logsGrep = new Option<string?>("--grep", "-g") { Description = "Show lines with grep match only (use this to fetch specific requests by id)" };
      var logsFollow = new Option<bool>("--follow", "-f") { Description = "Follow logs on primary server (or specific host set by --hosts)" };
      var logsSkipTimestamps = new Option<bool>("--skip-timestamps", "-T") { Description = "Skip appending timestamps to logging output" };
      var logs = new Command("logs", "Show log lines from proxy on servers") { logsSince, logsLines, logsGrep, logsFollow, logsSkipTimestamps };
      SetAction(logs, "proxy", "logs", (parse, context) =>
         new ProxyCli(context).Logs(since: parse.GetValue(logsSince), lines: parse.GetValue(logsLines), grep: parse.GetValue(logsGrep), follow: parse.GetValue(logsFollow), skipTimestamps: parse.GetValue(logsSkipTimestamps)));
      proxy.Add(logs);

      var removeForce = new Option<bool>("--force") { Description = "Force removing proxy when apps are still installed" };
      var remove = new Command("remove", "Remove proxy container and image from servers") { removeForce };
      SetAction(remove, "proxy", "remove", (parse, context) => new ProxyCli(context).Remove(force: parse.GetValue(removeForce)));
      proxy.Add(remove);

      var removeContainer = new Command("remove_container", "Remove proxy container from servers");
      removeContainer.Hidden = true;
      removeContainer.Aliases.Add("remove-container");
      SetAction(removeContainer, "proxy", "remove_container", (_, context) => new ProxyCli(context).RemoveContainer());
      proxy.Add(removeContainer);

      var removeImage = new Command("remove_image", "Remove proxy image from servers");
      removeImage.Hidden = true;
      removeImage.Aliases.Add("remove-image");
      SetAction(removeImage, "proxy", "remove_image", (_, context) => new ProxyCli(context).RemoveImage());
      proxy.Add(removeImage);

      var removeProxyDirectory = new Command("remove_proxy_directory", "Remove the proxy directory from servers");
      removeProxyDirectory.Hidden = true;
      removeProxyDirectory.Aliases.Add("remove-proxy-directory");
      SetAction(removeProxyDirectory, "proxy", "remove_proxy_directory", (_, context) => new ProxyCli(context).RemoveProxyDirectory());
      proxy.Add(removeProxyDirectory);

      return proxy;
   }

   private static Command PruneCommands()
   {
      var prune = new Command("prune", "Prune old application images and containers");

      var all = new Command("all", "Prune unused images and stopped containers");
      SetAction(all, "prune", "all", (_, context) => new PruneCli(context).All());
      prune.Add(all);

      var images = new Command("images", "Prune unused images");
      SetAction(images, "prune", "images", (_, context) => new PruneCli(context).Images());
      prune.Add(images);

      var retain = new Option<int?>("--retain") { Description = "Number of containers to retain" };
      var containers = new Command("containers", "Prune all stopped containers, except the last n (default 5)") { retain };
      SetAction(containers, "prune", "containers", (parse, context) => new PruneCli(context).Containers(retain: parse.GetValue(retain)));
      prune.Add(containers);

      return prune;
   }

   private static Command RegistryCommands()
   {
      var registry = new Command("registry", "Login and -out of the image registry");

      Option<bool> SkipLocal() => new("--skip-local", "-L") { Description = "Skip local login" };
      Option<bool> SkipRemote() => new("--skip-remote", "-R") { Description = "Skip remote login" };

      var setupSkipLocal = SkipLocal();
      var setupSkipRemote = SkipRemote();
      var setup = new Command("setup", "Setup local registry or log in to remote registry locally and remotely") { setupSkipLocal, setupSkipRemote };
      SetAction(setup, "registry", "setup", (parse, context) => new RegistryCli(context).Setup(skipLocal: parse.GetValue(setupSkipLocal), skipRemote: parse.GetValue(setupSkipRemote)));
      registry.Add(setup);

      var removeSkipLocal = SkipLocal();
      var removeSkipRemote = SkipRemote();
      var remove = new Command("remove", "Remove local registry or log out of remote registry locally and remotely") { removeSkipLocal, removeSkipRemote };
      SetAction(remove, "registry", "remove", (parse, context) => new RegistryCli(context).Remove(skipLocal: parse.GetValue(removeSkipLocal), skipRemote: parse.GetValue(removeSkipRemote)));
      registry.Add(remove);

      var loginSkipLocal = SkipLocal();
      var loginSkipRemote = SkipRemote();
      var login = new Command("login", "Log in to remote registry locally and remotely") { loginSkipLocal, loginSkipRemote };
      SetAction(login, "registry", "login", (parse, context) => new RegistryCli(context).Login(skipLocal: parse.GetValue(loginSkipLocal), skipRemote: parse.GetValue(loginSkipRemote)));
      registry.Add(login);

      var logoutSkipLocal = SkipLocal();
      var logoutSkipRemote = SkipRemote();
      var logout = new Command("logout", "Log out of remote registry locally and remotely") { logoutSkipLocal, logoutSkipRemote };
      SetAction(logout, "registry", "logout", (parse, context) => new RegistryCli(context).Logout(skipLocal: parse.GetValue(logoutSkipLocal), skipRemote: parse.GetValue(logoutSkipRemote)));
      registry.Add(logout);

      return registry;
   }

   private static Command SecretsCommands()
   {
      var secrets = new Command("secrets", "Helpers for extracting secrets");

      var adapter = new Option<string>("--adapter", "-a") { Description = "Which vault adapter to use", Required = true };
      var account = new Option<string?>("--account") { Description = "The account identifier or username" };
      var from = new Option<string?>("--from") { Description = "A vault or folder to fetch the secrets from" };
      var fetchInline = new Option<bool>("--inline") { Hidden = true };
      var fetchSecrets = new Argument<string[]>("secrets") { Arity = ArgumentArity.ZeroOrMore, Description = "Secrets to fetch" };
      var fetch = new Command("fetch", "Fetch secrets from a vault") { adapter, account, from, fetchInline, fetchSecrets };
      SetAction(fetch, "secrets", "fetch", async (parse, context) =>
      {
         var result = await new SecretsCli(context).Fetch(
            parse.GetRequiredValue(adapter),
            parse.GetValue(fetchSecrets) ?? [],
            account: parse.GetValue(account),
            from: parse.GetValue(from),
            inline: parse.GetValue(fetchInline)).ConfigureAwait(false);

         if (parse.GetValue(fetchInline) && result is not null)
            Console.WriteLine(result);
      }, requiresCommander: false);
      secrets.Add(fetch);

      var extractName = new Argument<string>("name") { Description = "The secret to extract" };
      var extractSecrets = new Argument<string>("secrets") { Description = "JSON results of a fetch call" };
      var extractInline = new Option<bool>("--inline") { Hidden = true };
      var extract = new Command("extract", "Extract a single secret from the results of a fetch call") { extractName, extractSecrets, extractInline };
      SetAction(extract, "secrets", "extract", async (parse, context) =>
      {
         var result = await new SecretsCli(context).Extract(
            parse.GetRequiredValue(extractName),
            parse.GetRequiredValue(extractSecrets),
            inline: parse.GetValue(extractInline)).ConfigureAwait(false);

         if (parse.GetValue(extractInline) && result is not null)
            Console.WriteLine(result);
      }, requiresCommander: false);
      secrets.Add(extract);

      var print = new Command("print", "Print the secrets (for debugging)");
      SetAction(print, "secrets", "print", (_, context) => new SecretsCli(context).Print());
      secrets.Add(print);

      return secrets;
   }

   private static Command ServerCommands()
   {
      var server = new Command("server", "Bootstrap servers with curl and Docker");

      var bootstrap = new Command("bootstrap", "Set up Docker to run Kamal apps");
      SetAction(bootstrap, "server", "bootstrap", (_, context) => new ServerCli(context).Bootstrap());
      server.Add(bootstrap);

      var execCmd = new Argument<string[]>("cmd") { Arity = ArgumentArity.ZeroOrMore, Description = "Command to run" };
      var execInteractive = new Option<bool>("--interactive", "-i") { Description = "Run the command interactively (use for console/bash)" };
      var exec = new Command("exec", "Run a custom command on the server (use --help to show options)") { execCmd, execInteractive };
      SetAction(exec, "server", "exec", (parse, context) => new ServerCli(context).Exec(parse.GetValue(execCmd) ?? [], interactive: parse.GetValue(execInteractive)));
      server.Add(exec);

      return server;
   }

   // ----- Shared action plumbing ----------------------------------------------------------------

   private static Option<bool> ConfirmedOption() => new("--confirmed", "-y") { Description = "Proceed without confirmation question" };

   private static void SetAction(Command command, string topCommand, string? subcommand, Func<System.CommandLine.ParseResult, CliContext, Task> action, bool requiresCommander = true)
   {
      command.SetAction(async parseResult =>
      {
         var options = ReadGlobalOptions(parseResult);
         var context = new CliContext(options, topCommand, subcommand);

         if (requiresCommander)
            InitializeCommander(options);

         await action(parseResult, context).ConfigureAwait(false);
      });
   }

   private static CliOptions ReadGlobalOptions(System.CommandLine.ParseResult parseResult)
   {
      return new CliOptions
      {
         Verbose = parseResult.GetValue(VerboseOption),
         Quiet = parseResult.GetValue(QuietOption),
         Version = parseResult.GetValue(VersionOption),
         Primary = parseResult.GetValue(PrimaryOption),
         Hosts = parseResult.GetValue(HostsOption),
         Roles = parseResult.GetValue(RolesOption),
         ConfigFile = parseResult.GetValue(ConfigFileOption) ?? "config/deploy.yml",
         Destination = parseResult.GetValue(DestinationOption),
         SkipHooks = parseResult.GetValue(SkipHooksOption)
      };
   }

   /// <summary>Port of <c>initialize_commander</c> in base.rb.</summary>
   private static void InitializeCommander(CliOptions options)
   {
      var commander = KamalRuntime.Commander;

      if (commander.Configured)
         return;

      if (options.Verbose)
      {
         Environment.SetEnvironmentVariable("VERBOSE", "1"); // For backtraces via cli/start
         commander.Verbosity = Verbosity.Debug;
         KamalOutput.Verbosity = Verbosity.Debug;
      }

      if (options.Quiet)
      {
         commander.Verbosity = Verbosity.Error;
         KamalOutput.Verbosity = Verbosity.Error;
      }

      commander.Configure(
         configFile: Path.GetFullPath(options.ConfigFile),
         destination: options.Destination,
         version: options.Version);

      commander.SetSpecificHosts(options.Hosts?.Split(','));
      commander.SetSpecificRoles(options.Roles?.Split(','));

      if (options.Primary)
         commander.SpecificPrimary();
   }
}

/// <summary>Shared color helper for the top-level error reporting.</summary>
internal static class CliBaseColors
{
   public static string Red(string message)
   {
      if (Environment.GetEnvironmentVariable("NO_COLOR") is not null || Console.IsOutputRedirected)
         return message;

      return $"\x1b[31m{message}\x1b[0m";
   }
}
