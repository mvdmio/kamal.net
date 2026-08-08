using System.Security.Cryptography;
using System.Text;
using Kamal.Configuration;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Kamal.Execution;

/// <summary>
/// Host-key verification for SSH.NET clients. Default is permissive (accept any host key).
/// When <see cref="Ssh.StrictHostKeyChecking"/> is enabled, keys are checked against OpenSSH
/// <c>known_hosts</c> file(s).
/// </summary>
internal static class SshHostKeyPolicy
{
   /// <summary>Attaches host-key verification to an SSH or SFTP client for the logical host name.</summary>
   public static void Apply(BaseClient client, string host, int port, Ssh ssh)
   {
      if (!ssh.StrictHostKeyChecking)
         return;

      var paths = ssh.ResolvedKnownHostsPaths();
      var store = KnownHostsStore.Load(paths);

      client.HostKeyReceived += (_, e) =>
      {
         e.CanTrust = store.IsTrusted(host, port, e.HostKeyName, e.HostKey);
      };
   }

   /// <summary>Evaluates whether a host key is trusted under the given known_hosts material (test seam).</summary>
   public static bool IsTrusted(
      string host,
      int port,
      string hostKeyName,
      byte[] hostKey,
      IEnumerable<string> knownHostsPaths) =>
      KnownHostsStore.Load(knownHostsPaths).IsTrusted(host, port, hostKeyName, hostKey);

   /// <summary>Parses OpenSSH known_hosts lines into a trust store.</summary>
   internal sealed class KnownHostsStore
   {
      private readonly List<Entry> _entries;

      private KnownHostsStore(List<Entry> entries) => _entries = entries;

      public static KnownHostsStore Load(IEnumerable<string> paths)
      {
         var entries = new List<Entry>();

         foreach (var path in paths)
         {
            if (!File.Exists(path))
               continue;

            foreach (var rawLine in File.ReadLines(path))
            {
               if (TryParseLine(rawLine, out var entry))
                  entries.Add(entry);
            }
         }

         return new KnownHostsStore(entries);
      }

      /// <summary>Parse a single known_hosts line (public for unit tests).</summary>
      public static bool TryParseLine(string rawLine, out Entry entry)
      {
         entry = default!;
         var line = rawLine.Trim();
         if (line.Length == 0 || line.StartsWith('#'))
            return false;

         var revoked = false;
         if (line.StartsWith("@revoked", StringComparison.Ordinal))
         {
            revoked = true;
            line = line["@revoked".Length..].TrimStart();
         }
         else if (line.StartsWith('@'))
         {
            // @cert-authority and other markers are not used for plain host-key trust here.
            return false;
         }

         var parts = SplitKnownHostsFields(line);
         if (parts.Count < 3)
            return false;

         var hostField = parts[0];
         var keyType = parts[1];
         byte[] keyBytes;
         try
         {
            keyBytes = Convert.FromBase64String(parts[2]);
         }
         catch (FormatException)
         {
            return false;
         }

         entry = new Entry(hostField, keyType, keyBytes, revoked);
         return true;
      }

      public bool IsTrusted(string host, int port, string hostKeyName, byte[] hostKey)
      {
         var matched = false;
         var revoked = false;

         foreach (var entry in _entries)
         {
            if (!entry.MatchesHost(host, port))
               continue;

            if (!string.Equals(entry.KeyType, hostKeyName, StringComparison.Ordinal))
               continue;

            if (!hostKey.AsSpan().SequenceEqual(entry.KeyBytes))
               continue;

            if (entry.Revoked)
               revoked = true;
            else
               matched = true;
         }

         return matched && !revoked;
      }

      private static List<string> SplitKnownHostsFields(string line)
      {
         // known_hosts: hostnames keytype base64 [comment...]
         var parts = new List<string>(4);
         var start = 0;
         while (start < line.Length && parts.Count < 3)
         {
            while (start < line.Length && char.IsWhiteSpace(line[start]))
               start++;
            if (start >= line.Length)
               break;

            var end = start;
            while (end < line.Length && !char.IsWhiteSpace(line[end]))
               end++;

            parts.Add(line[start..end]);
            start = end;
         }

         return parts;
      }

      internal sealed class Entry
      {
         public Entry(string hostField, string keyType, byte[] keyBytes, bool revoked)
         {
            HostField = hostField;
            KeyType = keyType;
            KeyBytes = keyBytes;
            Revoked = revoked;
         }

         public string HostField { get; }
         public string KeyType { get; }
         public byte[] KeyBytes { get; }
         public bool Revoked { get; }

         public bool MatchesHost(string host, int port)
         {
            foreach (var pattern in HostField.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
               if (pattern.StartsWith("|1|", StringComparison.Ordinal))
               {
                  if (MatchesHashedHost(pattern, host) || (port != 22 && MatchesHashedHost(pattern, $"[{host}]:{port}")))
                     return true;
                  continue;
               }

               if (MatchesHostPattern(pattern, host, port))
                  return true;
            }

            return false;
         }

         private static bool MatchesHostPattern(string pattern, string host, int port)
         {
            // [host]:port form
            if (pattern.StartsWith('[') && pattern.Contains("]:", StringComparison.Ordinal))
            {
               var close = pattern.IndexOf(']');
               if (close > 1)
               {
                  var patternHost = pattern[1..close];
                  var portPart = pattern[(close + 1)..];
                  if (portPart.StartsWith(':') && int.TryParse(portPart[1..], out var patternPort))
                     return HostEquals(patternHost, host) && patternPort == port;
               }

               return false;
            }

            // Bare hostname matches default port 22, or any port when entry has no port qualifier.
            return HostEquals(pattern, host);
         }

         private static bool HostEquals(string a, string b) =>
            string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

         private static bool MatchesHashedHost(string pattern, string hostCandidate)
         {
            // |1|<base64 salt>|<base64 hmac-sha1>
            var parts = pattern.Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts[0] != "1")
               return false;

            try
            {
               var salt = Convert.FromBase64String(parts[1]);
               var expected = Convert.FromBase64String(parts[2]);
               var actual = HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(hostCandidate));
               return CryptographicOperations.FixedTimeEquals(expected, actual);
            }
            catch (FormatException)
            {
               return false;
            }
         }
      }
   }
}
