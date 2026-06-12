using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>
/// Port of <c>Kamal::Commands::App</c>. The Ruby mixin modules (Assets, Containers, ErrorPages,
/// Execution, Images, Logging, Proxy) live in matching partial-class files.
/// </summary>
public sealed partial class App : CommandsBase
{
   public static readonly string[] ActiveDockerStatuses = ["running", "restarting"];

   public App(KamalConfiguration config, Role? role = null, string? host = null) : base(config)
   {
      Role = role;
      Host = host;
   }

   public Role? Role { get; }
   public string? Host { get; }

   public string ContainerName(string? version = null) => Role!.ContainerName(version);

   public object[] Run(string? hostname = null)
   {
      return Docker("run",
         "--detach",
         "--restart unless-stopped",
         "--name", ContainerName(),
         "--network", "kamal",
         hostname is null ? null : new object[] { "--hostname", hostname },
         "--env", $"KAMAL_CONTAINER_NAME=\"{ContainerName()}\"",
         "--env", $"KAMAL_VERSION=\"{Config.Version}\"",
         "--env", $"KAMAL_HOST=\"{Host}\"",
         Config.Destination is null ? null : new object[] { "--env", $"KAMAL_DESTINATION=\"{Config.Destination}\"" },
         Role!.EnvArgs(Host!),
         Role.LoggingArgs,
         Config.VolumeArgs,
         Role.AssetVolumeArgs,
         Role.LabelArgs,
         Role.OptionArgs,
         Config.AbsoluteImage,
         Role.Cmd);
   }

   public object[] Start() => Docker("start", ContainerName());

   public object[] Status(string version)
   {
      return Pipe(ContainerIdForVersion(version), Xargs(Docker("inspect", "--format", DockerHealthStatusFormat)));
   }

   public object[] Stop(string? version = null)
   {
      return Pipe(
         version is not null ? ContainerIdForVersion(version) : CurrentRunningContainerId(),
         Xargs(Docker("stop", Role!.StopArgs)));
   }

   public object[] Info() => Docker("ps", ContainerFilterArgs());

   public object[] CurrentRunningContainerId() => CurrentRunningContainer(format: "--quiet");

   public object[] ContainerIdForVersion(string version, bool onlyRunning = false)
   {
      return ContainerIdFor(containerName: ContainerName(version), onlyRunning: onlyRunning);
   }

   public object[] CurrentRunningVersion()
   {
      return Pipe(
         CurrentRunningContainer(format: "--format '{{.Names}}'"),
         ExtractVersionFromName);
   }

   public object[] ListVersions(object[]? dockerArgs = null, string[]? statuses = null)
   {
      return Pipe(
         Docker("ps", ContainerFilterArgs(statuses: statuses), dockerArgs, "--format", "\"{{.Names}}\""),
         ExtractVersionFromName);
   }

   public object[] EnsureEnvDirectory() => MakeDirectory(Role!.EnvDirectory);

   private object[] LatestImageId()
   {
      return Docker("image", "ls", KamalUtils.Argumentize("--filter", $"reference={Config.LatestImage}"), "--format", "'{{.ID}}'");
   }

   private object[] CurrentRunningContainer(string format)
   {
      return Pipe(
         Shell(Chain(LatestImageContainer(format: format), LatestContainer(format: format))),
         new object[] { "head", "-1" });
   }

   private object[] LatestImageContainer(string format)
   {
      return LatestContainer(format: format, filters: [$"ancestor=$({JoinTokens(LatestImageId())})"]);
   }

   private object[] LatestContainer(string format, List<string>? filters = null)
   {
      return Docker("ps", "--latest", format, ContainerFilterArgs(statuses: ActiveDockerStatuses), KamalUtils.Argumentize("--filter", filters));
   }

   private List<object> ContainerFilterArgs(string[]? statuses = null)
   {
      return KamalUtils.Argumentize("--filter", ContainerFilters(statuses: statuses));
   }

   private List<object> ImageFilterArgs => KamalUtils.Argumentize("--filter", ImageFilters);

   // Extract SHA from "service-role-dest-SHA"
   private string ExtractVersionFromName => $"while read line; do echo ${{line#{Role!.ContainerPrefix}-}}; done";

   private List<string> ContainerFilters(string[]? statuses = null)
   {
      var filters = new List<string> { $"label=service={Config.Service}" };

      filters.Add($"label=destination={Config.Destination}");

      if (Role is not null)
         filters.Add($"label=role={Role}");

      foreach (var status in statuses ?? [])
         filters.Add($"status={status}");

      return filters;
   }

   private List<string> ImageFilters => [$"label=service={Config.Service}"];
}
