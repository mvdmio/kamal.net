using Kamal.Execution;

namespace Kamal.Tests.Execution;

[Collection("kamal-config")]
public class LocalBackendTests
{
   private readonly LocalBackend _backend = new();

   [Fact]
   public void HostIsLocalhost()
   {
      Assert.Equal("localhost", _backend.Host);
   }

   [Fact]
   public async Task CaptureEchoRoundTrip()
   {
      Assert.Equal("hello", await _backend.Capture(["echo", "hello"]));
   }

   [Fact]
   public async Task TestReflectsExitCode()
   {
      Assert.True(await _backend.Test(["echo", "hi"]));
      Assert.False(await _backend.Test(["exit", "1"]));
   }

   [Fact]
   public async Task ExecuteRaisesOnNonZeroExit()
   {
      var error = await Assert.ThrowsAsync<ExecuteError>(() => _backend.Execute(["exit", "7"]));

      Assert.Equal("localhost", error.Host);
      Assert.Equal(7, error.ExitCode);
   }

   [Fact]
   public async Task ExecuteDoesNotRaiseWhenDisabled()
   {
      await _backend.Execute(["exit", "1"], raiseOnNonZeroExit: false);
   }

   [Fact]
   public async Task StderrIsCapturedInErrors()
   {
      object[] command = OperatingSystem.IsWindows()
         ? ["echo", "oops", "1>&2", "&", "exit", "3"]
         : ["echo", "oops", "1>&2", ";", "exit", "3"];

      var error = await Assert.ThrowsAsync<ExecuteError>(() => _backend.Execute(command));

      Assert.Equal(3, error.ExitCode);
      Assert.Contains("oops", error.Stderr);
   }

   [Fact]
   public async Task EnvIsInjectedIntoTheProcess()
   {
      object[] command = OperatingSystem.IsWindows()
         ? ["echo", "%KAMAL_TEST_HOOK_ENV%"]
         : ["echo", "$KAMAL_TEST_HOOK_ENV"];

      var output = await _backend.Capture(command, env: new Dictionary<string, string> { ["KAMAL_TEST_HOOK_ENV"] = "hook-value-42" });

      Assert.Equal("hook-value-42", output);
   }

   [Fact]
   public async Task InputIsFedToStdin()
   {
      var output = await _backend.Capture(["sort"], input: "banana\napple\n");

      Assert.Equal("apple\nbanana", output);
   }

   [Fact]
   public async Task UploadStreamWritesTheFile()
   {
      var path = Path.Combine(Path.GetTempPath(), "kamal-local-upload-" + Guid.NewGuid().ToString("N") + ".txt");

      try
      {
         using var content = new MemoryStream("proxy boot config"u8.ToArray());
         await _backend.Upload(content, path);

         Assert.Equal("proxy boot config", await File.ReadAllTextAsync(path));
      }
      finally
      {
         File.Delete(path);
      }
   }

   [Fact]
   public async Task UploadStreamAppliesTheMode()
   {
      if (OperatingSystem.IsWindows())
         return;

      var path = Path.Combine(Path.GetTempPath(), "kamal-local-upload-" + Guid.NewGuid().ToString("N") + ".env");

      try
      {
         using var content = new MemoryStream("SECRET=1"u8.ToArray());
         await _backend.Upload(content, path, mode: "0600");

         Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
      }
      finally
      {
         File.Delete(path);
      }
   }

   [Fact]
   public async Task RecursiveUploadAppliesTheModeToTheWholeTree()
   {
      if (OperatingSystem.IsWindows())
         return;

      var root = Path.Combine(Path.GetTempPath(), "kamal-local-recursive-" + Guid.NewGuid().ToString("N"));
      var source = Path.Combine(root, "pages");
      var destination = Path.Combine(root, "remote");
      var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

      try
      {
         Directory.CreateDirectory(Path.Combine(source, "nested"));
         await File.WriteAllTextAsync(Path.Combine(source, "503.html"), "gone");
         await File.WriteAllTextAsync(Path.Combine(source, "nested", "404.html"), "missing");

         await _backend.Upload(source, destination, mode: "0700", recursive: true);

         var uploaded = Path.Combine(destination, "pages");
         Assert.Equal(expected, File.GetUnixFileMode(uploaded));
         Assert.Equal(expected, File.GetUnixFileMode(Path.Combine(uploaded, "503.html")));
         Assert.Equal(expected, File.GetUnixFileMode(Path.Combine(uploaded, "nested")));
         Assert.Equal(expected, File.GetUnixFileMode(Path.Combine(uploaded, "nested", "404.html")));
      }
      finally
      {
         Directory.Delete(root, recursive: true);
      }
   }

   [Fact]
   public async Task AnInvalidModeFailsBeforeAnythingIsWritten()
   {
      var path = Path.Combine(Path.GetTempPath(), "kamal-local-upload-" + Guid.NewGuid().ToString("N") + ".env");

      try
      {
         using var content = new MemoryStream("SECRET=1"u8.ToArray());
         var error = await Assert.ThrowsAsync<FormatException>(() => _backend.Upload(content, path, mode: "0999"));

         Assert.Contains("0999", error.Message);
         Assert.Contains(path, error.Message);
         Assert.False(File.Exists(path));
      }
      finally
      {
         File.Delete(path);
      }
   }
}
