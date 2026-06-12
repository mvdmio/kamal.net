using System.Security.Cryptography;
using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::App::Execution</c>.</summary>
public sealed partial class App
{
   public object[] ExecuteInExistingContainer(object[] command, IDictionary<string, object?> env, bool interactive = false)
   {
      return Docker("exec",
         interactive ? DockerInteractiveArgs : null,
         KamalUtils.Argumentize("--env", env),
         ContainerName(),
         command);
   }

   public object[] ExecuteInNewContainer(object[] command, IDictionary<string, object?> env, bool interactive = false, bool detach = false)
   {
      return Docker("run",
         interactive ? DockerInteractiveArgs : null,
         detach ? "--detach" : null,
         detach ? null : "--rm",
         "--name", ContainerNameForExec,
         "--network", "kamal",
         Role?.EnvArgs(Host!),
         KamalUtils.Argumentize("--env", env),
         Role!.LoggingArgs,
         Config.VolumeArgs,
         Role?.OptionArgs,
         Config.AbsoluteImage,
         command);
   }

   public string ExecuteInExistingContainerOverSsh(object[] command, IDictionary<string, object?> env)
   {
      return RunOverSsh(ExecuteInExistingContainer(command, env, interactive: true), host: Host!);
   }

   public string ExecuteInNewContainerOverSsh(object[] command, IDictionary<string, object?> env)
   {
      return RunOverSsh(ExecuteInNewContainer(command, env, interactive: true), host: Host!);
   }

   private string ContainerNameForExec =>
      string.Join("-", new[] { Role!.ContainerPrefix, "exec", Config.Version, RandomHex(3) }.Where(part => part is not null));

   private static string RandomHex(int bytes)
   {
      var buffer = new byte[bytes];
      RandomNumberGenerator.Fill(buffer);

      return Convert.ToHexStringLower(buffer);
   }
}
