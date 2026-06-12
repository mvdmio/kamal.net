using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::Registry</c>.</summary>
public class Registry : CommandsBase
{
   public Registry(KamalConfiguration config) : base(config)
   {
   }

   /// <summary>Port of <c>login</c>; returns null for a local registry (Ruby's early return).</summary>
   public object[]? Login(Configuration.Registry? registryConfig = null)
   {
      registryConfig ??= Config.Registry;

      if (registryConfig.Local)
         return null;

      return Docker("login",
         registryConfig.Server,
         "-u", new Sensitive(KamalUtils.EscapeShellValue(registryConfig.Username)),
         "-p", new Sensitive(KamalUtils.EscapeShellValue(registryConfig.Password)));
   }

   public object[] Logout(Configuration.Registry? registryConfig = null)
   {
      registryConfig ??= Config.Registry;

      return Docker("logout", registryConfig.Server);
   }

   public object[] Setup(Configuration.Registry? registryConfig = null)
   {
      registryConfig ??= Config.Registry;

      return CombineBy("||",
         Docker("start", "kamal-docker-registry"),
         Docker("run", "--detach", "-p", $"127.0.0.1:{registryConfig.LocalPort}:5000", "--name", "kamal-docker-registry", "registry:3"));
   }

   public object[] Remove()
   {
      return CombineBy("&&",
         Docker("stop", "kamal-docker-registry"),
         Docker("rm", "kamal-docker-registry"));
   }

   public bool Local => Config.Registry.Local;
}
