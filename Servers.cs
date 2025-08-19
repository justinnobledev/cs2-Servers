using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Extensions;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;
using Servers.Config;
using Servers.Services;

namespace Servers;

public class Servers : BasePlugin, IPluginConfig<PluginConfig>
{
    public override string ModuleName => "Servers";
    public override string ModuleVersion => "1.0";
    public override string ModuleAuthor => "TICHOJEBEC";

    public PluginConfig Config { get; set; } = new();

    private ServerQuery _query = null!;
    private readonly HashSet<string> _registered = new(StringComparer.OrdinalIgnoreCase);

    private Timer? _advertTimer;
    private int _advertServerCounter = 0;

    public void OnConfigParsed(PluginConfig config)
    {
        if (config.QueryTimeoutMs < 200) config.QueryTimeoutMs = 200;
        if (config.QueryTimeoutMs > 5000) config.QueryTimeoutMs = 5000;
        if (config.CacheTtlSeconds < 0) config.CacheTtlSeconds = 0;
        if (config.CacheTtlSeconds > 30) config.CacheTtlSeconds = 30;
        if (config.AdvertisementTimeSecs < 0) config.AdvertisementTimeSecs = 0;

        if (string.IsNullOrWhiteSpace(config.ChatPrefix)) config.ChatPrefix = " {green}[Servers]{default}";
        if (config.CommandNames.Length == 0) config.CommandNames = new[] { "servers" };
        if (string.IsNullOrWhiteSpace(config.Language)) config.Language = "en";

        foreach (var ep in config.Servers)
        {
            if (string.IsNullOrWhiteSpace(ep.Name)) ep.Name = "Server";
            if (string.IsNullOrWhiteSpace(ep.Address)) throw new Exception($"Server '{ep.Name}' has empty Address.");
            if (ep.Port is < 1 or > 65535) throw new Exception($"Server '{ep.Name}' has invalid Port: {ep.Port}");
        }

        if (_advertTimer is not null)
        {
            _advertTimer.Kill();
            _advertTimer = AddTimer((float)config.AdvertisementTimeSecs, AdvertServer);
        }

        Config = config;
    }

    public override void Load(bool hotReload)
    {
        var langDir = Path.Combine(ModuleDirectory, "lang");
        _query = new ServerQuery(Config.QueryTimeoutMs, Config.CacheTtlSeconds);

        foreach (var name in Config.CommandNames)
            RegisterCommandOnce(name, "Shows the list of servers", OnCmdServers);
    }

    private void RegisterCommandOnce(string name, string help, CommandInfo.CommandCallback callback)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        if (_registered.Contains(name)) return;
        AddCommand(name, help, callback);
        _registered.Add(name);
    }

    private string Pref(string s) => $"{Config.ChatPrefix} {s}";

    private void OnCmdServers(CCSPlayerController? caller, CommandInfo info)
    {
        if (!ValidateCaller(caller)) return;
        var player = caller!;
        
        var eps = Config.Servers.ToArray();
        
        _ = Task.Run(async () =>
        {
            var tasks = eps.Select(async (ep, i) =>
            {
                var qr = await _query.QueryAsync(ep).ConfigureAwait(false);
                return new { Index = i + 1, Ep = ep, Q = qr };
            });
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            
            Server.NextFrame(() =>
            {
                if (player is not { IsValid: true }) return;

                player.PrintToChat(Pref(Localizer["Servers.Header"]).ReplaceColorTags());
                foreach (var r in results)
                {
                    if (r.Q.Ok)
                    {
                        player.PrintToChat(Pref(Localizer["Servers.Line.Online",
                            r.Index, r.Ep.Name, r.Q.Map, r.Q.Players, r.Q.MaxPlayers, r.Ep.Address, r.Ep.Port]
                        ).ReplaceColorTags());
                    }
                    else
                    {
                        player.PrintToChat(Pref(Localizer["Servers.Line.Offline", r.Index, r.Ep.Name, r.Ep.Address, r.Ep.Port]).ReplaceColorTags());
                    }
                }
            });
        });
    }

    private void AdvertServer()
    {
        var eps = Config.Servers.ToArray();

        _ = Task.Run(async () =>
        {
            var tasks = eps.Select(async (ep, i) =>
            {
                var qr = await _query.QueryAsync(ep).ConfigureAwait(false);
                return new { Index = i + 1, Ep = ep, Q = qr };
            });
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);

            Server.NextFrame(() =>
            {
                var validResults = results.Where(r => r.Q.Ok);
                if (_advertServerCounter > results.Length) _advertServerCounter = 0;
                if (results.Length == 0) return;

                var r = results[_advertServerCounter];
                Server.PrintToChatAll(Pref(Localizer["Servers.Line.Online",
                            r.Index, r.Ep.Name, r.Q.Map, r.Q.Players, r.Q.MaxPlayers, r.Ep.Address, r.Ep.Port]).ReplaceColorTags());

                _advertServerCounter++;
            });
        });
    }

    [ConsoleCommand("servers_reload_config", "Reloads the Servers plugin config")]
    [RequiresPermissions("@css/root")]
    public void OnReloadConfig(CCSPlayerController? player, CommandInfo cmd)
    {
        Config.Reload();
        cmd.ReplyToCommand(Localizer["Servers.Reload.Done"]);
    }

    [ConsoleCommand("servers_reset_config", "Resets the Servers plugin config to defaults (in-memory)")]
    [RequiresPermissions("@css/root")]
    public void OnResetConfig(CCSPlayerController? player, CommandInfo cmd)
    {
        Config.Update();
        cmd.ReplyToCommand(Localizer["Servers.Reset.Done"]);
    }
    
    public static bool ValidateCaller(CCSPlayerController? caller)
        => caller is { IsValid: true } && !caller.IsBot && !caller.IsHLTV;

    public static string Name(CCSPlayerController player)
        => string.IsNullOrWhiteSpace(player.PlayerName) ? "Unknown" : player.PlayerName;
}
