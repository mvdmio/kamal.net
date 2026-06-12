using System.Diagnostics;
using Kamal.Configuration;
using Kamal.Execution;
using Kamal.Output;
using Kamal.Utils;

namespace Kamal;

/// <summary>
/// Port of <c>Kamal::Commander</c>: the shared deploy state the CLI works against — lazy
/// configuration, host/role filtering, command builder accessors, verbosity and the audit
/// (modify) log broadcast.
/// </summary>
public sealed partial class Commander
{
   private KamalConfiguration? _config;
   private bool _hasConfigKwargs;
   private string? _configFile;
   private string? _destination;
   private string? _version;

   private Specifics? _specifics;
   private List<Role>? _specificRoles;
   private List<string>? _specificHosts;

   private int _modifyDepth;
   private BroadcastLogger? _outputLogger;
   private readonly Dictionary<string, object> _commands = new();

   public Commander()
   {
      Reset();
   }

   public Verbosity Verbosity { get; set; }

   public bool HoldingLock { get; set; }

   public bool Connected { get; set; }

   public bool Logging { get; set; }

   public List<Role>? SpecificRoles => _specificRoles;

   public List<string>? SpecificHosts => _specificHosts;

   /// <summary>Port of <c>reset</c>: returns the commander to its initial state (used between tests).</summary>
   public void Reset()
   {
      Verbosity = Verbosity.Info;
      HoldingLock = Environment.GetEnvironmentVariable("KAMAL_LOCK") == "true";
      Connected = false;
      Logging = false;
      _modifyDepth = 0;
      _specifics = null;
      _specificRoles = null;
      _specificHosts = null;
      _config = null;
      _hasConfigKwargs = false;
      _configFile = null;
      _destination = null;
      _version = null;
      _outputLogger = null;
      _commands.Clear();
   }

   /// <summary>Lazy configuration: created from the kwargs given to <see cref="Configure"/> on first access.</summary>
   public KamalConfiguration Config
   {
      get
      {
         if (_config is null)
         {
            _config = KamalConfiguration.CreateFrom(configFile: _configFile, destination: _destination, version: _version);
            _hasConfigKwargs = false;
            ConfigureSshkitWith(_config);
         }

         return _config;
      }
   }

   public void Configure(string? configFile = null, string? destination = null, string? version = null)
   {
      _config = null;
      _configFile = configFile;
      _destination = destination;
      _version = version;
      _hasConfigKwargs = true;
   }

   public bool Configured => _config is not null || _hasConfigKwargs;

   /// <summary>Port of <c>specific_primary!</c>: narrows the hosts to the primary host.</summary>
   public void SpecificPrimary()
   {
      _specifics = null;

      if (_specificRoles is { Count: > 0 })
         SetSpecificHosts([_specificRoles[0].PrimaryHost!]);
      else
         SetSpecificHosts([Config.PrimaryHost!]);
   }

   /// <summary>Port of <c>specific_roles=</c>: filters roles by names/wildcards; throws when nothing matches.</summary>
   public void SetSpecificRoles(IEnumerable<string>? roleNames)
   {
      _specifics = null;

      var names = roleNames?.ToList();

      if (names is { Count: > 0 })
      {
         var filteredNames = KamalUtils.FilterSpecificItems(names, Config.Roles.Select(role => role.Name));

         if (filteredNames.Count == 0)
            throw new ArgumentException($"No --roles match for {string.Join(",", names)}");

         _specificRoles = filteredNames.Select(name => Config.Roles.First(role => role.Name == name)).ToList();
      }
      else
      {
         _specificRoles = null;
      }
   }

   public void SetSpecificRoles(string roleName) => SetSpecificRoles([roleName]);

   /// <summary>Port of <c>specific_hosts=</c>: filters hosts by names/wildcards; throws when nothing matches.</summary>
   public void SetSpecificHosts(IEnumerable<string>? hosts)
   {
      _specifics = null;

      var hostList = hosts?.ToList();

      if (hostList is { Count: > 0 })
      {
         var filtered = KamalUtils.FilterSpecificItems(hostList, Config.AllHosts);

         if (filtered.Count == 0)
            throw new ArgumentException($"No --hosts match for {string.Join(",", hostList)}");

         _specificHosts = filtered;
      }
      else
      {
         _specificHosts = null;
      }
   }

   public void SetSpecificHosts(string host) => SetSpecificHosts([host]);

   /// <summary>Port of <c>with_specific_hosts</c>: temporarily narrows the hosts for the duration of the action.</summary>
   public void WithSpecificHosts(IEnumerable<string>? hosts, Action action)
   {
      var originalHosts = _specificHosts;
      SetSpecificHosts(hosts);

      try
      {
         action();
      }
      finally
      {
         _specifics = null;
         _specificHosts = originalHosts;
      }
   }

   public void WithSpecificHosts(string host, Action action) => WithSpecificHosts([host], action);

   public async Task WithSpecificHosts(IEnumerable<string>? hosts, Func<Task> action)
   {
      var originalHosts = _specificHosts;
      SetSpecificHosts(hosts);

      try
      {
         await action().ConfigureAwait(false);
      }
      finally
      {
         _specifics = null;
         _specificHosts = originalHosts;
      }
   }

   public List<string> AccessoryNames => Config.Accessories.Select(accessory => accessory.Name).ToList();

   public Commands.App App(Role? role = null, string? host = null) => new(Config, role, host);

   public Commands.Accessory Accessory(string name) => new(Config, name);

   public Commands.Auditor Auditor(params KeyValuePair<string, object?>[] details) => new(Config, details);

   public Commands.Builder Builder => Cached("builder", () => new Commands.Builder(Config));

   public Commands.Docker Docker => Cached("docker", () => new Commands.Docker(Config));

   public Commands.Hook Hook => Cached("hook", () => new Commands.Hook(Config));

   public Commands.Lock Lock => Cached("lock", () => new Commands.Lock(Config));

   public Commands.Proxy Proxy(string host) => new(Config, host);

   public Commands.Prune Prune => Cached("prune", () => new Commands.Prune(Config));

   public Commands.Registry Registry => Cached("registry", () => new Commands.Registry(Config));

   public Commands.Server Server => Cached("server", () => new Commands.Server(Config));

   /// <summary>Port of <c>alias(name)</c>.</summary>
   public Alias? Alias(string name) => Config.Aliases.TryGetValue(name, out var alias) ? alias : null;

   /// <summary>
   /// Port of <c>resolve_alias</c>: reads the alias command, from the loaded config when present,
   /// otherwise from the raw config file (so aliases resolve before full validation).
   /// </summary>
   public object? ResolveAlias(string name)
   {
      if (_config is not null)
         return _config.Aliases.TryGetValue(name, out var alias) ? alias.Command : null;

      var rawConfig = KamalConfiguration.LoadRawConfig(_configFile ?? RubyHelpers.JoinPath("config", "deploy.yml"), _destination);

      return RubyHelpers.AsDict(rawConfig.Get("aliases"))?.Get(name);
   }

   /// <summary>Port of <c>with_verbosity</c>: temporarily changes the global output verbosity.</summary>
   public void WithVerbosity(Verbosity level, Action action)
   {
      var oldLevel = Verbosity;

      Verbosity = level;
      KamalOutput.Verbosity = level;

      try
      {
         action();
      }
      finally
      {
         Verbosity = oldLevel;
         KamalOutput.Verbosity = oldLevel;
      }
   }

   public async Task WithVerbosity(Verbosity level, Func<Task> action)
   {
      var oldLevel = Verbosity;

      Verbosity = level;
      KamalOutput.Verbosity = level;

      try
      {
         await action().ConfigureAwait(false);
      }
      finally
      {
         Verbosity = oldLevel;
         KamalOutput.Verbosity = oldLevel;
      }
   }

   /// <summary>
   /// Port of <c>modify</c>: marks a modifying command, broadcasting start/finish (with runtime
   /// and any exception) to the configured output loggers, and closing them when the outermost
   /// modify finishes. (ActiveSupport::Notifications instrumentation is replaced by direct
   /// broadcast calls.)
   /// </summary>
   public async Task Modify(string command, string? subcommand, Func<Task> action)
   {
      Logging = true;

      var started = ++_modifyDepth == 1;
      var stopwatch = started ? Stopwatch.StartNew() : null;
      ModifyPayload? payload = null;
      Exception? exception = null;

      try
      {
         if (started)
         {
            payload = new ModifyPayload(command, subcommand, Config.Destination, Hosts);
            OutputLogger.Start(payload);
         }

         await action().ConfigureAwait(false);
      }
      catch (Exception e)
      {
         exception = e;
         throw;
      }
      finally
      {
         if (started && payload is not null)
            OutputLogger.Finish(payload, stopwatch!.Elapsed.TotalSeconds, exception);

         if (--_modifyDepth == 0)
            _outputLogger?.Close();
      }
   }

   public void Modify(string command, string? subcommand, Action action)
   {
      Modify(command, subcommand, () =>
      {
         action();
         return Task.CompletedTask;
      }).GetAwaiter().GetResult();
   }

   /// <summary>Port of <c>log(line)</c>: appends a line to the audit output loggers while logging is active.</summary>
   public void Log(string line)
   {
      if (Logging)
         OutputLogger.Append(line + "\n");
   }

   // Delegation to Specifics (Ruby's `delegate ... to: :specifics`).

   public List<string> Hosts => CurrentSpecifics.Hosts;

   public List<Role> Roles => CurrentSpecifics.Roles;

   public string? PrimaryHost => CurrentSpecifics.PrimaryHost;

   public Role? PrimaryRole => CurrentSpecifics.PrimaryRole;

   public List<Role> RolesOn(string host) => CurrentSpecifics.RolesOn(host);

   public List<string> AppHosts => CurrentSpecifics.AppHosts;

   public List<string> ProxyHosts => CurrentSpecifics.ProxyHosts;

   public List<string> AccessoryHosts => CurrentSpecifics.AccessoryHosts;

   private Specifics CurrentSpecifics => _specifics ??= new Specifics(Config, _specificHosts, _specificRoles);

   private BroadcastLogger OutputLogger => _outputLogger ??= new BroadcastLogger();

   private T Cached<T>(string key, Func<T> factory) where T : class
   {
      if (_commands.TryGetValue(key, out var existing))
         return (T)existing;

      var created = factory();
      _commands[key] = created;

      return created;
   }

   /// <summary>Lazy SSH/output setup once the configuration is created (Ruby's <c>configure_sshkit_with</c>).</summary>
   private void ConfigureSshkitWith(KamalConfiguration config)
   {
      SshBackend.Configure(config.Ssh, config.Sshkit);
      KamalOutput.Verbosity = Verbosity;

      ConfigureOutputWith(config);
   }

   private void ConfigureOutputWith(KamalConfiguration config)
   {
      if (!config.Output.Enabled)
         return;

      try
      {
         foreach (var descriptor in config.Output.Loggers)
         {
            switch (descriptor.Type)
            {
               case "file":
                  OutputLogger.BroadcastTo(new FileLogger(RubyHelpers.RubyToS(descriptor.Settings.Get("path"))));
                  break;

               case "otel":
                  Console.Error.WriteLine("OTel output logging is not supported by kamal.net; the otel output configuration will be ignored.");
                  break;
            }
         }

         KamalOutput.Logger = new ConsoleKamalLogger(Console.Out, line => OutputLogger.Append(line));
      }
      catch (Exception exception)
      {
         Console.Error.WriteLine($"Output logger setup failed: {exception.GetType().Name}: {exception.Message}");
      }
   }
}
