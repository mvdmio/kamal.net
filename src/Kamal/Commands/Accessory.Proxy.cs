namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::Accessory::Proxy</c>.</summary>
public sealed partial class Accessory
{
   public object[] Deploy(string target)
   {
      return ProxyExec("deploy", ServiceName, Proxy!.DeployCommandArgs(target: target));
   }

   public object[] Remove() => ProxyExec("remove", ServiceName);

   private string ProxyContainerName => Config.ProxyBoot.ContainerName;

   private object[] ProxyExec(params object?[] command) => Docker("exec", ProxyContainerName, "kamal-proxy", command);
}
