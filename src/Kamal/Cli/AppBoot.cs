using System.Security.Cryptography;
using System.Text;
using Kamal.Configuration;
using Kamal.Execution;
using Kamal.Output;
using Kamal.Utils;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::App::Boot</c>: boots one role on one host, coordinating via the barrier.</summary>
public sealed class AppBoot
{
   private readonly string _host;
   private readonly Role _role;
   private readonly IBackend _backend;
   private readonly string _version;
   private readonly HealthcheckBarrier? _barrier;
   private Commands.App? _app;

   public AppBoot(string host, Role role, IBackend backend, string version, HealthcheckBarrier? barrier)
   {
      _host = host;
      _role = role;
      _backend = backend;
      _version = version;
      _barrier = barrier;
   }

   private static Commander KAMAL => KamalRuntime.Commander;

   private Commands.App App => _app ??= KAMAL.App(role: _role, host: _host);

   private bool Gatekeeper => _barrier is not null && BarrierRole;

   private bool Queuer => _barrier is not null && !BarrierRole;

   private bool BarrierRole => _role == KAMAL.PrimaryRole;

   public async Task Run()
   {
      var oldVersion = await OldVersionRenamedIfClashing().ConfigureAwait(false);

      if (Queuer)
         await WaitAtBarrier().ConfigureAwait(false);

      try
      {
         await StartNewVersion().ConfigureAwait(false);
      }
      catch
      {
         if (Gatekeeper)
            await CloseBarrier().ConfigureAwait(false);

         await StopNewVersion().ConfigureAwait(false);
         throw;
      }

      if (Gatekeeper)
         ReleaseBarrier();

      if (oldVersion is not null)
         await StopOldVersion(oldVersion).ConfigureAwait(false);
   }

   private async Task<string?> OldVersionRenamedIfClashing()
   {
      var clashing = await _backend.CaptureWithInfo(App.ContainerIdForVersion(_version), raiseOnNonZeroExit: false).ConfigureAwait(false);

      if (!string.IsNullOrWhiteSpace(clashing))
      {
         var renamedVersion = $"{_version}_replaced_{RandomHex(8)}";
         Info($"Renaming container {_version} to {renamedVersion} as already deployed on {_host}");
         await Audit($"Renaming container {_version} to {renamedVersion}").ConfigureAwait(false);
         await _backend.Execute(App.RenameContainer(version: _version, newVersion: renamedVersion)).ConfigureAwait(false);
      }

      var oldVersion = (await _backend.CaptureWithInfo(App.CurrentRunningVersion(), raiseOnNonZeroExit: false).ConfigureAwait(false)).Trim();

      return string.IsNullOrWhiteSpace(oldVersion) ? null : oldVersion;
   }

   private async Task StartNewVersion()
   {
      try
      {
         await Audit($"Booted app version {_version}").ConfigureAwait(false);

         var truncatedHost = _host.Length > 51 ? _host[..51] : _host;

         if (truncatedHost.EndsWith('.'))
            truncatedHost = truncatedHost[..^1];

         var hostname = $"{truncatedHost}-{RandomHex(6)}";

         await _backend.Execute(App.EnsureEnvDirectory()).ConfigureAwait(false);

         using (var secrets = new MemoryStream(Encoding.UTF8.GetBytes(_role.SecretsIo(_host))))
            await _backend.Upload(secrets, _role.SecretsPath, mode: "0600").ConfigureAwait(false);

         await _backend.Execute(App.Run(hostname: hostname)).ConfigureAwait(false);

         if (_role.RunningProxy)
         {
            var endpoint = (await _backend.CaptureWithInfo(App.ContainerIdForVersion(_version)).ConfigureAwait(false)).Trim();

            if (endpoint.Length == 0)
               throw new BootError($"Failed to get endpoint for {_role} on {_host}, did the container boot?");

            await _backend.Execute(App.Deploy(target: endpoint)).ConfigureAwait(false);
         }
         else
         {
            await HealthcheckPoller.WaitForHealthy(() => _backend.CaptureWithInfo(App.Status(version: _version))).ConfigureAwait(false);
         }
      }
      catch
      {
         Error($"Failed to boot {_role} on {_host}");
         throw;
      }
   }

   private Task StopNewVersion()
   {
      return _backend.Execute(App.Stop(version: _version), raiseOnNonZeroExit: false);
   }

   private async Task StopOldVersion(string version)
   {
      await _backend.Execute(App.Stop(version: version), raiseOnNonZeroExit: false).ConfigureAwait(false);

      if (_role.Assets)
         await _backend.Execute(App.CleanUpAssets()).ConfigureAwait(false);

      if (KAMAL.Config.ErrorPagesPath is not null)
         await _backend.Execute(App.CleanUpErrorPages()).ConfigureAwait(false);
   }

   private void ReleaseBarrier()
   {
      if (_barrier!.Open())
         Info($"First {KAMAL.PrimaryRole} container is healthy on {_host}, booting any other roles");
   }

   private async Task WaitAtBarrier()
   {
      Info($"Waiting for the first healthy {KAMAL.PrimaryRole} container before booting {_role} on {_host}...");

      try
      {
         await _barrier!.Wait().ConfigureAwait(false);
      }
      catch (HealthcheckError)
      {
         Info($"First {KAMAL.PrimaryRole} container is unhealthy, not booting {_role} on {_host}");
         throw;
      }

      Info($"First {KAMAL.PrimaryRole} container is healthy, booting {_role} on {_host}...");
   }

   private async Task CloseBarrier()
   {
      if (_barrier!.Close())
      {
         Info($"First {KAMAL.PrimaryRole} container is unhealthy on {_host}, not booting any other roles");

         try
         {
            Error(await _backend.CaptureWithInfo(App.Logs(containerId: App.ContainerIdForVersion(_version))).ConfigureAwait(false));
            Error(await _backend.CaptureWithInfo(App.ContainerHealthLog(version: _version)).ConfigureAwait(false));
         }
         catch (ExecuteError)
         {
            Error($"Could not fetch logs for {_version}");
         }
      }
   }

   private async Task Audit(string message)
   {
      var auditor = KAMAL.Auditor(new KeyValuePair<string, object?>("role", _role.Name));
      await _backend.Execute(auditor.Record(message), verbosity: Verbosity.Debug).ConfigureAwait(false);
   }

   private void Info(string message) => KamalOutput.Logger.Log(Verbosity.Info, message, _host);

   private void Error(string message) => KamalOutput.Logger.Log(Verbosity.Error, message, _host);

   internal static string RandomHex(int bytes)
   {
      var buffer = new byte[bytes];
      RandomNumberGenerator.Fill(buffer);

      return Convert.ToHexStringLower(buffer);
   }
}

/// <summary>Port of <c>Kamal::Cli::App::Assets</c>.</summary>
public sealed class AppAssets
{
   private readonly string _host;
   private readonly Role _role;
   private readonly IBackend _backend;

   public AppAssets(string host, Role role, IBackend backend)
   {
      _host = host;
      _role = role;
      _backend = backend;
   }

   public async Task Run()
   {
      if (!_role.Assets)
         return;

      var app = KamalRuntime.Commander.App(role: _role, host: _host);

      await _backend.Execute(app.ExtractAssets()).ConfigureAwait(false);
      var oldVersion = (await _backend.CaptureWithInfo(app.CurrentRunningVersion(), raiseOnNonZeroExit: false).ConfigureAwait(false)).Trim();
      await _backend.Execute(app.SyncAssetVolumes(oldVersion: oldVersion)).ConfigureAwait(false);
   }
}

/// <summary>Port of <c>Kamal::Cli::App::ErrorPages</c>.</summary>
public sealed class AppErrorPages
{
   private static readonly string[] ErrorPagePatterns = ["4??.html", "5??.html"];

   private readonly IBackend _backend;

   public AppErrorPages(IBackend backend)
   {
      _backend = backend;
   }

   public async Task Run()
   {
      var kamal = KamalRuntime.Commander;

      if (kamal.Config.ErrorPagesPath is null)
         return;

      var tmpdir = Path.Combine(Path.GetTempPath(), $"kamal-error-pages-{AppBoot.RandomHex(6)}");

      try
      {
         var errorPagesDir = Path.Combine(tmpdir, kamal.Config.Version);
         Directory.CreateDirectory(errorPagesDir);

         var files = ErrorPagePatterns
            .SelectMany(pattern => Directory.Exists(kamal.Config.ErrorPagesPath) ? Directory.GetFiles(kamal.Config.ErrorPagesPath, pattern) : [])
            .ToList();

         if (files.Count > 0)
         {
            foreach (var file in files)
               File.Copy(file, Path.Combine(errorPagesDir, Path.GetFileName(file)));

            await _backend.Execute(kamal.App().CreateErrorPagesDirectory()).ConfigureAwait(false);
            await _backend.Upload(errorPagesDir, kamal.Config.ProxyBoot.ErrorPagesDirectory, mode: "0700", recursive: true).ConfigureAwait(false);
         }
      }
      finally
      {
         try
         {
            Directory.Delete(tmpdir, recursive: true);
         }
         catch (IOException)
         {
         }
      }
   }
}

/// <summary>Port of <c>Kamal::Cli::App::SslCertificates</c>.</summary>
public sealed class AppSslCertificates
{
   private readonly string _host;
   private readonly Role _role;
   private readonly IBackend _backend;

   public AppSslCertificates(string host, Role role, IBackend backend)
   {
      _host = host;
      _role = role;
      _backend = backend;
   }

   public async Task Run()
   {
      if (!(_role.RunningProxy && _role.Proxy!.CustomSslCertificate))
         return;

      var app = KamalRuntime.Commander.App(role: _role, host: _host);

      KamalOutput.Logger.Log(Verbosity.Info, $"Writing SSL certificates for {_role.Name} on {_host}", _host);
      await _backend.Execute(app.CreateSslDirectory()).ConfigureAwait(false);

      if (_role.Proxy.CertificatePemContent is { } certContent)
      {
         using var stream = new MemoryStream(Encoding.UTF8.GetBytes(certContent));
         await _backend.Upload(stream, _role.Proxy.HostTlsCert!, mode: "0644").ConfigureAwait(false);
      }

      if (_role.Proxy.PrivateKeyPemContent is { } keyContent)
      {
         using var stream = new MemoryStream(Encoding.UTF8.GetBytes(keyContent));
         await _backend.Upload(stream, _role.Proxy.HostTlsKey!, mode: "0644").ConfigureAwait(false);
      }
   }
}
