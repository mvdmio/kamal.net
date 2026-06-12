using Kamal.Cli;
using Kamal.Execution;
using Kamal.Secrets.Dotenv;

// Port of bin/kamal: wire the in-process `$(kamal secrets ...)` substitution, run the CLI,
// and always disconnect the SSH connections on the way out.
InlineCommandSubstitution.KamalSecretsCommandHandler = SecretsCli.HandleInline;

try
{
   return await KamalCli.Start(args).ConfigureAwait(false);
}
finally
{
   SshBackend.DisconnectAll();
}
