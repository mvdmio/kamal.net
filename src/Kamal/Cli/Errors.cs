namespace Kamal.Cli;

/// <summary>Port of <c>Kamal::Cli::BootError</c>.</summary>
public sealed class BootError : Exception
{
   public BootError(string message) : base(message)
   {
   }
}

/// <summary>Port of <c>Kamal::Cli::HookError</c>.</summary>
public sealed class HookError : Exception
{
   public HookError(string message) : base(message)
   {
   }
}

/// <summary>Port of <c>Kamal::Cli::LockError</c>.</summary>
public sealed class LockError : Exception
{
   public LockError(string message) : base(message)
   {
   }
}

/// <summary>Port of <c>Kamal::Cli::Base::LockHeldError</c>: the remote lock directory already exists.</summary>
public sealed class LockHeldError : Exception;

/// <summary>Port of <c>Kamal::Cli::Base::LockMissingError</c>: no lock file on the primary host.</summary>
public sealed class LockMissingError : Exception;

/// <summary>Port of <c>Kamal::Cli::DependencyError</c>.</summary>
public sealed class DependencyError : Exception
{
   public DependencyError(string message) : base(message)
   {
   }
}

/// <summary>Port of <c>Kamal::Cli::Build::BuildError</c>.</summary>
public sealed class BuildError : Exception
{
   public BuildError(string message) : base(message)
   {
   }
}

/// <summary>
/// SSH credential / authentication failure (missing passphrase, unreadable configured keys,
/// explicit keys that do not load). Maps to <see cref="FailureClass.Auth"/> (exit 11).
/// </summary>
public class AuthError : Exception
{
   public AuthError(string message) : base(message)
   {
   }

   public AuthError(string message, Exception innerException) : base(message, innerException)
   {
   }
}

/// <summary>
/// Private key is encrypted and no passphrase is available (env, config, or interactive prompt).
/// Subtype of <see cref="AuthError"/> so default-identity loading can skip individual keys and
/// only fail closed when every candidate is encrypted-without-passphrase.
/// </summary>
public sealed class MissingPassphraseError : AuthError
{
   public MissingPassphraseError(string message) : base(message)
   {
   }
}
