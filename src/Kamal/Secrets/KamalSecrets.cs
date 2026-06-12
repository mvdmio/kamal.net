using Kamal.Secrets.Dotenv;

namespace Kamal.Secrets;

/// <summary>
/// Port of <c>Kamal::Secrets</c>: lazily loads secrets from dotenv-format files.
/// Reads <c>&lt;secretsPath&gt;-common</c> first, then <c>&lt;secretsPath&gt;.&lt;destination&gt;</c>
/// (or <c>&lt;secretsPath&gt;</c> when no destination is given), with later files overriding earlier ones.
/// Secrets files support $(command) substitution, including the
/// <c>$(kamal secrets fetch/extract ...)</c> adapter pipeline.
/// </summary>
public class KamalSecrets
{
   private readonly string? _destination;
   private readonly string _secretsPath;
   private readonly object _mutex = new();

   private Dictionary<string, string>? _secrets;
   private IReadOnlyList<string>? _secretsFiles;

   public KamalSecrets(string? destination = null, string secretsPath = ".kamal/secrets")
   {
      _destination = destination;
      _secretsPath = secretsPath;
   }

   /// <summary>
   /// Fetches a secret, throwing <see cref="KeyNotFoundException"/> with the searched files
   /// in the message when the secret is missing (Ruby raises <c>Kamal::ConfigurationError</c>).
   /// </summary>
   public string this[string key]
   {
      get
      {
         if (TryFetch(key, out var value))
            return value;

         if (SecretsFiles.Count > 0)
            throw new KeyNotFoundException($"Secret '{key}' not found in {string.Join(", ", SecretsFiles)}");

         throw new KeyNotFoundException($"Secret '{key}' not found, no secret files ({string.Join(", ", SecretsFilenames)}) provided");
      }
   }

   /// <summary>The secrets filenames that actually exist, in load order.</summary>
   public IReadOnlyList<string> SecretsFiles => _secretsFiles ??= SecretsFilenames.Where(File.Exists).ToArray();

   /// <summary>
   /// Port of Ruby's <c>key?</c>: true when the secret exists and is present (non-blank).
   /// </summary>
   public bool ContainsKey(string key)
   {
      return TryFetch(key, out var value) && !string.IsNullOrWhiteSpace(value);
   }

   /// <summary>Returns all secrets, loading them on first use (Ruby's <c>to_h</c>).</summary>
   public IReadOnlyDictionary<string, string> ToDictionary()
   {
      lock (_mutex)
      {
         return LoadedSecrets();
      }
   }

   private bool TryFetch(string key, out string value)
   {
      // Fetching secrets may ask the user for input, so ensure only one thread does that.
      lock (_mutex)
      {
         return LoadedSecrets().TryGetValue(key, out value!);
      }
   }

   private Dictionary<string, string> LoadedSecrets()
   {
      if (_secrets != null)
         return _secrets;

      var secrets = new Dictionary<string, string>();

      foreach (var secretsFile in SecretsFiles)
      {
         foreach (var (key, value) in DotenvParser.ParseFile(secretsFile, overwrite: true))
            secrets[key] = value;
      }

      return _secrets = secrets;
   }

   private string[] SecretsFilenames =>
   [
      $"{_secretsPath}-common",
      $"{_secretsPath}{(_destination != null ? $".{_destination}" : "")}"
   ];
}
