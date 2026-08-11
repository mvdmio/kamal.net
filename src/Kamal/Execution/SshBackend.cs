using System.Net.Sockets;
using System.Text;
using Kamal.Configuration;
using Kamal.Secrets;
using Kamal.Utils;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Kamal.Execution;

/// <summary>
/// SSH backend on SSH.NET: one pooled connection per host honoring <c>Kamal.Configuration.Ssh</c>
/// (user, port, keys, key_data, jump proxy) and <c>Kamal.Configuration.Sshkit</c> (pool idle
/// timeout, max concurrent starts). Host key verification is permissive by default (accept any
/// host key); set <c>ssh.strict_host_key_checking: true</c> to verify against known_hosts.
/// A raw <c>proxy_command</c> is not supported and throws <see cref="NotSupportedException"/>.
/// </summary>
public sealed class SshBackend : BackendBase
{
   private static Ssh? _sshConfig;
   private static SemaphoreSlim _startSemaphore = new(30);

   public SshBackend(string host)
   {
      Host = host;
   }

   public override string Host { get; }

   /// <summary>
   /// Applies the deploy configuration (the equivalent of Commander's <c>configure_sshkit_with</c>).
   /// Must be called before connecting; the Commander does this on lazy config creation.
   /// </summary>
   public static void Configure(Ssh ssh, Sshkit sshkit)
   {
      _sshConfig = ssh;
      _startSemaphore = new SemaphoreSlim(sshkit.MaxConcurrentStarts);
      SshConnectionPool.IdleTimeout = TimeSpan.FromSeconds(sshkit.PoolIdleTimeout);
   }

   /// <summary>Closes all pooled SSH connections.</summary>
   public static void DisconnectAll() => SshConnectionPool.DisconnectAll();

   protected override async Task<RunResult> Run(
      string commandLine,
      string? input,
      IReadOnlyDictionary<string, string>? env,
      Action<string, string> onOutputLine,
      CancellationToken cancellationToken)
   {
      var connection = await SshConnectionPool.GetAsync(Host, ConnectAsync, cancellationToken).ConfigureAwait(false);
      var fullCommand = env is { Count: > 0 } ? WrapWithEnv(env, commandLine) : commandLine;

      using var command = connection.Client.CreateCommand(fullCommand);
      var executeTask = command.ExecuteAsync(cancellationToken);

      if (input is not null)
      {
         using var stdin = command.CreateInputStream();
         var bytes = Encoding.UTF8.GetBytes(input);
         await stdin.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
      }

      var stdout = new StringBuilder();
      var stderr = new StringBuilder();
      var stdoutTask = PumpAsync(command.OutputStream, "stdout", stdout, onOutputLine, cancellationToken);
      var stderrTask = PumpAsync(command.ExtendedOutputStream, "stderr", stderr, onOutputLine, cancellationToken);

      await executeTask.ConfigureAwait(false);
      await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);

      connection.Touch();

      var exitCode = command.ExitStatus ?? (command.ExitSignal is null ? 0 : 1);

      return new RunResult(exitCode, stdout.ToString(), stderr.ToString());
   }

   public override async Task Upload(string localPath, string remotePath, string? mode = null, bool recursive = false, CancellationToken cancellationToken = default)
   {
      // Parsed before anything is transferred, so a bad mode leaves nothing behind on the host.
      var uploadMode = UploadMode.ParseOptional(mode, remotePath);

      using var sftp = await CreateSftpClientAsync(cancellationToken).ConfigureAwait(false);

      if (recursive && Directory.Exists(localPath))
      {
         // scp -r semantics: the local directory is created as a child of the remote path.
         var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(localPath));
         await UploadDirectory(sftp, new DirectoryInfo(localPath), UnixJoin(remotePath, directoryName), uploadMode, cancellationToken).ConfigureAwait(false);
      }
      else
      {
         await using var file = File.OpenRead(localPath);
         await UploadStream(sftp, file, remotePath, uploadMode, cancellationToken).ConfigureAwait(false);
      }
   }

   public override async Task Upload(Stream local, string remotePath, string? mode = null, CancellationToken cancellationToken = default)
   {
      // Parsed before anything is transferred, so a bad mode leaves nothing behind on the host.
      var uploadMode = UploadMode.ParseOptional(mode, remotePath);

      using var sftp = await CreateSftpClientAsync(cancellationToken).ConfigureAwait(false);
      await UploadStream(sftp, local, remotePath, uploadMode, cancellationToken).ConfigureAwait(false);
   }

   private static Ssh ConfiguredSsh => _sshConfig
      ?? throw new InvalidOperationException("SshBackend has not been configured. Access the Commander config (or call SshBackend.Configure) first.");

   /// <summary>
   /// Session-open entry used by the pool. Applies <see cref="SshConnectRetry"/> around a single
   /// establish attempt (direct or jump-plus-target as one unit). The start semaphore is held only
   /// for each attempt, not across backoff waits, so other hosts are not blocked during delay.
   /// </summary>
   private static Task<PooledSshConnection> ConnectAsync(string host, CancellationToken cancellationToken) =>
      SshConnectRetry.RunAsync(host, ct => ConnectOnceAsync(host, ct), cancellationToken);

   private static async Task<PooledSshConnection> ConnectOnceAsync(string host, CancellationToken cancellationToken)
   {
      var ssh = ConfiguredSsh;

      // Capture the semaphore instance: Configure() may replace the static field
      // concurrently, and we must release the same instance we acquired.
      var startSemaphore = _startSemaphore;
      await startSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

      try
      {
         switch (ssh.Proxy)
         {
            case SshCommandProxy:
               throw new NotSupportedException(
                  "ssh.proxy_command is not supported by kamal.net. Configure ssh.proxy (a jump host) instead.");

            case SshJumpProxy jump:
               // Jump bastion + target open is one ConnectOnceAsync unit; SshConnectRetry wraps
               // this method only (not ConnectViaJump alone), so a glitch on either side retries
               // the whole open rather than nesting a second retry loop per hop.
               return await ConnectViaJump(host, jump, ssh, cancellationToken).ConfigureAwait(false);

            default:
            {
               var port = TargetPort(ssh);
               var client = NewClient(BuildConnectionInfo(host, port, ssh), host, port, ssh);
               await ConnectClientAsync(client, host, cancellationToken).ConfigureAwait(false);
               return new PooledSshConnection(client);
            }
         }
      }
      catch (ExecuteError)
      {
         throw;
      }
      catch (Exception exception) when (exception is not OperationCanceledException and not NotSupportedException)
      {
         throw WrapConnectFailure(host, exception);
      }
      finally
      {
         startSemaphore.Release();
      }
   }

   /// <summary>
   /// Connect vs auth seam: preserve SSH.NET exception types as inners so the CLI can map them
   /// to failure classes (auth=11, connect=10) without grepping free-form messages alone.
   /// </summary>
   private static async Task ConnectClientAsync(SshClient client, string host, CancellationToken cancellationToken)
   {
      try
      {
         await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (Exception exception) when (exception is not OperationCanceledException)
      {
         throw WrapConnectFailure(host, exception);
      }
   }

   internal static ExecuteError WrapConnectFailure(string host, Exception exception)
   {
      if (exception is SshAuthenticationException or SshPassPhraseNullOrEmptyException)
         return new ExecuteError(host, $"SSH authentication failed for {host}: {exception.Message}", innerException: exception);

      if (exception is SshConnectionException or SshOperationTimeoutException or SocketException or TimeoutException)
         return new ExecuteError(host, $"SSH connection failed for {host}: {exception.Message}", innerException: exception);

      // Other SSH.NET / transport failures during connect are treated as connect-class.
      if (exception is SshException or IOException)
         return new ExecuteError(host, $"SSH connection failed for {host}: {exception.Message}", innerException: exception);

      return new ExecuteError(host, $"SSH error for {host}: {exception.Message}", innerException: exception);
   }

   private static async Task<PooledSshConnection> ConnectViaJump(string host, SshJumpProxy jump, Ssh ssh, CancellationToken cancellationToken)
   {
      if (jump.JumpProxies.Contains(','))
         throw new NotSupportedException("Chained SSH jump hosts (comma-separated ssh.proxy) are not supported by kamal.net.");

      var (jumpUser, jumpHost, jumpPort) = ParseJumpSpec(jump.JumpProxies);
      var jumpClient = NewClient(BuildConnectionInfo(jumpHost, jumpPort, ssh, userOverride: jumpUser), jumpHost, jumpPort, ssh);

      try
      {
         await ConnectClientAsync(jumpClient, jumpHost, cancellationToken).ConfigureAwait(false);

         var targetPort = TargetPort(ssh);
         var forwardedPort = new ForwardedPortLocal("127.0.0.1", 0u, host, (uint)targetPort);
         jumpClient.AddForwardedPort(forwardedPort);
         forwardedPort.Start();

         // Logical host is the jump target for known_hosts; connection goes via localhost forward.
         var client = NewClient(
            BuildConnectionInfo("127.0.0.1", (int)forwardedPort.BoundPort, ssh),
            host,
            targetPort,
            ssh);
         await ConnectClientAsync(client, host, cancellationToken).ConfigureAwait(false);

         return new PooledSshConnection(client, jumpClient, forwardedPort);
      }
      catch
      {
         jumpClient.Dispose();
         throw;
      }
   }

   private static SshClient NewClient(ConnectionInfo connectionInfo, string host, int port, Ssh ssh)
   {
      var client = new SshClient(connectionInfo) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
      SshHostKeyPolicy.Apply(client, host, port, ssh);
      return client;
   }

   private static (string? User, string Host, int Port) ParseJumpSpec(string spec)
   {
      string? user = null;
      var rest = spec;
      var at = rest.LastIndexOf('@');

      if (at >= 0)
      {
         user = rest[..at];
         rest = rest[(at + 1)..];
      }

      var port = 22;
      var colon = rest.LastIndexOf(':');

      if (colon >= 0 && int.TryParse(rest[(colon + 1)..], out var parsedPort))
      {
         port = parsedPort;
         rest = rest[..colon];
      }

      return (user, rest, port);
   }

   private static int TargetPort(Ssh ssh) => Convert.ToInt32(RubyHelpers.RubyToS(ssh.Port));

   private static ConnectionInfo BuildConnectionInfo(string host, int port, Ssh ssh, string? userOverride = null)
      => SshCredentials.BuildConnectionInfo(host, port, ssh, userOverride);

   private static string WrapWithEnv(IReadOnlyDictionary<string, string> env, string commandLine)
   {
      // SSHKit-style: ( export K="v" K2="v2" ; command )
      var exports = string.Join(" ", env.Select(pair => $"{pair.Key}={Shellwords.Escape(pair.Value)}"));

      return $"( export {exports} ; {commandLine} )";
   }

   private async Task<SftpClient> CreateSftpClientAsync(CancellationToken cancellationToken)
   {
      var ssh = ConfiguredSsh;
      var connection = await SshConnectionPool.GetAsync(Host, ConnectAsync, cancellationToken).ConfigureAwait(false);

      var port = TargetPort(ssh);
      var connectionInfo = connection.ForwardedPort is { } forwardedPort
         ? BuildConnectionInfo("127.0.0.1", (int)forwardedPort.BoundPort, ssh)
         : BuildConnectionInfo(Host, port, ssh);

      var sftp = new SftpClient(connectionInfo);
      SshHostKeyPolicy.Apply(sftp, Host, port, ssh);
      await sftp.ConnectAsync(cancellationToken).ConfigureAwait(false);

      return sftp;
   }

   private static async Task UploadStream(SftpClient sftp, Stream local, string remotePath, UploadMode? mode, CancellationToken cancellationToken)
   {
      await sftp.UploadFileAsync(local, remotePath, cancellationToken).ConfigureAwait(false);

      ApplyMode(sftp, remotePath, mode);
   }

   private static async Task UploadDirectory(SftpClient sftp, DirectoryInfo local, string remotePath, UploadMode? mode, CancellationToken cancellationToken)
   {
      if (!await sftp.ExistsAsync(remotePath, cancellationToken).ConfigureAwait(false))
         await sftp.CreateDirectoryAsync(remotePath, cancellationToken).ConfigureAwait(false);

      foreach (var file in local.GetFiles())
      {
         await using var stream = file.OpenRead();
         await UploadStream(sftp, stream, UnixJoin(remotePath, file.Name), mode, cancellationToken).ConfigureAwait(false);
      }

      foreach (var directory in local.GetDirectories())
         await UploadDirectory(sftp, directory, UnixJoin(remotePath, directory.Name), mode, cancellationToken).ConfigureAwait(false);

      // Last, so a mode without the traverse bit cannot lock us out of the tree we are still filling.
      ApplyMode(sftp, remotePath, mode);
   }

   private static void ApplyMode(SftpClient sftp, string remotePath, UploadMode? mode)
   {
      if (mode is not null)
         sftp.ChangePermissions(remotePath, mode.PermissionDigits);
   }

   private static string UnixJoin(string left, string right) => $"{left.TrimEnd('/')}/{right}";

   private static async Task PumpAsync(Stream stream, string streamName, StringBuilder buffer, Action<string, string> onOutputLine, CancellationToken cancellationToken)
   {
      using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

      while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
      {
         buffer.Append(line).Append('\n');
         onOutputLine(streamName, line);
      }
   }
}
