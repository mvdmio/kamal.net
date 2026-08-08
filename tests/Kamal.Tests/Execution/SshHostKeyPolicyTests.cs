using System.Text;
using Kamal.Execution;

namespace Kamal.Tests.Execution;

/// <summary>known_hosts matching and permissive-vs-strict host-key policy behaviour.</summary>
public class SshHostKeyPolicyTests
{
   // Public key blob for: ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIJ5W7XUl2wDUCG43wACtZo8xuMXEbt62k/tJXx7wQ1GO
   private static readonly byte[] SampleHostKey = Convert.FromBase64String(
      "AAAAC3NzaC1lZDI1NTE5AAAAIJ5W7XUl2wDUCG43wACtZo8xuMXEbt62k/tJXx7wQ1GO");

   private const string SampleKeyType = "ssh-ed25519";

   [Fact]
   public void ParsesPlainKnownHostsLine()
   {
      Assert.True(SshHostKeyPolicy.KnownHostsStore.TryParseLine(
         "example.com ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIJ5W7XUl2wDUCG43wACtZo8xuMXEbt62k/tJXx7wQ1GO plain",
         out var entry));
      Assert.Equal("example.com", entry.HostField);
      Assert.Equal(SampleKeyType, entry.KeyType);
      Assert.Equal(SampleHostKey, entry.KeyBytes);
      Assert.False(entry.Revoked);
   }

   [Fact]
   public void TrustsMatchingHostAndKeyFromFile()
   {
      using var dir = new TempDir();
      var path = dir.Write(
         "known_hosts",
         "example.com ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIJ5W7XUl2wDUCG43wACtZo8xuMXEbt62k/tJXx7wQ1GO\n");

      Assert.True(SshHostKeyPolicy.IsTrusted("example.com", 22, SampleKeyType, SampleHostKey, [path]));
   }

   [Fact]
   public void RejectsUnknownHostWhenStrictMaterialLoaded()
   {
      using var dir = new TempDir();
      var path = dir.Write(
         "known_hosts",
         "example.com ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIJ5W7XUl2wDUCG43wACtZo8xuMXEbt62k/tJXx7wQ1GO\n");

      Assert.False(SshHostKeyPolicy.IsTrusted("other.example", 22, SampleKeyType, SampleHostKey, [path]));
   }

   [Fact]
   public void RejectsKeyMismatchForKnownHost()
   {
      using var dir = new TempDir();
      var path = dir.Write(
         "known_hosts",
         "example.com ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIJ5W7XUl2wDUCG43wACtZo8xuMXEbt62k/tJXx7wQ1GO\n");
      var otherKey = Encoding.UTF8.GetBytes("not-a-real-host-key-blob!!!!");

      Assert.False(SshHostKeyPolicy.IsTrusted("example.com", 22, SampleKeyType, otherKey, [path]));
   }

   [Fact]
   public void MatchesBracketHostPortForm()
   {
      using var dir = new TempDir();
      var path = dir.Write(
         "known_hosts",
         "[example.com]:2222 ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIJ5W7XUl2wDUCG43wACtZo8xuMXEbt62k/tJXx7wQ1GO\n");

      Assert.True(SshHostKeyPolicy.IsTrusted("example.com", 2222, SampleKeyType, SampleHostKey, [path]));
      Assert.False(SshHostKeyPolicy.IsTrusted("example.com", 22, SampleKeyType, SampleHostKey, [path]));
   }

   [Fact]
   public void MissingKnownHostsFileMeansUntrusted()
   {
      Assert.False(SshHostKeyPolicy.IsTrusted(
         "example.com",
         22,
         SampleKeyType,
         SampleHostKey,
         ["/nonexistent/kamal-known-hosts-does-not-exist"]));
   }

   [Fact]
   public void RevokedMarkerDeniesTrust()
   {
      using var dir = new TempDir();
      var path = dir.Write(
         "known_hosts",
         "@revoked example.com ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIJ5W7XUl2wDUCG43wACtZo8xuMXEbt62k/tJXx7wQ1GO\n");

      Assert.False(SshHostKeyPolicy.IsTrusted("example.com", 22, SampleKeyType, SampleHostKey, [path]));
   }

   private sealed class TempDir : IDisposable
   {
      private readonly string _dir = Path.Combine(Path.GetTempPath(), "kamal-hostkey-" + Guid.NewGuid().ToString("N"));

      public TempDir() => Directory.CreateDirectory(_dir);

      public string Write(string name, string content)
      {
         var path = Path.Combine(_dir, name);
         File.WriteAllText(path, content);
         return path;
      }

      public void Dispose()
      {
         try
         {
            Directory.Delete(_dir, recursive: true);
         }
         catch (IOException)
         {
         }
      }
   }
}
