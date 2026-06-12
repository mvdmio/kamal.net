using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::App::Proxy</c>.</summary>
public sealed partial class App
{
   public object[] Deploy(string target)
   {
      return ProxyExec("deploy", Role!.ContainerPrefix, Role.Proxy!.DeployCommandArgs(target: target));
   }

   public object[] Remove() => ProxyExec("remove", Role!.ContainerPrefix);

   public object[] Live() => ProxyExec("resume", Role!.ContainerPrefix);

   public object[] Maintenance(object? drainTimeout = null, string? message = null)
   {
      return ProxyExec("stop", Role!.ContainerPrefix, Role.Proxy!.StopCommandArgs(drainTimeout, message));
   }

   public object[] RemoveProxyAppDirectory() => RemoveDirectory(Config.ProxyBoot.AppDirectory);

   public object[] CreateSslDirectory() => MakeDirectory(RubyHelpers.JoinPath(Config.ProxyBoot.TlsDirectory, Role!.Name));

   private string ProxyContainerName => Config.ProxyBoot.ContainerName;

   private object[] ProxyExec(params object?[] command) => Docker("exec", ProxyContainerName, "kamal-proxy", command);
}
