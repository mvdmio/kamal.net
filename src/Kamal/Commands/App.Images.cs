namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::App::Images</c>.</summary>
public sealed partial class App
{
   public object[] ListImages() => Docker("image", "ls", Config.Repository);

   public object[] RemoveImages() => Docker("image", "prune", "--all", "--force", ImageFilterArgs);

   public object[] TagLatestImage() => Docker("tag", Config.AbsoluteImage, Config.LatestImage);
}
