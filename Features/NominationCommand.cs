﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Menu;
using cs2_rockthevote.Core;

namespace cs2_rockthevote;

public partial class Plugin {
  [ConsoleCommand("nominate", "nominate a map to rtv")]
  public void OnNominate(CCSPlayerController? player, CommandInfo command) {
    var map = command.GetArg(1).Trim().ToLower();
    _nominationManager.CommandHandler(player!, map);
  }

  [GameEventHandler(HookMode.Pre)]
  public HookResult EventPlayerDisconnectNominate(EventPlayerDisconnect @event,
    GameEventInfo eventInfo) {
    var player = @event.Userid;
    _nominationManager.PlayerDisconnected(player);
    return HookResult.Continue;
  }
}

public class NominationCommand : IPluginDependency<Plugin, Config> {
  private RtvConfig _config = new();
  private readonly GameRules _gamerules;
  private readonly StringLocalizer _localizer;
  private readonly MapCooldown _mapCooldown;
  private readonly MapLister _mapLister;
  private readonly PluginState _pluginState;
  private ChatMenu? nominationMenu;
  private readonly Dictionary<int, List<string>> Nominations = new();
  private readonly Dictionary<ulong, DateTime> LastNomination = new();

  public NominationCommand(MapLister mapLister, GameRules gamerules,
    StringLocalizer localizer, PluginState pluginState,
    MapCooldown mapCooldown) {
    _mapLister                          =  mapLister;
    _gamerules                          =  gamerules;
    _localizer                          =  localizer;
    _pluginState                        =  pluginState;
    _mapCooldown                        =  mapCooldown;
    _mapCooldown.EventCooldownRefreshed += OnMapsLoaded;
  }


  public void OnMapStart(string map) { Nominations.Clear(); }

  public void OnConfigParsed(Config config) { _config = config.Rtv; }

  public void OnMapsLoaded(object? sender, Map[] maps) {
    nominationMenu = new ChatMenu("Nomination");
    foreach (var map in _mapLister.Maps!.Where(x => x.Name != Server.MapName))
      nominationMenu.AddMenuOption(map.Name,
        (player, option) => { Nominate(player, option.Text); },
        _mapCooldown.IsMapInCooldown(map.Name));
  }

  public void CommandHandler(CCSPlayerController? player, string map) {
    if (player is null) return;

    map = map.ToLower().Trim();
    if (_pluginState.DisableCommands || !_config.NominationEnabled) {
      player.PrintToChat(
        _localizer.LocalizeWithPrefix("general.validation.disabled"));
      return;
    }

    if (_gamerules.WarmupRunning) {
      if (!_config.EnabledInWarmup) {
        player.PrintToChat(
          _localizer.LocalizeWithPrefix("general.validation.warmup"));
        return;
      }
    } else if (_config.MinRounds > 0
      && _config.MinRounds > _gamerules.TotalRoundsPlayed) {
      player!.PrintToChat(_localizer.LocalizeWithPrefix(
        "general.validation.minimum-rounds", _config.MinRounds));
      return;
    }

    if (ServerManager.ValidPlayerCount() < _config!.MinPlayers) {
      player.PrintToChat(_localizer.LocalizeWithPrefix(
        "general.validation.minimum-players", _config!.MinPlayers));
      return;
    }

    if (string.IsNullOrEmpty(map))
      OpenNominationMenu(player!);
    else
      Nominate(player, map);
  }

  public void OpenNominationMenu(CCSPlayerController player) {
    MenuManager.OpenChatMenu(player!, nominationMenu!);
  }

  private void Nominate(CCSPlayerController player, string map) {
    if (_mapLister.Maps!.Select(x => x.Name)
     .FirstOrDefault(x => x.ToLower() == map) is null) {
      var result = _mapLister.Maps!.Select(x => x.Name)
       .FirstOrDefault(x => x.ToLower().Contains(map));
      if (result == null) {
        player!.PrintToChat(
          _localizer.LocalizeWithPrefix("general.invalid-map"));
        return;
      }

      map = result;
    }

    if (_mapCooldown.IsMapInCooldown(map)) {
      player!.PrintToChat(
        _localizer.LocalizeWithPrefix(
          "general.validation.map-played-recently"));
      return;
    }

    if (map == Server.MapName) {
      player!.PrintToChat(
        _localizer.LocalizeWithPrefix("general.validation.current-map"));
      return;
    }

    var userId = player.UserId!.Value;
    if (Nominations.ContainsKey(userId)) return;
    Nominations[userId] = new List<string>();

    var alreadyVoted = Nominations[userId].IndexOf(map) != -1;
    if (!alreadyVoted) Nominations[userId].Add(map);

    var totalVotes = Nominations
     .Select(x => x.Value.Where(y => y == map).Count())
     .Sum();

    if (!alreadyVoted)
      Server.PrintToChatAll(_localizer.LocalizeWithPrefix("nominate.nominated",
        player.PlayerName, map, totalVotes));
    else
      player.PrintToChat(
        _localizer.LocalizeWithPrefix("nominate.already-nominated", map,
          totalVotes));
  }

  public List<string> NominationWinners() {
    if (Nominations.Count == 0) return new List<string>();

    var rawNominations = Nominations.Select(x => x.Value)
     .Aggregate((acc, x) => acc.Concat(x).ToList());

    return rawNominations.Distinct()
     .Select(map
        => new KeyValuePair<string, int>(map,
          rawNominations.Count(x => x == map)))
     .OrderByDescending(x => x.Value)
     .Select(x => x.Key)
     .ToList();
  }

  public void PlayerDisconnected(CCSPlayerController player) {
    var userId = player.UserId!.Value;
    if (!Nominations.ContainsKey(userId)) Nominations.Remove(userId);
  }
}