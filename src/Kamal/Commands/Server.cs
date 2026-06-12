using Kamal.Configuration;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::Server</c>.</summary>
public class Server : CommandsBase
{
   public Server(KamalConfiguration config) : base(config)
   {
   }

   public object[] EnsureRunDirectory() => MakeDirectory(Config.RunDirectory);

   public object[] RemoveAppDirectory() => RemoveDirectory(Config.AppDirectory);

   public object[] AppDirectoryCount()
   {
      return Pipe(
         new object[] { "ls", Config.AppsDirectory },
         new object[] { "wc", "-l" });
   }
}
