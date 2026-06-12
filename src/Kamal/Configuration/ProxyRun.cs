using System.Net;
using System.Net.Sockets;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Proxy::Run</c>: options used when booting the proxy container.</summary>
public sealed class ProxyRun
{
   public const string MinimumVersion = "v0.9.2";
   public const int DefaultHttpPort = 80;
   public const int DefaultHttpsPort = 443;
   public const string DefaultLogMaxSize = "10m";

   private readonly KamalConfiguration _config;
   private readonly string _context;

   public ProxyRun(KamalConfiguration config, IDictionary<string, object?> runConfig, string context = "proxy/run")
   {
      _config = config;
      RunConfig = runConfig;
      _context = context;
   }

   public IDictionary<string, object?> RunConfig { get; }

   public object? Debug => RunConfig.Fetch("debug", null);

   public bool Publish => RunConfig.Fetch("publish", true) is not (null or false);

   public object HttpPort => RunConfig.Fetch("http_port", DefaultHttpPort)!;

   public object HttpsPort => RunConfig.Fetch("https_port", DefaultHttpsPort)!;

   public List<object?>? BindIps => RubyHelpers.AsList(RunConfig.Fetch("bind_ips", null));

   public string? PublishArgs
   {
      get
      {
         if (!Publish)
            return null;

         var args = (BindIps ?? [null]).Select(bindIp =>
         {
            var formattedIp = FormatBindIp(bindIp is null ? null : RubyHelpers.RubyToS(bindIp));
            var publishHttp = string.Join(":", new[] { formattedIp, RubyHelpers.RubyToS(HttpPort), DefaultHttpPort.ToString() }.Where(part => part is not null));
            var publishHttps = string.Join(":", new[] { formattedIp, RubyHelpers.RubyToS(HttpsPort), DefaultHttpsPort.ToString() }.Where(part => part is not null));

            return string.Join(" ", KamalUtils.Argumentize("--publish", new List<object?> { publishHttp, publishHttps }).Select(RubyHelpers.RubyToS));
         });

         return string.Join(" ", args);
      }
   }

   public string LogMaxSize => RubyHelpers.RubyToS(RunConfig.Fetch("log_max_size", DefaultLogMaxSize));

   public List<object>? LoggingArgs =>
      RubyHelpers.IsPresent(LogMaxSize) ? KamalUtils.Argumentize("--log-opt", $"max-size={LogMaxSize}") : null;

   public string Version => RubyHelpers.RubyToS(RunConfig.Fetch("version", MinimumVersion));

   public string? Registry => RunConfig.Fetch("registry", null) as string;

   public string Repository => RubyHelpers.RubyToS(RunConfig.Fetch("repository", "basecamp/kamal-proxy"));

   public string Image => $"{string.Join("/", new[] { Registry, Repository }.Where(part => part is not null))}:{Version}";

   public string ContainerName => "kamal-proxy";

   public List<object>? OptionsArgs =>
      RunConfig.Get("options") is IDictionary<string, object?> args ? KamalUtils.Optionize(args) : null;

   public string RunCommand =>
      string.Join(" ", new object[] { "kamal-proxy", "run" }.Concat(KamalUtils.Optionize(RunCommandOptions)).Select(RubyHelpers.RubyToS));

   public object? MetricsPort => RunConfig.Get("metrics_port");

   public OrderedDictionary<string, object?> RunCommandOptions
   {
      get
      {
         var options = new OrderedDictionary<string, object?>();

         if (Debug is not (null or false))
            options["debug"] = Debug;
         if (MetricsPort is not null)
            options["metrics-port"] = MetricsPort;

         return options;
      }
   }

   public List<object?> DockerOptionsArgs
   {
      get
      {
         var args = new List<object?>();
         args.AddRange(AppsVolumeArgs);

         if (PublishArgs is not null)
            args.Add(PublishArgs);
         if (LoggingArgs is not null)
            args.AddRange(LoggingArgs);
         if (RubyHelpers.IsPresent(MetricsPort))
            args.Add($"--expose={RubyHelpers.RubyToS(MetricsPort)}");
         if (OptionsArgs is not null)
            args.AddRange(OptionsArgs);

         return args.Where(arg => arg is not null).ToList();
      }
   }

   public string HostDirectory => RubyHelpers.JoinPath(_config.RunDirectory, "proxy");

   public string AppsDirectory => RubyHelpers.JoinPath(HostDirectory, "apps-config");

   public string AppsContainerDirectory => "/home/kamal-proxy/.apps-config";

   public Volume AppsVolume => new(hostPath: AppsDirectory, containerPath: AppsContainerDirectory);

   public List<object?> AppsVolumeArgs => [AppsVolume.DockerArgs];

   public string AppDirectory => RubyHelpers.JoinPath(AppsDirectory, _config.ServiceAndDestination);

   public string AppContainerDirectory => RubyHelpers.JoinPath(AppsContainerDirectory, _config.ServiceAndDestination);

   /// <summary>Ensures an IPv6 address is wrapped in square brackets, e.g. [::1].</summary>
   internal static string? FormatBindIp(string? ip)
   {
      if (ip is null)
         return null;

      if (IPAddress.TryParse(ip, out var parsed) && parsed.AddressFamily == AddressFamily.InterNetworkV6 && !(ip.StartsWith('[') && ip.EndsWith(']')))
         return $"[{ip}]";

      return ip;
   }
}
