using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::App::Logging</c>.</summary>
public sealed partial class App
{
   public object[] Logs(object? containerId = null, bool timestamps = true, object? since = null, object? lines = null, string? grep = null, string? grepOptions = null)
   {
      return Pipe(
         ContainerIdCommand(containerId),
         $"xargs docker logs{(timestamps ? " --timestamps" : "")}{(since is not null ? $" --since {RubyHelpers.RubyToS(since)}" : "")}{(lines is not null ? $" --tail {RubyHelpers.RubyToS(lines)}" : "")} 2>&1",
         grep is not null ? $"grep '{grep}'{(grepOptions is not null ? $" {grepOptions}" : "")}" : null);
   }

   public string FollowLogs(string host, object? containerId = null, bool timestamps = true, object? lines = null, string? grep = null, string? grepOptions = null)
   {
      return RunOverSsh(
         Pipe(
            ContainerIdCommand(containerId),
            $"xargs docker logs{(timestamps ? " --timestamps" : "")}{(lines is not null ? $" --tail {RubyHelpers.RubyToS(lines)}" : "")} --follow 2>&1",
            grep is not null ? $"grep \"{grep}\"{(grepOptions is not null ? $" {grepOptions}" : "")}" : null),
         host: host);
   }

   private object ContainerIdCommand(object? containerId)
   {
      return containerId switch
      {
         object[] command => command,
         string id => $"echo {id}",
         _ => CurrentRunningContainerId()
      };
   }
}
