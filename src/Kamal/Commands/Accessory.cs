using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::Accessory</c> (the Proxy mixin lives in Accessory.Proxy.cs).</summary>
public sealed partial class Accessory : CommandsBase
{
   public Accessory(KamalConfiguration config, string name) : base(config)
   {
      AccessoryConfig = config.Accessory(name)
         ?? throw new ArgumentException($"No accessory by the name of '{name}'");
   }

   public Configuration.Accessory AccessoryConfig { get; }

   public string ServiceName => AccessoryConfig.ServiceName;
   public string Image => AccessoryConfig.Image;
   public List<string> Hosts => AccessoryConfig.Hosts;
   public string? Port => AccessoryConfig.Port;
   public object? Cmd => AccessoryConfig.Cmd;
   public string EnvDirectory => AccessoryConfig.EnvDirectory;
   public string SecretsIo => AccessoryConfig.SecretsIo;
   public string SecretsPath => AccessoryConfig.SecretsPath;
   public Configuration.Proxy? Proxy => AccessoryConfig.Proxy;
   public bool RunningProxy => AccessoryConfig.RunningProxy;
   public Configuration.Registry? Registry => AccessoryConfig.Registry;

   public object[] Run(string? host = null)
   {
      return Docker("run",
         "--name", ServiceName,
         "--detach",
         "--restart", "unless-stopped",
         AccessoryConfig.NetworkArgs,
         Config.LoggingArgs,
         AccessoryConfig.PublishArgs,
         host is null ? null : new object[] { "--env", $"KAMAL_HOST=\"{host}\"" },
         AccessoryConfig.EnvArgs,
         AccessoryConfig.VolumeArgs,
         AccessoryConfig.LabelArgs,
         AccessoryConfig.OptionArgs,
         Image,
         Cmd);
   }

   public object[] Start() => Docker("container", "start", ServiceName);

   public object[] Stop() => Docker("container", "stop", ServiceName);

   public object[] Info(bool all = false, bool quiet = false)
   {
      return Docker("ps", all ? "-a" : null, quiet ? "-q" : null, ServiceFilter);
   }

   public object[] Logs(bool timestamps = true, object? since = null, object? lines = null, string? grep = null, string? grepOptions = null)
   {
      return Pipe(
         Docker("logs",
            ServiceName,
            since is not null ? $" --since {RubyHelpers.RubyToS(since)}" : null,
            lines is not null ? $" --tail {RubyHelpers.RubyToS(lines)}" : null,
            timestamps ? "--timestamps" : null,
            "2>&1"),
         grep is not null ? $"grep '{grep}'{(grepOptions is not null ? $" {grepOptions}" : "")}" : null);
   }

   public string FollowLogs(bool timestamps = true, string? grep = null, string? grepOptions = null)
   {
      return RunOverSsh(
         Pipe(
            Docker("logs", ServiceName, timestamps ? "--timestamps" : null, "--tail", "10", "--follow", "2>&1"),
            grep is not null ? $"grep \"{grep}\"{(grepOptions is not null ? $" {grepOptions}" : "")}" : null));
   }

   public object[] ExecuteInExistingContainer(object[] command, bool interactive = false)
   {
      return Docker("exec",
         interactive ? DockerInteractiveArgs : null,
         ServiceName,
         command);
   }

   public object[] ExecuteInNewContainer(object[] command, bool interactive = false)
   {
      return Docker("run",
         interactive ? DockerInteractiveArgs : null,
         "--rm",
         AccessoryConfig.NetworkArgs,
         AccessoryConfig.EnvArgs,
         AccessoryConfig.VolumeArgs,
         AccessoryConfig.OptionArgs,
         Image,
         command);
   }

   public string ExecuteInExistingContainerOverSsh(params object[] command)
   {
      return RunOverSsh(ExecuteInExistingContainer(command, interactive: true));
   }

   public string ExecuteInNewContainerOverSsh(params object[] command)
   {
      return RunOverSsh(ExecuteInNewContainer(command, interactive: true));
   }

   /// <summary>Ruby's override of <c>run_over_ssh</c>: always targets the first accessory host.</summary>
   public string RunOverSsh(object command) => RunOverSsh(command, host: Hosts.First());

   /// <summary>
   /// Port of <c>ensure_local_file_present</c>.
   /// DEVIATION: Ruby also accepts StringIO content (rendered .erb files); the C# port only checks paths.
   /// </summary>
   public void EnsureLocalFilePresent(string localFile)
   {
      if (!File.Exists(localFile))
         throw new InvalidOperationException($"Missing file: {localFile}");
   }

   public object[] PullImage() => Docker("image", "pull", Image);

   public object[] RemoveServiceDirectory() => ["rm", "-rf", ServiceName];

   public object[] RemoveContainer() => Docker("container", "prune", "--force", ServiceFilter);

   public object[] RemoveImage() => Docker("image", "rm", "--force", Image);

   public object[] EnsureEnvDirectory() => MakeDirectory(EnvDirectory);

   private object[] ServiceFilter => ["--filter", $"label=service={ServiceName}"];
}
