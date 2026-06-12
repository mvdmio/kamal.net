namespace Kamal.Cli;

/// <summary>
/// The Thor class options shared by every command (port of the <c>class_option</c>s in
/// <c>Kamal::Cli::Base</c>), plus the <c>--confirmed</c> flag used by destructive commands.
/// </summary>
public sealed class CliOptions
{
   public bool Verbose { get; set; }
   public bool Quiet { get; set; }

   /// <summary>Run commands against a specific app version.</summary>
   public string? Version { get; set; }

   public bool Primary { get; set; }
   public string? Hosts { get; set; }
   public string? Roles { get; set; }

   public string ConfigFile { get; set; } = "config/deploy.yml";
   public string? Destination { get; set; }

   public bool SkipHooks { get; set; }

   /// <summary>Proceed without confirmation question (<c>-y</c>).</summary>
   public bool Confirmed { get; set; }
}

/// <summary>
/// Per-invocation state shared by all CLI classes: the parsed global options and the original
/// command/subcommand names (Ruby's <c>first_invocation</c>, used by <c>modify</c> and hooks).
/// </summary>
public sealed class CliContext
{
   public CliContext(CliOptions options, string command, string? subcommand)
   {
      Options = options;
      Command = command;
      Subcommand = subcommand;
   }

   public CliOptions Options { get; }

   /// <summary>The top-level command name ("deploy", "app", "proxy", ...).</summary>
   public string Command { get; }

   /// <summary>The subcommand name when the command is a subcommand group ("boot", ...).</summary>
   public string? Subcommand { get; }
}
