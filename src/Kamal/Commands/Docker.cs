using Kamal.Configuration;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::Docker</c>.</summary>
public class Docker : CommandsBase
{
   public Docker(KamalConfiguration config) : base(config)
   {
   }

   /// <summary>Install Docker using the https://github.com/docker/docker-install convenience script.</summary>
   public object[] Install() => Pipe(GetDocker(), "sh");

   /// <summary>Checks the Docker client version. Fails if Docker is not installed.</summary>
   public object[] Installed() => Docker_("-v");

   /// <summary>Checks the Docker server version. Fails if Docker is not running.</summary>
   public object[] Running() => Docker_("version");

   /// <summary>Do we have superuser access to install Docker and start system services?</summary>
   public object[] Superuser() => ["[ \"${EUID:-$(id -u)}\" -eq 0 ] || sudo -nl usermod >/dev/null"];

   public object[] Root() => ["[ \"${EUID:-$(id -u)}\" -eq 0 ]"];

   public object[] InDockerGroup() => ["id -nG \"${USER:-$(id -un)}\" | grep -qw docker"];

   public object[] AddToDockerGroup() => ["sudo -n usermod -aG docker \"${USER:-$(id -un)}\""];

   public object[] RefreshSession() => ["kill -HUP $PPID"];

   public object[] CreateNetwork() => Docker_("network", "create", "kamal");

   private static object[] GetDocker()
   {
      return Shell(
         Any(
            new object[] { "curl", "-fsSL", "https://get.docker.com" },
            new object[] { "wget", "-O -", "https://get.docker.com" },
            new object[] { "echo", "\"exit 1\"" }));
   }

   // The base Docker(...) helper is shadowed by this class' name; this private alias keeps call sites readable.
   private static object[] Docker_(params object?[] args) => Docker(args);
}
