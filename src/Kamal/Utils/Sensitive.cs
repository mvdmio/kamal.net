namespace Kamal.Utils;

/// <summary>
/// Implemented by values that must be redacted in logs and human-visible output
/// (Ruby's <c>SSHKit::Redaction</c> marker module).
/// </summary>
public interface IRedactable
{
   /// <summary>The replacement text shown instead of the real value.</summary>
   string Redaction { get; }
}

/// <summary>
/// Port of <c>Kamal::Utils::Sensitive</c>: wraps a value so that command formatting can use the
/// real value (<see cref="ToString"/> / <see cref="Unredacted"/>) while loggers and YAML output
/// show <see cref="Redaction"/> instead (Ruby's <c>inspect</c> / <c>encode_with</c>).
/// </summary>
public sealed class Sensitive : IRedactable
{
   public Sensitive(string value, string redaction = "[REDACTED]")
   {
      Unredacted = value;
      Redaction = redaction;
   }

   /// <summary>The real value (Ruby delegates <c>to_s</c> to it).</summary>
   public string Unredacted { get; }

   /// <summary>The redacted representation (Ruby delegates <c>inspect</c> to it).</summary>
   public string Redaction { get; }

   /// <summary>Ruby's <c>to_s</c>: yields the unredacted value for command building.</summary>
   public override string ToString() => Unredacted;

   /// <summary>Ruby's <c>inspect</c>: yields the redaction for human-visible output.</summary>
   public string Inspect => Redaction;
}
