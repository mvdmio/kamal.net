namespace Kamal.Secrets.Adapters;

/// <summary>
/// Base class for the secrets adapters. Port of <c>Kamal::Secrets::Adapters::Base</c>.
/// Adapters shell out to their password manager CLI; all shell execution is routed through
/// <see cref="Shell"/> so tests can substitute a fake (the equivalent of stubbing Ruby backticks).
/// </summary>
public abstract class AdapterBase
{
   /// <summary>
   /// Executes a shell command line and returns the result. Defaults to the real OS shell.
   /// Replace in tests to stub adapter CLI calls.
   /// </summary>
   public Func<string, ShellResult> Shell { get; set; } = ShellRunner.Run;

   public Dictionary<string, string> Fetch(IReadOnlyList<string> secrets, string? account = null, string? from = null)
   {
      if (RequiresAccount && string.IsNullOrWhiteSpace(account))
         throw new InvalidOperationException("Missing required option '--account'");

      CheckDependencies();

      var session = Login(account);
      return FetchSecrets(secrets, from, account, session);
   }

   public virtual bool RequiresAccount => true;

   protected abstract string? Login(string? account);

   protected abstract Dictionary<string, string> FetchSecrets(IReadOnlyList<string> secrets, string? from, string? account, string? session);

   protected abstract void CheckDependencies();

   protected ShellResult Run(string command)
   {
      return Shell(command);
   }

   protected static List<string> PrefixedSecrets(IEnumerable<string> secrets, string? from)
   {
      return secrets.Select(secret => from == null ? secret : $"{from}/{secret}").ToList();
   }

   /// <summary>Ruby's <c>presence</c>: returns null for null/blank strings.</summary>
   protected static string? Presence(string? value)
   {
      return string.IsNullOrWhiteSpace(value) ? null : value;
   }
}
