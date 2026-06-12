namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::App::Containers</c>.</summary>
public sealed partial class App
{
   public const string DockerHealthLogFormat = "'{{json .State.Health}}'";

   public object[] ListContainers() => Docker("container", "ls", "--all", ContainerFilterArgs());

   public object[] ListContainerNames() => Flatten(ListContainers(), "--format", "'{{ .Names }}'");

   public object[] RemoveContainer(string version)
   {
      return Pipe(
         ContainerIdFor(containerName: ContainerName(version)),
         Xargs(Docker("container", "rm")));
   }

   public object[] RenameContainer(string version, string newVersion)
   {
      return Docker("rename", ContainerName(version), ContainerName(newVersion));
   }

   public object[] RemoveContainers() => Docker("container", "prune", "--force", ContainerFilterArgs());

   public object[] ContainerHealthLog(string version)
   {
      return Pipe(
         ContainerIdFor(containerName: ContainerName(version)),
         Xargs(Docker("inspect", "--format", DockerHealthLogFormat)));
   }
}
