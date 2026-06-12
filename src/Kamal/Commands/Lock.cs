using System.Text;
using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::Lock</c>.</summary>
public class Lock : CommandsBase
{
   public Lock(KamalConfiguration config) : base(config)
   {
   }

   public object[] Acquire(string message, string version)
   {
      return Combine(
         new object[] { "mkdir", LockDir },
         WriteLockDetails(message, version));
   }

   public object[] Release()
   {
      return Combine(
         new object[] { "rm", LockDetailsFile },
         new object[] { "rm", "-r", LockDir });
   }

   public object[] Status()
   {
      return Combine(
         StatLockDir(),
         ReadLockDetails());
   }

   // NOTE: Ruby also defines ensure_locks_directory, but it references an undefined locks_dir
   // (dead upstream code that would raise if called), so it is not ported.

   private object[] WriteLockDetails(string message, string version)
   {
      return Write(
         new object[] { "echo", $"\"{Base64Encode(LockDetails(message, version))}\"" },
         LockDetailsFile);
   }

   private object[] ReadLockDetails()
   {
      return Pipe(
         new object[] { "cat", LockDetailsFile },
         new object[] { "base64", "-d" });
   }

   private object[] StatLockDir()
   {
      return Write(
         new object[] { "stat", LockDir },
         "/dev/null");
   }

   private string LockDir
   {
      get
      {
         var dirName = string.Join("-", new[] { "lock", Config.Service, Config.Destination }.Where(part => part is not null));

         return RubyHelpers.JoinPath(Config.RunDirectory, dirName);
      }
   }

   private string LockDetailsFile => RubyHelpers.JoinPath(LockDir, "details");

   private string LockDetails(string message, string version)
   {
      return $"Locked by: {LockedBy} at {DateTime.UtcNow:yyyy-MM-dd'T'HH:mm:ss'Z'}\nVersion: {version}\nMessage: {message}";
   }

   private static string LockedBy
   {
      get
      {
         try
         {
            return Utils.Git.UserName;
         }
         catch
         {
            return "Unknown";
         }
      }
   }

   /// <summary>Ruby's <c>Base64.encode64</c>: line breaks every 60 characters plus a trailing newline.</summary>
   private static string Base64Encode(string value)
   {
      var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
      var result = new StringBuilder();

      for (var i = 0; i < encoded.Length; i += 60)
      {
         result.Append(encoded, i, Math.Min(60, encoded.Length - i));
         result.Append('\n');
      }

      return result.ToString();
   }
}
