﻿using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Menu;
using CounterStrikeSharp.API.Modules.Timers;
using cs2_rockthevote.Core;
using static CounterStrikeSharp.API.Core.Listeners;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace cs2_rockthevote;
//public partial class Plugin
//{

//    [ConsoleCommand("votebot", "Votes to rock the vote")]
//    public void VoteBot(CCSPlayerController? player, CommandInfo? command)
//    {
//        var bot = ServerManager.ValidPlayers().FirstOrDefault(x => x.IsBot);
//        if (bot is not null)
//        {
//            _endmapVoteManager.MapVoted(bot, "de_dust2");
//        }
//    }
//}

public class EndMapVoteManager : IPluginDependency<Plugin, Config> {
  private const int MAX_OPTIONS_HUD_MENU = 6;
  private readonly ChangeMapManager _changeMapManager;
  private readonly StringLocalizer _localizer;

  private readonly MapLister _mapLister;
  private readonly NominationCommand _nominationManager;
  private int _canVote;

  private IEndOfMapConfig? _config;
  private readonly MapCooldown _mapCooldown;
  private Plugin? _plugin;
  private readonly PluginState _pluginState;

  private readonly HashSet<int> _voted = new();

  private List<string> mapsEllected = new();
  private int timeLeft = -1;
  private Timer? Timer;

  private readonly Dictionary<string, int> Votes = new();

  public EndMapVoteManager(MapLister mapLister,
    ChangeMapManager changeMapManager, NominationCommand nominationManager,
    StringLocalizer localizer, PluginState pluginState,
    MapCooldown mapCooldown) {
    _mapLister         = mapLister;
    _changeMapManager  = changeMapManager;
    _nominationManager = nominationManager;
    _localizer         = localizer;
    _pluginState       = pluginState;
    _mapCooldown       = mapCooldown;
  }

  public void OnLoad(Plugin plugin) {
    _plugin = plugin;
    plugin.RegisterListener<OnTick>(VoteDisplayTick);
  }

  public void OnMapStart(string map) {
    Votes.Clear();
    timeLeft = 0;
    mapsEllected.Clear();
    KillTimer();
  }

  public void MapVoted(CCSPlayerController player, string mapName) {
    if (_config!.HideHudAfterVote) _voted.Add(player.UserId!.Value);

    Votes[mapName] += 1;
    player.PrintToChat(_localizer.LocalizeWithPrefix("emv.you-voted", mapName));
    if (Votes.Select(x => x.Value).Sum() >= _canVote) EndVote();
  }

  private void KillTimer() {
    timeLeft = -1;
    if (Timer is not null) {
      Timer!.Kill();
      Timer = null;
    }
  }

  private void PrintCenterTextAll(string text) {
    foreach (var player in Utilities.GetPlayers())
      if (player.IsValid)
        player.PrintToCenter(text);
  }

  public void VoteDisplayTick() {
    if (timeLeft < 0) return;

    var           index         = 1;
    StringBuilder stringBuilder = new();
    stringBuilder.AppendFormat(
      $"<b>{_localizer.Localize("emv.hud.hud-timer", timeLeft)}</b>");
    if (!_config!.HudMenu)
      foreach (var kv in Votes.OrderByDescending(x => x.Value)
       .Take(MAX_OPTIONS_HUD_MENU)
       .Where(x => x.Value > 0))
        stringBuilder.AppendFormat(
          $"<br>{kv.Key} <font color='green'>({kv.Value})</font>");
    else
      foreach (var kv in Votes.Take(MAX_OPTIONS_HUD_MENU))
        stringBuilder.AppendFormat(
          $"<br><font color='yellow'>!{index++}</font> {kv.Key} <font color='green'>({kv.Value})</font>");

    foreach (var player in ServerManager.ValidPlayers()
     .Where(x => !_voted.Contains(x.UserId!.Value)))
      player.PrintToCenterHtml(stringBuilder.ToString());
  }

  private void EndVote() {
    var mapEnd = _config is EndOfMapConfig;
    KillTimer();
    decimal maxVotes = Votes.Select(x => x.Value).Max();
    IEnumerable<KeyValuePair<string, int>> potentialWinners =
      Votes.Where(x => x.Value == maxVotes);
    Random rnd = new();
    KeyValuePair<string, int> winner =
      potentialWinners.ElementAt(rnd.Next(0, potentialWinners.Count()));

    decimal totalVotes = Votes.Select(x => x.Value).Sum();
    var     percent    = totalVotes > 0 ? winner.Value / totalVotes * 100M : 0;

    if (maxVotes > 0)
      Server.PrintToChatAll(_localizer.LocalizeWithPrefix("emv.vote-ended",
        winner.Key, percent, totalVotes));
    else
      Server.PrintToChatAll(
        _localizer.LocalizeWithPrefix("emv.vote-ended-no-votes", winner.Key));

    PrintCenterTextAll(_localizer.Localize("emv.hud.finished", winner.Key));
    _changeMapManager.ScheduleMapChange(winner.Key, mapEnd);
    if (_config!.ChangeMapImmediatly) {
      _changeMapManager.ChangeNextMap(mapEnd);
    } else {
      if (!mapEnd)
        Server.PrintToChatAll(
          _localizer.LocalizeWithPrefix("general.changing-map-next-round",
            winner.Key));
    }
  }

  private IList<T> Shuffle<T>(Random rng, IList<T> array) {
    var n = array.Count;
    while (n > 1) {
      var k    = rng.Next(n--);
      var temp = array[n];
      array[n] = array[k];
      array[k] = temp;
    }

    return array;
  }

  public void StartVote(IEndOfMapConfig config) {
    Votes.Clear();
    _voted.Clear();

    _pluginState.EofVoteHappening = true;
    _config                       = config;
    var mapsToShow = _config!.MapsToShow == 0 ?
      MAX_OPTIONS_HUD_MENU :
      _config!.MapsToShow;
    if (config.HudMenu && mapsToShow > MAX_OPTIONS_HUD_MENU)
      mapsToShow = MAX_OPTIONS_HUD_MENU;

    var mapsScrambled = Shuffle(new Random(),
      _mapLister.Maps!.Select(x => x.Name)
       .Where(x => x != Server.MapName && !_mapCooldown.IsMapInCooldown(x))
       .ToList());
    mapsEllected = _nominationManager.NominationWinners()
     .Concat(mapsScrambled)
     .Distinct()
     .ToList();

    _canVote = ServerManager.ValidPlayerCount();
    ChatMenu menu = new(_localizer.Localize("emv.hud.menu-title"));
    foreach (var map in mapsEllected.Take(mapsToShow)) {
      Votes[map] = 0;
      menu.AddMenuOption(map, (player, option) => {
        MapVoted(player, map);
        MenuManager.CloseActiveMenu(player);
      });
    }

    foreach (var player in ServerManager.ValidPlayers())
      MenuManager.OpenChatMenu(player, menu);

    timeLeft = _config.VoteDuration;
    Timer = _plugin!.AddTimer(1.0F, () => {
      if (timeLeft <= 0)
        EndVote();
      else
        timeLeft--;
    }, TimerFlags.REPEAT);
  }
}