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
