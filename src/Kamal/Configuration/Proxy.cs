using Kamal.Configuration.Validation;
using Kamal.Secrets;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>Port of <c>Kamal::Configuration::Proxy</c>: app-specific kamal-proxy deploy options.</summary>
public sealed class Proxy
{
   public static readonly string[] DefaultLogRequestHeaders = ["Cache-Control", "Last-Modified", "User-Agent"];
   public const string ContainerName = "kamal-proxy";

   private readonly KamalConfiguration _config;

   public Proxy(KamalConfiguration config, object? proxyConfig, KamalSecrets secrets, string? roleName = null, string context = "proxy")
   {
      _config = config;
      RoleName = roleName;
      Secrets = secrets;

      proxyConfig ??= new OrderedDictionary<string, object?>();

      new ProxyValidator(proxyConfig, ValidationDocs.Doc("proxy").Get("proxy"), context).Validate();

      ProxyConfig = RubyHelpers.AsDict(proxyConfig) ?? new OrderedDictionary<string, object?>();

      if (RubyHelpers.IsPresent(ProxyConfig.Get("run")))
         Run = new ProxyRun(config, RubyHelpers.AsDict(ProxyConfig.Get("run"))!, $"{context}/run");
   }

   public IDictionary<string, object?> ProxyConfig { get; }
   public string? RoleName { get; }
   public KamalSecrets Secrets { get; }
   public ProxyRun? Run { get; }

   public object AppPort => ProxyConfig.Fetch("app_port", 80)!;

   // Ruby truthiness: anything but nil/false counts (including an empty hash).
   public bool Ssl => ProxyConfig.Fetch("ssl", false) is not (null or false);

   public List<string> Hosts
   {
      get
      {
         if (RubyHelpers.AsList(ProxyConfig.Get("hosts")) is { } hosts)
            return hosts.Select(RubyHelpers.RubyToS).ToList();

         if (ProxyConfig.Get("host") is string host)
            return host.Split(',').ToList();

         return [];
      }
   }

   public bool CustomSslCertificate
   {
      get
      {
         if (ProxyConfig.Get("ssl") is not IDictionary<string, object?> ssl)
            return false;

         return RubyHelpers.IsPresent(ssl.Get("certificate_pem")) && RubyHelpers.IsPresent(ssl.Get("private_key_pem"));
      }
   }

   public string? CertificatePemContent =>
      ProxyConfig.Get("ssl") is IDictionary<string, object?> ssl ? Secrets[RubyHelpers.RubyToS(ssl.Get("certificate_pem"))] : null;

   public string? PrivateKeyPemContent =>
      ProxyConfig.Get("ssl") is IDictionary<string, object?> ssl ? Secrets[RubyHelpers.RubyToS(ssl.Get("private_key_pem"))] : null;

   public string? HostTlsCert => TlsPath(_config.ProxyBoot.TlsDirectory, "cert.pem");

   public string? HostTlsKey => TlsPath(_config.ProxyBoot.TlsDirectory, "key.pem");

   public string? ContainerTlsCert => TlsPath(_config.ProxyBoot.TlsContainerDirectory, "cert.pem");

   public string? ContainerTlsKey => CustomSslCertificate ? TlsPath(_config.ProxyBoot.TlsContainerDirectory, "key.pem") : null;

   public List<string> PathPrefixes
   {
      get
      {
         if (RubyHelpers.AsList(ProxyConfig.Get("path_prefixes")) is { } prefixes)
            return prefixes.Select(RubyHelpers.RubyToS).ToList();

         if (ProxyConfig.Get("path_prefix") is string prefix)
            return prefix.Split(',').ToList();

         return [];
      }
   }

   public OrderedDictionary<string, object?> DeployOptions
   {
      get
      {
         var buffering = RubyHelpers.AsDict(ProxyConfig.Get("buffering"));

         var options = new OrderedDictionary<string, object?>
         {
            ["host"] = Hosts,
            ["tls"] = Ssl ? true : null,
            ["tls-certificate-path"] = ContainerTlsCert,
            ["tls-private-key-path"] = ContainerTlsKey,
            ["deploy-timeout"] = SecondsDuration(_config.DeployTimeout),
            ["drain-timeout"] = SecondsDuration(_config.DrainTimeout),
            ["health-check-interval"] = SecondsDuration(ProxyConfig.Dig("healthcheck", "interval")),
            ["health-check-timeout"] = SecondsDuration(ProxyConfig.Dig("healthcheck", "timeout")),
            ["health-check-path"] = ProxyConfig.Dig("healthcheck", "path"),
            ["target-timeout"] = SecondsDuration(ProxyConfig.Get("response_timeout")),
            ["buffer-requests"] = buffering is null ? true : buffering.Fetch("requests", true),
            ["buffer-responses"] = buffering is null ? true : buffering.Fetch("responses", true),
            ["buffer-memory"] = ProxyConfig.Dig("buffering", "memory"),
            ["max-request-body"] = ProxyConfig.Dig("buffering", "max_request_body"),
            ["max-response-body"] = ProxyConfig.Dig("buffering", "max_response_body"),
            ["path-prefix"] = PathPrefixes,
            ["strip-path-prefix"] = ProxyConfig.Get("strip_path_prefix"),
            ["forward-headers"] = ProxyConfig.Get("forward_headers"),
            ["tls-redirect"] = ProxyConfig.Get("ssl_redirect"),
            ["log-request-header"] = ProxyConfig.Dig("logging", "request_headers") ?? DefaultLogRequestHeaders.Cast<object?>().ToList(),
            ["log-response-header"] = ProxyConfig.Dig("logging", "response_headers"),
            ["error-pages"] = ErrorPages
         };

         return Compact(options);
      }
   }

   public List<object> DeployCommandArgs(string target)
   {
      var args = new OrderedDictionary<string, object?> { ["target"] = $"{target}:{RubyHelpers.RubyToS(AppPort)}" };
      foreach (var (key, value) in DeployOptions)
         args[key] = value;

      return KamalUtils.Optionize(args, with: "=");
   }

   public OrderedDictionary<string, object?> StopOptions(object? drainTimeout = null, string? message = null)
   {
      return Compact(new OrderedDictionary<string, object?>
      {
         ["drain-timeout"] = SecondsDuration(drainTimeout),
         ["message"] = message
      });
   }

   public List<object> StopCommandArgs(object? drainTimeout = null, string? message = null)
   {
      return KamalUtils.Optionize(StopOptions(drainTimeout, message), with: "=");
   }

   /// <summary>Port of <c>merge</c>: the other proxy's config deep-merged with this one overriding.</summary>
   public Proxy Merge(Proxy other)
   {
      return new Proxy(_config, RubyHelpers.DeepMerge(other.ProxyConfig, ProxyConfig), Secrets, RoleName);
   }

   private string? TlsPath(string directory, string filename)
   {
      return CustomSslCertificate ? RubyHelpers.JoinPath(directory, RoleName, filename) : null;
   }

   private static string? SecondsDuration(object? value)
   {
      return RubyHelpers.IsPresent(value) || value is 0 ? $"{RubyHelpers.RubyToS(value)}s" : null;
   }

   private string? ErrorPages
   {
      get
      {
         if (RubyHelpers.IsPresent(_config.ErrorPagesPath))
            return RubyHelpers.JoinPath(_config.ProxyBoot.ErrorPagesContainerDirectory, _config.Version);

         return null;
      }
   }

   private static OrderedDictionary<string, object?> Compact(OrderedDictionary<string, object?> dict)
   {
      var result = new OrderedDictionary<string, object?>();
      foreach (var (key, value) in dict)
      {
         if (value is not null)
            result[key] = value;
      }

      return result;
   }
}
