using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::Proxy</c>.</summary>
public class Proxy : CommandsBase
{
   public Proxy(KamalConfiguration config, string host) : base(config)
   {
      ProxyRunConfig = config.ProxyRunFor(host);
   }

   public ProxyRun? ProxyRunConfig { get; }

   public object[] Run()
   {
      if (ProxyRunConfig is not null)
      {
         return Docker(
            "run",
            "--name", ContainerName,
            "--network", "kamal",
            "--detach",
            "--restart", "unless-stopped",
            "--volume", "kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy",
            ProxyRunConfig.DockerOptionsArgs,
            ProxyRunConfig.Image,
            ProxyRunConfig.RunCommand);
      }

      return Pipe(BootConfig(), Xargs(DockerRun()));
   }

   public object[] Start() => Docker("container", "start", ContainerName);

   public object[] Stop(string? name = null) => Docker("container", "stop", name ?? ContainerName);

   public object[] StartOrRun() => CombineBy("||", Start(), Run());

   public object[] Info() => Docker("ps", "--filter", $"'name=^{ContainerName}$'");

   public object[] Version()
   {
      return Pipe(
         Docker("inspect", ContainerName, "--format '{{.Config.Image}}'"),
         new object[] { "awk", "-F:", "'{print $NF}'" });
   }

   public object[] Logs(bool timestamps = true, object? since = null, object? lines = null, string? grep = null, string? grepOptions = null)
   {
      return Pipe(
         Docker("logs",
            ContainerName,
            since is not null ? $"--since {RubyHelpers.RubyToS(since)}" : null,
            lines is not null ? $"--tail {RubyHelpers.RubyToS(lines)}" : null,
            timestamps ? "--timestamps" : null,
            "2>&1"),
         grep is not null ? $"grep '{grep}'{(grepOptions is not null ? $" {grepOptions}" : "")}" : null);
   }

   public string FollowLogs(string host, bool timestamps = true, string? grep = null, string? grepOptions = null)
   {
      return RunOverSsh(
         JoinTokens(Pipe(
            Docker("logs", ContainerName, timestamps ? "--timestamps" : null, "--tail", "10", "--follow", "2>&1"),
            grep is not null ? $"grep \"{grep}\"{(grepOptions is not null ? $" {grepOptions}" : "")}" : null)),
         host: host);
   }

   public object[] RemoveContainer()
   {
      return Docker("container", "prune", "--force", "--filter", "label=org.opencontainers.image.title=kamal-proxy");
   }

   public object[] RemoveImage()
   {
      return Docker("image", "prune", "--all", "--force", "--filter", "label=org.opencontainers.image.title=kamal-proxy");
   }

   public object[] CleanupTraefik()
   {
      return Chain(
         Docker("container", "stop", "traefik"),
         Combine(
            Docker("container", "prune", "--force", "--filter", "label=org.opencontainers.image.title=Traefik"),
            Docker("image", "prune", "--all", "--force", "--filter", "label=org.opencontainers.image.title=Traefik")));
   }

   public object[] EnsureProxyDirectory() => MakeDirectory(Config.ProxyBoot.HostDirectory);

   public object[] RemoveProxyDirectory() => RemoveDirectory(Config.ProxyBoot.HostDirectory);

   public object[] EnsureAppsConfigDirectory() => MakeDirectory(Config.ProxyBoot.AppsDirectory);

   public object[] BootConfig()
   {
      return ["echo", $"{Substitute(ReadBootOptions())} {Substitute(ReadImage())}:{Substitute(ReadImageVersion())} {Substitute(ReadRunCommand())}"];
   }

   public object[] ReadBootOptions()
   {
      return ReadFile(Config.ProxyBoot.OptionsFile, defaultValue: JoinTokens(Config.ProxyBoot.DefaultBootOptions));
   }

   public object[] ReadImage() => ReadFile(Config.ProxyBoot.ImageFile, defaultValue: Config.ProxyBoot.ImageDefault);

   public object[] ReadImageVersion() => ReadFile(Config.ProxyBoot.ImageVersionFile, defaultValue: ProxyRun.MinimumVersion);

   public object[] ReadRunCommand() => ReadFile(Config.ProxyBoot.RunCommandFile);

   public object[] ResetBootOptions() => RemoveFile(Config.ProxyBoot.OptionsFile);

   public object[] ResetImage() => RemoveFile(Config.ProxyBoot.ImageFile);

   public object[] ResetImageVersion() => RemoveFile(Config.ProxyBoot.ImageVersionFile);

   public object[] ResetRunCommand() => RemoveFile(Config.ProxyBoot.RunCommandFile);

   private string ContainerName => Config.ProxyBoot.ContainerName;

   private static object[] ReadFile(string file, string? defaultValue = null)
   {
      return CombineBy("||",
         new object[] { "cat", file, "2>", "/dev/null" },
         new object[] { "echo", $"\"{defaultValue}\"" });
   }

   private object[] DockerRun()
   {
      return Docker(
         "run",
         "--name", ContainerName,
         "--network", "kamal",
         "--detach",
         "--restart", "unless-stopped",
         "--volume", "kamal-proxy-config:/home/kamal-proxy/.config/kamal-proxy",
         Config.ProxyBoot.AppsVolume.DockerArgs);
   }
}
