using System.Text;
using Kamal.Configuration;
using Kamal.Secrets;
using Kamal.Utils;
using Renci.SshNet;

namespace Kamal.Execution;

/// <summary>
/// SSH backend on SSH.NET: one pooled connection per host honoring <c>Kamal.Configuration.Ssh</c>
/// (user, port, keys, key_data, jump proxy) and <c>Kamal.Configuration.Sshkit</c> (pool idle
/// timeout, max concurrent starts). Host key verification is relaxed (SSH.NET accepts unknown
/// host keys by default), matching Kamal's permissive known-hosts behavior.
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
      using var sftp = await CreateSftpClientAsync(cancellationToken).ConfigureAwait(false);

      if (recursive && Directory.Exists(localPath))
      {
         // scp -r semantics: the local directory is created as a child of the remote path.
         var directoryName = Path.GetFileName(Path.TrimEndingDirectorySeparator(localPath));
         await UploadDirectory(sftp, new DirectoryInfo(localPath), UnixJoin(remotePath, directoryName), mode, cancellationToken).ConfigureAwait(false);
      }
      else
      {
         await using var file = File.OpenRead(localPath);
         await UploadStream(sftp, file, remotePath, mode, cancellationToken).ConfigureAwait(false);
      }
   }

   public override async Task Upload(Stream local, string remotePath, string? mode = null, CancellationToken cancellationToken = default)
   {
      using var sftp = await CreateSftpClientAsync(cancellationToken).ConfigureAwait(false);
      await UploadStream(sftp, local, remotePath, mode, cancellationToken).ConfigureAwait(false);
   }

   private static Ssh ConfiguredSsh => _sshConfig
      ?? throw new InvalidOperationException("SshBackend has not been configured. Access the Commander config (or call SshBackend.Configure) first.");

   private static async Task<PooledSshConnection> ConnectAsync(string host, CancellationToken cancellationToken)
   {
      var ssh = ConfiguredSsh;

      await _startSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

      try
      {
         switch (ssh.Proxy)
         {
            case SshCommandProxy:
               throw new NotSupportedException(
                  "ssh.proxy_command is not supported by kamal.net. Configure ssh.proxy (a jump host) instead.");

            case SshJumpProxy jump:
               return await ConnectViaJump(host, jump, ssh, cancellationToken).ConfigureAwait(false);

            default:
               var client = NewClient(BuildConnectionInfo(host, TargetPort(ssh), ssh));
               await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
               return new PooledSshConnection(client);
         }
      }
      finally
      {
         _startSemaphore.Release();
      }
   }

   private static async Task<PooledSshConnection> ConnectViaJump(string host, SshJumpProxy jump, Ssh ssh, CancellationToken cancellationToken)
   {
      if (jump.JumpProxies.Contains(','))
         throw new NotSupportedException("Chained SSH jump hosts (comma-separated ssh.proxy) are not supported by kamal.net.");

      var (jumpUser, jumpHost, jumpPort) = ParseJumpSpec(jump.JumpProxies);
      var jumpClient = NewClient(BuildConnectionInfo(jumpHost, jumpPort, ssh, userOverride: jumpUser));

      try
      {
         await jumpClient.ConnectAsync(cancellationToken).ConfigureAwait(false);

         var forwardedPort = new ForwardedPortLocal("127.0.0.1", 0u, host, (uint)TargetPort(ssh));
         jumpClient.AddForwardedPort(forwardedPort);
         forwardedPort.Start();

         var client = NewClient(BuildConnectionInfo("127.0.0.1", (int)forwardedPort.BoundPort, ssh));
         await client.ConnectAsync(cancellationToken).ConfigureAwait(false);

         return new PooledSshConnection(client, jumpClient, forwardedPort);
      }
      catch
      {
         jumpClient.Dispose();
         throw;
      }
   }

   private static SshClient NewClient(ConnectionInfo connectionInfo)
   {
      return new SshClient(connectionInfo) { KeepAliveInterval = TimeSpan.FromSeconds(30) };
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
   {
      var user = userOverride ?? ssh.User;
      var keyFiles = LoadKeyFiles(ssh);
      var methods = new List<AuthenticationMethod>();

      if (keyFiles.Count > 0)
         methods.Add(new PrivateKeyAuthenticationMethod(user, keyFiles.Cast<IPrivateKeySource>().ToArray()));

      methods.Add(new NoneAuthenticationMethod(user));

      return new ConnectionInfo(host, port, user, methods.ToArray())
      {
         Timeout = TimeSpan.FromSeconds(30)
      };
   }

   private static List<PrivateKeyFile> LoadKeyFiles(Ssh ssh)
   {
      var keyFiles = new List<PrivateKeyFile>();

      foreach (var key in RubyHelpers.AsList(ssh.Keys) ?? [])
      {
         var path = ExpandHome(RubyHelpers.RubyToS(key));

         if (File.Exists(path))
            keyFiles.Add(new PrivateKeyFile(path));
      }

      foreach (var keyData in ssh.KeyData ?? [])
         keyFiles.Add(new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(keyData))));

      if (keyFiles.Count > 0)
         return keyFiles;

      // No keys configured: fall back to the default identity files, like OpenSSH/net-ssh.
      var sshDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");

      foreach (var name in (string[])["id_ed25519", "id_ecdsa", "id_rsa", "id_dsa"])
      {
         var path = Path.Combine(sshDir, name);

         if (File.Exists(path))
         {
            try
            {
               keyFiles.Add(new PrivateKeyFile(path));
            }
            catch (Exception)
            {
               // Skip unreadable/passphrase-protected default keys.
            }
         }
      }

      return keyFiles;
   }

   private static string ExpandHome(string path)
   {
      if (path.StartsWith("~/", StringComparison.Ordinal) || path == "~")
         return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~', '/', '\\'));

      return path;
   }

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

      var connectionInfo = connection.ForwardedPort is { } forwardedPort
         ? BuildConnectionInfo("127.0.0.1", (int)forwardedPort.BoundPort, ssh)
         : BuildConnectionInfo(Host, TargetPort(ssh), ssh);

      var sftp = new SftpClient(connectionInfo);
      await sftp.ConnectAsync(cancellationToken).ConfigureAwait(false);

      return sftp;
   }

   private static async Task UploadStream(SftpClient sftp, Stream local, string remotePath, string? mode, CancellationToken cancellationToken)
   {
      await sftp.UploadFileAsync(local, remotePath, cancellationToken).ConfigureAwait(false);

      if (mode is not null)
         sftp.ChangePermissions(remotePath, Convert.ToInt16(mode, 8));
   }

   private static async Task UploadDirectory(SftpClient sftp, DirectoryInfo local, string remotePath, string? mode, CancellationToken cancellationToken)
   {
      if (!await sftp.ExistsAsync(remotePath, cancellationToken).ConfigureAwait(false))
         await sftp.CreateDirectoryAsync(remotePath, cancellationToken).ConfigureAwait(false);

      if (mode is not null)
         sftp.ChangePermissions(remotePath, Convert.ToInt16(mode, 8));

      foreach (var file in local.GetFiles())
      {
         await using var stream = file.OpenRead();
         await UploadStream(sftp, stream, UnixJoin(remotePath, file.Name), mode, cancellationToken).ConfigureAwait(false);
      }

      foreach (var directory in local.GetDirectories())
         await UploadDirectory(sftp, directory, UnixJoin(remotePath, directory.Name), mode, cancellationToken).ConfigureAwait(false);
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
