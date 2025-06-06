﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace cs2_rockthevote;

public partial class Plugin {
  [GameEventHandler()]
  public HookResult OnRoundEndMapChanger(EventRoundEnd @event,
    GameEventInfo info) {
    _changeMapManager.ChangeNextMap();
    return HookResult.Continue;
  }

  [GameEventHandler()]
  public HookResult OnRoundStartMapChanger(EventRoundStart @event,
    GameEventInfo info) {
    _changeMapManager.ChangeNextMap();
    return HookResult.Continue;
  }
}

public class ChangeMapManager : IPluginDependency<Plugin, Config> {
  private const string DEFAULT_PREFIX = "rtv.prefix";
  private Config _config;
  private readonly StringLocalizer _localizer;
  private bool _mapEnd;
  private readonly MapLister _mapLister;
  
  private Map[] _maps = new Map[0];
  private Plugin? _plugin;
  private readonly PluginState _pluginState;
  private string _prefix = DEFAULT_PREFIX;

  public ChangeMapManager(StringLocalizer localizer, PluginState pluginState,
    MapLister mapLister) {
    _localizer                 =  localizer;
    _pluginState               =  pluginState;
    _mapLister                 =  mapLister;
    _mapLister.EventMapsLoaded += OnMapsLoaded;
  }

  public string? NextMap { get; private set; }

  public void OnMapStart(string _map) {
    NextMap = null;
    _prefix = DEFAULT_PREFIX;
  }

  public void OnConfigParsed(Config config) { _config = config; }

  public void OnLoad(Plugin plugin) {
    _plugin = plugin;
    plugin.RegisterEventHandler<EventCsWinPanelMatch>((ev, info) => {
      if (_pluginState.MapChangeScheduled) {
        var delay =
          _config.EndOfMapVote.DelayToChangeInTheEnd
          - 3.0F; //subtracting the delay that is going to be applied by ChangeNextMap function anyway
        if (delay < 0) delay = 0;

        _plugin.AddTimer(delay, () => { ChangeNextMap(true); });
      }

      return HookResult.Continue;
    });
  }

  public void OnMapsLoaded(object? sender, Map[] maps) { _maps = maps; }


  public void ScheduleMapChange(string map, bool mapEnd = false,
    string prefix = DEFAULT_PREFIX) {
    NextMap                         = map;
    _prefix                         = prefix;
    _pluginState.MapChangeScheduled = true;
    _mapEnd                         = mapEnd;
  }

  public bool ChangeNextMap(bool mapEnd = false) {
    if (mapEnd != _mapEnd) return false;

    if (!_pluginState.MapChangeScheduled) return false;

    _pluginState.MapChangeScheduled = false;
    Server.PrintToChatAll(
      _localizer.LocalizeWithPrefixInternal(_prefix, "general.changing-map",
        NextMap!));
    _plugin.AddTimer(3.0F, () => {
      var map = _maps.FirstOrDefault(x => x.Name == NextMap!)!;
      if (Server.IsMapValid(map.Name))
        Server.ExecuteCommand($"changelevel {map.Name}");
      else if (map.Id is not null)
        Server.ExecuteCommand($"host_workshop_map {map.Id}");
      else
        Server.ExecuteCommand($"ds_workshop_changelevel {map.Name}");
    });
    return true;
  }
}