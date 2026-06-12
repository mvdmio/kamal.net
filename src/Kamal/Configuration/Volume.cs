using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Volume</c>.</summary>
public sealed class Volume
{
   public Volume(string hostPath, string containerPath, string? options = null)
   {
      HostPath = hostPath;
      ContainerPath = containerPath;
      Options = options;
   }

   public string HostPath { get; }
   public string ContainerPath { get; }
   public string? Options { get; }

   public List<object> DockerArgs => KamalUtils.Argumentize("--volume", DockerArgsString);

   public string DockerArgsString
   {
      get
      {
         var volumeString = $"{HostPathForDockerVolume}:{ContainerPath}";
         if (RubyHelpers.IsPresent(Options))
            volumeString += $":{Options}";

         return volumeString;
      }
   }

   private string HostPathForDockerVolume => IsAbsolutePath(HostPath) ? HostPath : $"$PWD/{HostPath}";

   // Remote paths are unix paths; Ruby's Pathname#absolute? checks for a leading "/".
   internal static bool IsAbsolutePath(string path) => path.StartsWith('/');
}
