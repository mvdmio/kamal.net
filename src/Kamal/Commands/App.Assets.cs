using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::App::Assets</c>.</summary>
public sealed partial class App
{
   public object[] ExtractAssets()
   {
      var assetContainer = $"{Role!.ContainerPrefix}-assets";

      return CombineBy("&&",
         MakeDirectory(Role.AssetExtractedDirectory()),
         Flatten(Docker("container", "rm", assetContainer, "2> /dev/null"), "|| true"),
         Docker("container", "create", "--name", assetContainer, Config.AbsoluteImage),
         Docker("container", "cp", "-L", $"{assetContainer}:{Role.AssetPath}/.", Role.AssetExtractedDirectory()),
         Docker("container", "rm", assetContainer));
   }

   public object[] SyncAssetVolumes(string? oldVersion = null)
   {
      var newExtractedPath = Role!.AssetExtractedDirectory(Config.Version);
      var newVolumePath = Role.AssetVolume()!.HostPath;

      var commands = new List<object?>
      {
         MakeDirectory(newVolumePath),
         CopyContents(newExtractedPath, newVolumePath)
      };

      if (RubyHelpers.IsPresent(oldVersion))
      {
         var oldExtractedPath = Role.AssetExtractedDirectory(oldVersion);
         var oldVolumePath = Role.AssetVolume(oldVersion)!.HostPath;

         commands.Add(CopyContents(newExtractedPath, oldVolumePath, continueOnError: true));
         commands.Add(CopyContents(oldExtractedPath, newVolumePath, continueOnError: true));
      }

      return Chain(commands.ToArray());
   }

   public object[] CleanUpAssets()
   {
      return Chain(
         FindAndRemoveOlderSiblings(Role!.AssetExtractedDirectory()),
         FindAndRemoveOlderSiblings(Role.AssetVolumeDirectory()));
   }

   private object[] FindAndRemoveOlderSiblings(string path)
   {
      return
      [
         "find",
         UnixDirname(path),
         "-maxdepth 1",
         "-name", $"'{Role!.Name}-*'",
         "!", "-name", UnixBasename(path),
         "-exec rm -rf \"{}\" +"
      ];
   }

   private static object[] CopyContents(string source, string destination, bool continueOnError = false)
   {
      return Flatten("cp", "-rnT", source, destination, continueOnError ? "|| true" : null);
   }
}
