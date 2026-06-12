namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::App::ErrorPages</c>.</summary>
public sealed partial class App
{
   public object[] CreateErrorPagesDirectory() => MakeDirectory(Config.ProxyBoot.ErrorPagesDirectory);

   public object[] CleanUpErrorPages()
   {
      return
      [
         "find", Config.ProxyBoot.ErrorPagesDirectory,
         "-mindepth", "1", "-maxdepth", "1", "!", "-name", Config.Version, "-exec", "rm", "-rf", "{} +"
      ];
   }
}
