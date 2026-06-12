using System.Collections;
using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>
/// Port of <c>Kamal::Commands::Base</c>: shared helpers for building shell command token arrays.
/// Command methods return <c>object[]</c> token arrays (strings or <see cref="Sensitive"/> values);
/// nested arrays are flattened and nulls dropped, mirroring Ruby's array flatten + compact, and the
/// execution layer joins tokens with spaces the way SSHKit does.
/// </summary>
public abstract class CommandsBase
{
   public const string DockerHealthStatusFormat = "'{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}'";

   /// <summary>Ruby's <c>STDIN.isatty</c>; replaceable in tests (Ruby tests stub stdin).</summary>
   public static Func<bool> StdinTty { get; set; } = static () => !Console.IsInputRedirected;

   protected CommandsBase(KamalConfiguration config)
   {
      Config = config;
   }

   public KamalConfiguration Config { get; set; }

   /// <summary>Port of <c>run_over_ssh</c>: wraps a command in an ssh invocation for interactive use.</summary>
   public string RunOverSsh(object command, string host)
   {
      var joined = JoinTokens(command).Replace("'", "'\\''");

      return $"ssh{SshConfigArgs}{SshProxyArgs}{SshKeysArgs} -t {Config.Ssh.User}@{host} -p {RubyHelpers.RubyToS(Config.Ssh.Port)} '{joined}'";
   }

   /// <summary>Port of <c>container_id_for</c>.</summary>
   public object[] ContainerIdFor(string containerName, bool onlyRunning = false)
   {
      return Docker("container", "ls", onlyRunning ? null : "--all", "--filter", $"'name=^{containerName}$'", "--quiet");
   }

   /// <summary>Port of <c>make_directory_for</c>: make the parent directory of a remote file.</summary>
   public object[] MakeDirectoryFor(string remoteFile) => MakeDirectory(UnixDirname(remoteFile));

   public object[] MakeDirectory(string path) => ["mkdir", "-p", path];

   public object[] RemoveDirectory(string path) => ["rm", "-r", path];

   public object[] RemoveFile(string path) => ["rm", path];

   /// <summary>Port of <c>ensure_docker_installed</c>.</summary>
   public object[] EnsureDockerInstalled()
   {
      return Combine(
         EnsureLocalDockerInstalled(),
         EnsureLocalBuildxInstalled());
   }

   /// <summary>
   /// Flattens nested token arrays into a single token list, dropping nulls
   /// (Ruby's <c>Array#flatten</c> + the <c>compact</c> calls sprinkled through the commands).
   /// </summary>
   public static object[] Flatten(params object?[] command)
   {
      var result = new List<object>();
      AddFlattened(result, command);

      return result.ToArray();
   }

   /// <summary>Joins flattened tokens with spaces, rendering <see cref="Sensitive"/> values unredacted (Ruby's <c>Array#join</c>).</summary>
   public static string JoinTokens(params object?[] command)
   {
      return string.Join(" ", Flatten(command).Select(token => RubyHelpers.RubyToS(token)));
   }

   private static void AddFlattened(List<object> result, object? token)
   {
      switch (token)
      {
         case null:
            return;
         case string or Sensitive:
            result.Add(token);
            return;
         case IEnumerable enumerable:
            foreach (var item in enumerable)
               AddFlattened(result, item);
            return;
         default:
            result.Add(token);
            return;
      }
   }

   /// <summary>Port of <c>combine</c> with the default <c>&amp;&amp;</c> combiner.</summary>
   protected static object[] Combine(params object?[] commands) => CombineBy("&&", commands);

   /// <summary>Port of <c>combine(*commands, by:)</c>.</summary>
   protected static object[] CombineBy(string by, params object?[] commands)
   {
      var result = new List<object>();

      foreach (var command in commands)
      {
         if (command is null)
            continue;

         result.AddRange(Flatten(command));
         result.Add(by);
      }

      if (result.Count > 0)
         result.RemoveAt(result.Count - 1); // Remove trailing combiner

      return result.ToArray();
   }

   protected static object[] Chain(params object?[] commands) => CombineBy(";", commands);

   protected static object[] Pipe(params object?[] commands) => CombineBy("|", commands);

   protected static object[] Append(params object?[] commands) => CombineBy(">>", commands);

   protected static object[] Write(params object?[] commands) => CombineBy(">", commands);

   protected static object[] Any(params object?[] commands) => CombineBy("||", commands);

   /// <summary>Port of <c>substitute</c>: wraps a command in <c>$(...)</c>.</summary>
   protected static string Substitute(params object?[] commands) => $"$({JoinTokens(commands)})";

   protected static object[] Xargs(params object?[] command) => Flatten("xargs", command);

   /// <summary>Port of <c>shell</c>: wraps a command in <c>sh -c '...'</c> with single quotes escaped.</summary>
   protected static object[] Shell(params object?[] command)
   {
      return ["sh", "-c", $"'{JoinTokens(command).Replace("'", "'\\''")}'"];
   }

   protected static object[] Docker(params object?[] args) => Flatten("docker", args);

   protected static object[] PackCmd(params object?[] args) => Flatten("pack", args);

   /// <summary>Port of <c>git(*args, path:)</c>.</summary>
   protected static object[] Git(object?[] args, string? path = null)
   {
      return Flatten("git", path is null ? null : new object[] { "-C", path }, args);
   }

   protected static object[] Grep(params object?[] args) => Flatten("grep", args);

   /// <summary>Port of <c>tags(**details)</c>.</summary>
   protected KamalTags Tags(params KeyValuePair<string, object?>[] details) => KamalTags.FromConfig(Config, details);

   /// <summary>Ruby's <c>Pathname#dirname</c> for the unix paths Kamal builds.</summary>
   protected static string UnixDirname(string path)
   {
      var trimmed = path.TrimEnd('/');
      var index = trimmed.LastIndexOf('/');

      return index switch
      {
         < 0 => ".",
         0 => "/",
         _ => trimmed[..index]
      };
   }

   /// <summary>Ruby's <c>Pathname#basename</c> for the unix paths Kamal builds.</summary>
   protected static string UnixBasename(string path)
   {
      var trimmed = path.TrimEnd('/');
      var index = trimmed.LastIndexOf('/');

      return index < 0 ? trimmed : trimmed[(index + 1)..];
   }

   protected static string DockerInteractiveArgs => StdinTty() ? "-it" : "-i";

   private string SshConfigArgs
   {
      get
      {
         return Config.Ssh.Config switch
         {
            string file => $" -F {file}",
            true => "", // Use default SSH config
            false => " -F /dev/null", // Ignore SSH config
            IEnumerable files => string.Concat(files.Cast<object?>().Select(file => $" -F {RubyHelpers.RubyToS(file)}")),
            _ => ""
         };
      }
   }

   private string SshProxyArgs
   {
      get
      {
         return Config.Ssh.Proxy switch
         {
            SshJumpProxy jump => $" -J {jump.JumpProxies}",
            SshCommandProxy command => $" -o ProxyCommand='{command.Command}'",
            _ => ""
         };
      }
   }

   private string SshKeysArgs
   {
      get
      {
         var keys = SshKeys is { } sshKeys ? string.Concat(sshKeys) : "";
         var keysOnly = RubyHelpers.IsPresent(Config.Ssh.KeysOnly) ? " -o IdentitiesOnly=yes" : "";

         return keys + keysOnly;
      }
   }

   private List<string>? SshKeys
   {
      get
      {
         if (Config.Ssh.Keys is not IEnumerable keys || Config.Ssh.Keys is string)
            return null;

         return keys.Cast<object?>().Select(key => $" -i {RubyHelpers.RubyToS(key)}").ToList();
      }
   }

   private static object[] EnsureLocalDockerInstalled() => Docker("--version");

   private static object[] EnsureLocalBuildxInstalled() => Docker("buildx", "version");
}
