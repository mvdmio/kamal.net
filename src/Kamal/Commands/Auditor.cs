using Kamal.Configuration;
using Kamal.Utils;

namespace Kamal.Commands;

/// <summary>Port of <c>Kamal::Commands::Auditor</c>.</summary>
public class Auditor : CommandsBase
{
   public Auditor(KamalConfiguration config, params KeyValuePair<string, object?>[] details) : base(config)
   {
      Details = details;
   }

   public IReadOnlyList<KeyValuePair<string, object?>> Details { get; }

   /// <summary>Runs remotely.</summary>
   public object[] Record(string line, params KeyValuePair<string, object?>[] details)
   {
      return Combine(
         MakeRunDirectory(),
         Append(
            new object[] { "echo", KamalUtils.EscapeShellValue(AuditLine(line, details)) },
            AuditLogFile));
   }

   public object[] Reveal() => ["tail", "-n", "50", AuditLogFile];

   private string AuditLogFile
   {
      get
      {
         var file = string.Join("-", new[] { Config.Service, Config.Destination, "audit.log" }.Where(part => part is not null));

         return RubyHelpers.JoinPath(Config.RunDirectory, file);
      }
   }

   private KamalTags AuditTags(params KeyValuePair<string, object?>[] details)
   {
      return Tags(Details.Concat(details).ToArray());
   }

   private object[] MakeRunDirectory() => ["mkdir", "-p", Config.RunDirectory];

   private string AuditLine(string line, params KeyValuePair<string, object?>[] details)
   {
      return $"{AuditTags(details).Except("version", "service_version", "service")} {line}";
   }
}
