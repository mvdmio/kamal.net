using System.Net;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Proxy::Boot</c>: host-side proxy directories and boot defaults.</summary>
public sealed class ProxyBoot
{
   private readonly KamalConfiguration _config;

   public ProxyBoot(KamalConfiguration config)
   {
      _config = config;
   }

   public string PublishArgs(object? httpPort, object? httpsPort, List<string?>? bindIps = null)
   {
      EnsureValidBindIps(bindIps);

      var args = (bindIps ?? [null]).Select(bindIp =>
      {
         var formattedIp = ProxyRun.FormatBindIp(bindIp);
         var publishHttp = string.Join(":", new[] { formattedIp, RubyHelpers.RubyToS(httpPort), ProxyRun.DefaultHttpPort.ToString() }.Where(part => part is not null));
         var publishHttps = string.Join(":", new[] { formattedIp, RubyHelpers.RubyToS(httpsPort), ProxyRun.DefaultHttpsPort.ToString() }.Where(part => part is not null));

         return string.Join(" ", KamalUtils.Argumentize("--publish", new List<object?> { publishHttp, publishHttps }).Select(RubyHelpers.RubyToS));
      });

      return string.Join(" ", args);
   }

   public List<object>? LoggingArgs(string? maxSize)
   {
      return RubyHelpers.IsPresent(maxSize) ? KamalUtils.Argumentize("--log-opt", $"max-size={maxSize}") : null;
   }

   public List<object> DefaultBootOptions
   {
      get
      {
         var options = new List<object> { PublishArgs(ProxyRun.DefaultHttpPort, ProxyRun.DefaultHttpsPort) };
         options.AddRange(LoggingArgs(ProxyRun.DefaultLogMaxSize) ?? []);
         return options;
      }
   }

   public string RepositoryName => "basecamp";

   public string ImageName => "kamal-proxy";

   public string ImageDefault => $"{RepositoryName}/{ImageName}";

   public string ContainerName => "kamal-proxy";

   public string HostDirectory => RubyHelpers.JoinPath(_config.RunDirectory, "proxy");

   public string OptionsFile => RubyHelpers.JoinPath(HostDirectory, "options");

   public string ImageFile => RubyHelpers.JoinPath(HostDirectory, "image");

   public string ImageVersionFile => RubyHelpers.JoinPath(HostDirectory, "image_version");

   public string RunCommandFile => RubyHelpers.JoinPath(HostDirectory, "run_command");

   public string AppsDirectory => RubyHelpers.JoinPath(HostDirectory, "apps-config");

   public string AppsContainerDirectory => "/home/kamal-proxy/.apps-config";

   public Volume AppsVolume => new(hostPath: AppsDirectory, containerPath: AppsContainerDirectory);

   public string AppDirectory => RubyHelpers.JoinPath(AppsDirectory, _config.ServiceAndDestination);

   public string AppContainerDirectory => RubyHelpers.JoinPath(AppsContainerDirectory, _config.ServiceAndDestination);

   public string ErrorPagesDirectory => RubyHelpers.JoinPath(AppDirectory, "error_pages");

   public string ErrorPagesContainerDirectory => RubyHelpers.JoinPath(AppContainerDirectory, "error_pages");

   public string TlsDirectory => RubyHelpers.JoinPath(AppDirectory, "tls");

   public string TlsContainerDirectory => RubyHelpers.JoinPath(AppContainerDirectory, "tls");

   private static void EnsureValidBindIps(List<string?>? bindIps)
   {
      if (bindIps is null || bindIps.Count == 0)
         return;

      foreach (var ip in bindIps)
      {
         if (ip is null || !IPAddress.TryParse(ip, out _))
            throw new ArgumentException($"Invalid publish IP address: {ip}");
      }
   }
}
