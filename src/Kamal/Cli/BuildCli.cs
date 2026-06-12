using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Kamal.Execution;
using Kamal.Output;
using Kamal.Utils;

namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::Build</c>.</summary>
public sealed partial class BuildCli : CliBase
{
   [GeneratedRegex("context not found|no builder|no compatible builder|does not exist")]
   private static partial Regex MissingBuilderRegex();

   [GeneratedRegex("context not found|no builder|does not exist")]
   private static partial Regex MissingBuilderOnRemoveRegex();

   public BuildCli(CliContext context) : base(context)
   {
   }

   /// <summary>Port of <c>deliver</c>.</summary>
   public async Task Deliver(bool noCache = false)
   {
      await Push(noCache: noCache).ConfigureAwait(false);
      await Pull().ConfigureAwait(false);
   }

   /// <summary>Port of <c>push</c>.</summary>
   public async Task Push(string output = "registry", bool noCache = false)
   {
      // Ensure pre-connect hooks run before the build, they may be needed for a remote builder
      // or the pre-build hooks.
      await PreConnectIfRequired().ConfigureAwait(false);

      await EnsureDockerInstalled().ConfigureAwait(false);

      if (KAMAL.Builder.LoginToRegistryLocally)
         await LoginToRegistryLocally().ConfigureAwait(false);

      await RunHook("pre-build").ConfigureAwait(false);

      var uncommittedChanges = Git.UncommittedChanges;

      if (KAMAL.Config.Builder.GitClone)
      {
         if (uncommittedChanges.Length > 0)
            Say($"Building from a local git clone, so ignoring these uncommitted changes:\n {uncommittedChanges}", Yellow);

         await RunLocally(backend => new BuildClone(backend).Prepare()).ConfigureAwait(false);
      }
      else if (uncommittedChanges.Length > 0)
      {
         Say($"Building with uncommitted changes:\n {uncommittedChanges}", Yellow);
      }

      await ForwardLocalRegistryPortForRemoteBuilder(() =>
         WithEnv(KAMAL.Config.Builder.Secrets, () =>
            RunLocally(async backend =>
            {
               try
               {
                  if (KAMAL.Builder.InspectBuilder() is { } inspect)
                     await backend.Execute(inspect).ConfigureAwait(false);
               }
               catch (ExecuteError e)
               {
                  if (MissingBuilderRegex().IsMatch(e.Message))
                  {
                     Console.Error.WriteLine("Missing compatible builder, so creating a new one first");

                     try
                     {
                        await Remove().ConfigureAwait(false);
                     }
                     catch (ExecuteError)
                     {
                        if (!MissingBuilderOnRemoveRegex().IsMatch(e.Message))
                           throw;
                     }

                     await Create().ConfigureAwait(false);
                  }
                  else
                  {
                     throw;
                  }
               }

               // Get the command here to ensure the chdir doesn't interfere with it
               var push = KAMAL.Builder.Push(output, noCache: noCache);
               var pushEnv = KAMAL.Builder.PushEnv;

               await KAMAL.WithVerbosity(Verbosity.Debug, async () =>
               {
                  var originalDirectory = Directory.GetCurrentDirectory();

                  try
                  {
                     Directory.SetCurrentDirectory(KAMAL.Config.Builder.BuildDirectory);
                     await backend.Execute(push, env: pushEnv.AsReadOnly()).ConfigureAwait(false);
                  }
                  finally
                  {
                     Directory.SetCurrentDirectory(originalDirectory);
                  }
               }).ConfigureAwait(false);
            }))).ConfigureAwait(false);
   }

   /// <summary>Port of <c>pull</c>.</summary>
   public async Task Pull()
   {
      if (!KAMAL.Registry.Local)
         await LoginToRegistryRemotely().ConfigureAwait(false);

      await ForwardLocalRegistryPort(KAMAL.Hosts, async () =>
      {
         var firstHosts = await MirrorHosts().ConfigureAwait(false);

         if (firstHosts.Count > 0)
         {
            // Pull on a single host per mirror first to seed them
            Say($"Pulling image on {string.Join(", ", firstHosts)} to seed the {(firstHosts.Count == 1 ? "mirror" : "mirrors")}...", Magenta);
            await PullOnHosts(firstHosts).ConfigureAwait(false);
            Say("Pulling image on remaining hosts...", Magenta);
            await PullOnHosts(KAMAL.AppHosts.Except(firstHosts).ToList()).ConfigureAwait(false);
         }
         else
         {
            await PullOnHosts(KAMAL.AppHosts).ConfigureAwait(false);
         }
      }).ConfigureAwait(false);
   }

   /// <summary>Port of <c>create</c>.</summary>
   public async Task Create()
   {
      if (KAMAL.Config.Builder.Remote is { } remoteHost)
         await ConnectToRemoteHost(remoteHost).ConfigureAwait(false);

      await RunLocally(async backend =>
      {
         try
         {
            KamalOutput.Logger.Log(Verbosity.Debug, $"Using builder: {KAMAL.Builder.Name}");

            if (KAMAL.Builder.Create() is { } create)
               await backend.Execute(create).ConfigureAwait(false);
         }
         catch (ExecuteError e)
         {
            if (!string.IsNullOrWhiteSpace(e.Stderr))
               KamalOutput.Logger.Log(Verbosity.Error, $"Couldn't create remote builder: {e.Stderr.Trim()}");
            else
               throw;
         }
      }).ConfigureAwait(false);
   }

   /// <summary>Port of <c>remove</c>.</summary>
   public Task Remove()
   {
      return RunLocally(async backend =>
      {
         KamalOutput.Logger.Log(Verbosity.Debug, $"Using builder: {KAMAL.Builder.Name}");

         if (KAMAL.Builder.Remove() is { } remove)
            await backend.Execute(remove).ConfigureAwait(false);
      });
   }

   /// <summary>Port of <c>details</c>.</summary>
   public Task Details()
   {
      return RunLocally(async backend =>
      {
         Console.WriteLine($"Builder: {KAMAL.Builder.Name}");
         Console.WriteLine(await backend.Capture(KAMAL.Builder.Info()).ConfigureAwait(false));
      });
   }

   /// <summary>Port of <c>dev</c>.</summary>
   public async Task Dev(string output = "docker", bool noCache = false)
   {
      await EnsureDockerInstalled().ConfigureAwait(false);

      var dockerIncludedFiles = DockerContext.IncludedFiles().ToHashSet(StringComparer.Ordinal);
      var gitUncommittedFiles = Git.UncommittedFiles.ToHashSet(StringComparer.Ordinal);
      var gitUntrackedFiles = Git.UntrackedFiles.ToHashSet(StringComparer.Ordinal);

      var dockerUncommittedFiles = dockerIncludedFiles.Intersect(gitUncommittedFiles).Order(StringComparer.Ordinal).ToList();

      if (dockerUncommittedFiles.Count > 0)
      {
         Say("WARNING: Files with uncommitted changes will be present in the dev container:", Yellow);
         foreach (var file in dockerUncommittedFiles)
            Say($"  {file}", Yellow);
         Say();
      }

      var dockerUntrackedFiles = dockerIncludedFiles.Intersect(gitUntrackedFiles).Order(StringComparer.Ordinal).ToList();

      if (dockerUntrackedFiles.Count > 0)
      {
         Say("WARNING: Untracked files will be present in the dev container:", Yellow);
         foreach (var file in dockerUntrackedFiles)
            Say($"  {file}", Yellow);
         Say();
      }

      await WithEnv(KAMAL.Config.Builder.Secrets, () =>
         RunLocally(backend =>
         {
            var build = KAMAL.Builder.Push(output, tagAsDirty: true, noCache: noCache);

            return KAMAL.WithVerbosity(Verbosity.Debug, () => backend.Execute(build));
         })).ConfigureAwait(false);
   }

   // ----- Private helpers --------------------------------------------------------------------

   private async Task ConnectToRemoteHost(string remoteHost)
   {
      if (!Uri.TryCreate(remoteHost, UriKind.Absolute, out var remoteUri) || remoteUri.Scheme != "ssh")
         return;

      // DEVIATION: Ruby connects with the user/port from the URI; the .NET SSH backend uses the
      // deploy-wide ssh settings, so this warms the connection by hostname only.
      await On(remoteUri.Host, backend => backend.Execute(["true"])).ConfigureAwait(false);
   }

   private async Task<List<string>> MirrorHosts()
   {
      if (KAMAL.AppHosts.Count <= 1)
         return [];

      var mirrorHosts = new ConcurrentDictionary<string, string>();

      await On(KAMAL.AppHosts, async backend =>
      {
         try
         {
            var firstMirror = (await backend.CaptureWithInfo(KAMAL.Builder.FirstMirror()).ConfigureAwait(false)).Trim();

            if (firstMirror.Length > 0)
               mirrorHosts.TryAdd(firstMirror, backend.Host);
         }
         catch (ExecuteError e) when (e.Message.Contains("error calling index: reflect: slice index out of range"))
         {
         }
      }).ConfigureAwait(false);

      return mirrorHosts.Values.ToList();
   }

   private Task PullOnHosts(IReadOnlyList<string> hosts)
   {
      return On(hosts, async backend =>
      {
         await backend.Execute(KAMAL.Auditor().Record($"Pulled image with version {KAMAL.Config.Version}"), verbosity: Verbosity.Debug).ConfigureAwait(false);
         await backend.Execute(KAMAL.Builder.Clean(), raiseOnNonZeroExit: false).ConfigureAwait(false);
         await backend.Execute(KAMAL.Builder.Pull()).ConfigureAwait(false);
         await backend.Execute(KAMAL.Builder.ValidateImage()).ConfigureAwait(false);
      });
   }

   private static Task LoginToRegistryLocally()
   {
      return RunLocally(async backend =>
      {
         if (KAMAL.Registry.Local)
            await backend.Execute(KAMAL.Registry.Setup()).ConfigureAwait(false);
         else if (KAMAL.Registry.Login() is { } login)
            await backend.Execute(login).ConfigureAwait(false);
      });
   }

   private Task LoginToRegistryRemotely()
   {
      return On(KAMAL.AppHosts, async backend =>
      {
         if (KAMAL.Registry.Login() is { } login)
            await backend.Execute(login).ConfigureAwait(false);
      });
   }

   private Task ForwardLocalRegistryPortForRemoteBuilder(Func<Task> action)
   {
      if (KAMAL.Config.Builder.IsRemote && KAMAL.Config.Builder.Remote is { } remote && Uri.TryCreate(remote, UriKind.Absolute, out var remoteUri))
      {
         var user = string.IsNullOrEmpty(remoteUri.UserInfo) ? null : remoteUri.UserInfo;
         var port = remoteUri.Port > 0 ? remoteUri.Port : (int?)null;

         return ForwardLocalRegistryPort([remoteUri.Host], action, userOverride: user, portOverride: port);
      }

      return action();
   }

   private async Task ForwardLocalRegistryPort(IReadOnlyList<string> hosts, Func<Task> action, string? userOverride = null, int? portOverride = null)
   {
      if (KAMAL.Config.Registry.Local)
      {
         Say($"Setting up local registry port forwarding to {string.Join(", ", hosts)}...");

         using (SshPortForwarding.Start(hosts, KAMAL.Config.Registry.LocalPort!.Value, KAMAL.Config.Ssh, userOverride, portOverride))
            await action().ConfigureAwait(false);
      }
      else
      {
         await action().ConfigureAwait(false);
      }
   }
}

/// <summary>Port of <c>Kamal::Cli::Build::Clone</c>.</summary>
public sealed class BuildClone
{
   private readonly IBackend _backend;

   public BuildClone(IBackend backend)
   {
      _backend = backend;
   }

   private static Commander KAMAL => KamalRuntime.Commander;

   public async Task Prepare()
   {
      try
      {
         try
         {
            await CloneRepo().ConfigureAwait(false);
         }
         catch (ExecuteError e)
         {
            if (e.Message.Contains("already exists and is not an empty directory"))
               await Reset().ConfigureAwait(false);
            else
               throw new BuildError($"Failed to clone repo: {e.Message}");
         }

         await Validate().ConfigureAwait(false);
      }
      catch (BuildError e)
      {
         Error($"Error preparing clone: {e.Message}, deleting and retrying...");

         try
         {
            Directory.Delete(KAMAL.Config.Builder.CloneDirectory, recursive: true);
         }
         catch (DirectoryNotFoundException)
         {
         }

         await CloneRepo().ConfigureAwait(false);
         await Validate().ConfigureAwait(false);
      }
   }

   private async Task CloneRepo()
   {
      Info($"Cloning repo into build directory `{KAMAL.Config.Builder.BuildDirectory}`...");

      Directory.CreateDirectory(KAMAL.Config.Builder.CloneDirectory);
      await _backend.Execute(KAMAL.Builder.Clone()).ConfigureAwait(false);
   }

   private async Task Reset()
   {
      Info($"Resetting local clone as `{KAMAL.Config.Builder.BuildDirectory}` already exists...");

      try
      {
         foreach (var step in KAMAL.Builder.CloneResetSteps())
            await _backend.Execute(step).ConfigureAwait(false);
      }
      catch (ExecuteError e)
      {
         throw new BuildError($"Failed to clone repo: {e.Message}");
      }
   }

   private async Task Validate()
   {
      try
      {
         var status = (await _backend.CaptureWithInfo(KAMAL.Builder.CloneStatus()).ConfigureAwait(false)).Trim();

         if (status.Length > 0)
            throw new BuildError($"Clone in {KAMAL.Config.Builder.BuildDirectory} is dirty, {status}");

         var revision = (await _backend.CaptureWithInfo(KAMAL.Builder.CloneRevision()).ConfigureAwait(false)).Trim();

         if (revision != Git.Revision)
            throw new BuildError($"Clone in {KAMAL.Config.Builder.BuildDirectory} is not on the correct revision, expected `{Git.Revision}` but got `{revision}`");
      }
      catch (ExecuteError e)
      {
         throw new BuildError($"Failed to validate clone: {e.Message}");
      }
   }

   private static void Info(string message) => KamalOutput.Logger.Log(Verbosity.Info, message);

   private static void Error(string message) => KamalOutput.Logger.Log(Verbosity.Error, message);
}

/// <summary>
/// Port of <c>Kamal::Docker</c>: lists the files included in the local docker build context
/// by building a check image that prints its files.
/// </summary>
public static class DockerContext
{
   public const string BuildCheckTag = "kamal-local-build-check";

   /// <summary>Test hook.</summary>
   public static Func<List<string>>? IncludedFilesOverride { get; set; }

   public static List<string> IncludedFiles()
   {
      if (IncludedFilesOverride is { } overrideFunc)
         return overrideFunc();

      var dockerfile = Path.Combine(Path.GetTempPath(), $"kamal-build-check-{Guid.NewGuid():N}.dockerfile");

      try
      {
         File.WriteAllText(dockerfile,
            """
            FROM busybox
            COPY . app
            WORKDIR app
            CMD find . -type f | sed "s|^\./||"
            """);

         var build = RunShell($"docker buildx build -t={BuildCheckTag} -f={EscapePath(dockerfile)} .");

         if (build.ExitCode != 0)
            throw new InvalidOperationException("failed to build check image");
      }
      finally
      {
         try
         {
            File.Delete(dockerfile);
         }
         catch (IOException)
         {
         }
      }

      var run = RunShell($"docker run --rm {BuildCheckTag}");

      if (run.ExitCode != 0)
         throw new InvalidOperationException($"failed to run check image:\n{run.Stderr}");

      return run.Stdout
         .Split('\n', StringSplitOptions.RemoveEmptyEntries)
         .Select(line => line.Trim())
         .Where(line => line.Length > 0)
         .ToList();
   }

   private static string EscapePath(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

   private static (int ExitCode, string Stdout, string Stderr) RunShell(string command)
   {
      var startInfo = new ProcessStartInfo
      {
         RedirectStandardOutput = true,
         RedirectStandardError = true,
         UseShellExecute = false,
         CreateNoWindow = true
      };

      if (OperatingSystem.IsWindows())
      {
         startInfo.FileName = "cmd.exe";
         startInfo.Arguments = $"/d /s /c \"{command}\"";
      }
      else
      {
         startInfo.FileName = "/bin/sh";
         startInfo.ArgumentList.Add("-c");
         startInfo.ArgumentList.Add(command);
      }

      using var process = Process.Start(startInfo)
         ?? throw new InvalidOperationException($"Failed to start process for command: {command}");

      var stdout = process.StandardOutput.ReadToEnd();
      var stderr = process.StandardError.ReadToEnd();
      process.WaitForExit();

      return (process.ExitCode, stdout, stderr);
   }
}
