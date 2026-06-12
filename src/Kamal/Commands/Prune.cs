using Kamal.Configuration;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::Prune</c>.</summary>
public class Prune : CommandsBase
{
   public Prune(KamalConfiguration config) : base(config)
   {
   }

   public object[] DanglingImages()
   {
      return Docker("image", "prune", "--force", "--filter", $"label=service={Config.Service}");
   }

   public object[] TaggedImages()
   {
      return Pipe(
         Docker("image", "ls", ServiceFilter, "--format", "'{{.ID}} {{.Repository}}:{{.Tag}}'"),
         Grep($"-v -w \"{ActiveImageList}\""),
         "while read image tag; do docker rmi $tag; done");
   }

   public object[] AppContainers(int retain)
   {
      return Pipe(
         Docker("ps", "-q", "-a", ServiceFilter, StoppedContainersFilters),
         $"tail -n +{retain + 1}",
         "while read container_id; do docker rm $container_id; done");
   }

   private static object[] StoppedContainersFilters =>
      new[] { "created", "exited", "dead" }.SelectMany(status => new object[] { "--filter", $"status={status}" }).ToArray();

   private string ActiveImageList
   {
      get
      {
         // Pull the images that are used by any containers
         // Append repo:latest - to avoid deleting the latest tag
         // Append repo:<none> - to avoid deleting dangling images that are in use. Unused dangling images are deleted separately
         return $"$(docker container ls -a --format '{{{{.Image}}}}\\|' --filter label=service={Config.Service} | tr -d '\\n'){Config.LatestImage}\\|{Config.Repository}:<none>";
      }
   }

   private object[] ServiceFilter => ["--filter", $"label=service={Config.Service}"];
}
