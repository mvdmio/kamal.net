using Kamal.Configuration.Validation;
using Kamal.Utils;

namespace Kamal.Configuration;

/// <summary>
/// A configured output logger destination. The Ruby version instantiates
/// <c>Kamal::Output::OtelLogger</c> / <c>Kamal::Output::FileLogger</c>; the actual logger
/// implementations are outside the scope of the configuration port, so this carries the
/// validated type and settings for the output layer to consume.
/// </summary>
public sealed class OutputLogger
{
   public OutputLogger(string type, IDictionary<string, object?> settings)
   {
      Type = type;
      Settings = settings;
   }

   /// <summary>"otel" or "file".</summary>
   public string Type { get; }

   public IDictionary<string, object?> Settings { get; }
}

/// <summary>Port of <c>Kamal::Configuration::Output</c>.</summary>
public sealed class Output
{
   private static readonly string[] LoggerTypes = ["otel", "file"];

   private readonly IDictionary<string, object?> _outputConfig;

   public Output(KamalConfiguration config)
   {
      _outputConfig = RubyHelpers.AsDict(config.RawConfig.Get("output")) ?? new OrderedDictionary<string, object?>();

      if (_outputConfig.Count > 0)
         new Validator(config.RawConfig.Get("output"), ValidationDocs.Doc("output").Get("output"), "output").Validate();

      Loggers = BuildLoggers();
   }

   public IReadOnlyList<OutputLogger> Loggers { get; }

   public bool Enabled => RubyHelpers.IsPresent(_outputConfig);

   public IDictionary<string, object?> ToH() => _outputConfig;

   private List<OutputLogger> BuildLoggers()
   {
      var loggers = new List<OutputLogger>();

      foreach (var (key, settings) in _outputConfig)
      {
         if (!LoggerTypes.Contains(key))
            continue;

         var settingsDict = RubyHelpers.AsDict(settings) ?? new OrderedDictionary<string, object?>();

         // The logger constructors raise these in Ruby (otel_logger.rb / file_logger.rb).
         if (key == "otel" && settingsDict.Get("endpoint") is null)
            throw new ArgumentException("OTel endpoint is required");
         if (key == "file" && settingsDict.Get("path") is null)
            throw new ArgumentException("file path is required");

         loggers.Add(new OutputLogger(key, settingsDict));
      }

      return loggers;
   }
}
