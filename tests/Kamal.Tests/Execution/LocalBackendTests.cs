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
}
