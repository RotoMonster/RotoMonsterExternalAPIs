using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using RotoMonsterExternalAPIs.Client.Models.Providers;
using RotoMonsterExternalAPIs.Client.Models.Results;
using RotoMonsterExternalAPIs.Client.Services.Yahoo;

namespace RotoMonsterExternalAPIs.Client.Services.Providers
{
    /// <summary>
    /// Yahoo as an IFantasyProvider.
    ///
    /// The reason this provider matters: Yahoo accepts many league keys in one
    /// request, so importing twenty leagues costs two calls rather than sixty.
    /// Settings and draft results arrive together from a single ";out=" call,
    /// and rosters come from a second.
    ///
    /// Yahoo returns XML by default, which is easier to walk than their JSON,
    /// where collections come back as objects keyed by number.
    /// </summary>
    public class YahooFantasyProvider : IFantasyProvider
    {
        private const string BaseUrl = "https://fantasysports.yahooapis.com/fantasy/v2/";

        /// <summary>
        /// Keys per request. 45 came back fine in testing at about 1.7MB, but
        /// that was the largest account available rather than a documented
        /// limit, and the payload grows with the count. 25 leaves room.
        /// </summary>
        private const int ChunkSize = 25;

        private static readonly XNamespace Ns =
            "http://fantasysports.yahooapis.com/fantasy/v2/base.rng";

        private readonly YahooApiClient _client;

        public YahooFantasyProvider(YahooApiClient client)
        {
            if (client == null) throw new ArgumentNullException(nameof(client));
            _client = client;
        }

        public string ProviderName
        {
            get { return "Yahoo!"; }
        }

        // -------------------------------------------------------------------
        // League list
        // -------------------------------------------------------------------

        public async Task<GetProviderLeaguesResult> GetLeaguesAsync(string userKey, string seasonKey)
        {
            if (string.IsNullOrEmpty(seasonKey))
                return BaseResult.Failure<GetProviderLeaguesResult>("A Yahoo game key is required.");

            var url = BaseUrl + "users;use_login=1/games;game_keys=" + seasonKey + "/leagues";

            var response = await _client.GetAsync(userKey, url).ConfigureAwait(false);
            if (!response.Success)
            {
                var failed = BaseResult.Failure<GetProviderLeaguesResult>(response.ErrorMessage);
                failed.NeedsReauthorization = response.TokenRejected;
                return failed;
            }

            XDocument doc;
            if (!TryParse(response.Content, out doc))
                return BaseResult.Failure<GetProviderLeaguesResult>("Yahoo returned a response we could not read.");

            var result = new GetProviderLeaguesResult { Success = true };

            foreach (var leagueNode in doc.Descendants(Ns + "league"))
            {
                var league = new ProviderLeague
                {
                    LeagueId = Value(leagueNode, "league_id"),
                    Title = Value(leagueNode, "name"),
                    SeasonKey = seasonKey
                };

                if (!string.IsNullOrEmpty(league.LeagueId))
                    result.Leagues.Add(league);
            }

            return result;
        }

        // -------------------------------------------------------------------
        // League data
        // -------------------------------------------------------------------

        public async Task<GetProviderLeagueDataResult> GetLeagueDataAsync(
            string userKey,
            string seasonKey,
            IList<string> leagueIds,
            ProviderLeagueDataParts parts)
        {
            if (string.IsNullOrEmpty(seasonKey))
                return BaseResult.Failure<GetProviderLeagueDataResult>("A Yahoo game key is required.");

            var result = new GetProviderLeagueDataResult { Success = true, PartsReturned = parts };

            if (leagueIds == null || leagueIds.Count == 0 || parts == ProviderLeagueDataParts.None)
                return result;

            // One entry per league up front, so a league Yahoo drops out of a
            // response still appears in the result rather than vanishing.
            var byId = new Dictionary<string, ProviderLeagueData>();
            foreach (var id in leagueIds)
            {
                if (string.IsNullOrEmpty(id) || byId.ContainsKey(id)) continue;

                var data = new ProviderLeagueData { LeagueId = id };
                if ((parts & ProviderLeagueDataParts.Settings) != 0) data.Settings = null;
                if ((parts & ProviderLeagueDataParts.Rosters) != 0) data.Teams = new List<ProviderTeam>();
                if ((parts & ProviderLeagueDataParts.Drafts) != 0) data.DraftPicks = new List<ProviderDraftPick>();

                byId[id] = data;
                result.Leagues.Add(data);
            }

            var wantsSettings = (parts & ProviderLeagueDataParts.Settings) != 0;
            var wantsDrafts = (parts & ProviderLeagueDataParts.Drafts) != 0;
            var wantsRosters = (parts & ProviderLeagueDataParts.Rosters) != 0;

            foreach (var chunk in Chunk(byId.Keys.ToList(), ChunkSize))
            {
                // Settings and draft results share a request. This is the whole
                // point of the batching, so they are fetched together even
                // though the caller asked for them as separate flags.
                if (wantsSettings || wantsDrafts)
                {
                    var subResources = new List<string>();
                    if (wantsSettings) subResources.Add("settings");
                    if (wantsDrafts) subResources.Add("draftresults");

                    var url = BaseUrl + "leagues;league_keys=" + Keys(seasonKey, chunk)
                              + ";out=" + string.Join(",", subResources);

                    var response = await _client.GetAsync(userKey, url).ConfigureAwait(false);
                    result.RequestCount++;

                    if (!response.Success)
                    {
                        if (response.TokenRejected)
                        {
                            var failed = BaseResult.Failure<GetProviderLeagueDataResult>(response.ErrorMessage);
                            failed.NeedsReauthorization = true;
                            return failed;
                        }

                        MarkChunkFailed(byId, chunk, response.ErrorMessage);
                    }
                    else
                    {
                        XDocument doc;
                        if (TryParse(response.Content, out doc))
                            ReadLeagueNodes(doc, byId, wantsSettings, wantsDrafts);
                        else
                            MarkChunkFailed(byId, chunk, "Yahoo returned a response we could not read.");
                    }
                }

                if (wantsRosters)
                {
                    var url = BaseUrl + "leagues;league_keys=" + Keys(seasonKey, chunk)
                              + "/teams/roster/players";

                    var response = await _client.GetAsync(userKey, url).ConfigureAwait(false);
                    result.RequestCount++;

                    if (!response.Success)
                    {
                        if (response.TokenRejected)
                        {
                            var failed = BaseResult.Failure<GetProviderLeagueDataResult>(response.ErrorMessage);
                            failed.NeedsReauthorization = true;
                            return failed;
                        }

                        MarkChunkFailed(byId, chunk, response.ErrorMessage);
                    }
                    else
                    {
                        XDocument doc;
                        if (TryParse(response.Content, out doc))
                            ReadRosters(doc, byId);
                        else
                            MarkChunkFailed(byId, chunk, "Yahoo returned a response we could not read.");
                    }
                }
            }

            return result;
        }

        // -------------------------------------------------------------------
        // Parsing
        // -------------------------------------------------------------------

        private static void ReadLeagueNodes(
            XDocument doc,
            Dictionary<string, ProviderLeagueData> byId,
            bool wantsSettings,
            bool wantsDrafts)
        {
            foreach (var leagueNode in doc.Descendants(Ns + "league"))
            {
                var leagueId = Value(leagueNode, "league_id");
                if (string.IsNullOrEmpty(leagueId)) continue;

                ProviderLeagueData data;
                if (!byId.TryGetValue(leagueId, out data)) continue;

                if (wantsSettings)
                    data.Settings = ReadSettings(leagueNode, leagueId);

                if (wantsDrafts)
                    data.DraftPicks = ReadDraftPicks(leagueNode, leagueId);
            }
        }

        private static ProviderLeagueSettings ReadSettings(XElement leagueNode, string leagueId)
        {
            var settings = new ProviderLeagueSettings
            {
                LeagueId = leagueId,
                Title = Value(leagueNode, "name"),
                IsMoney = Value(leagueNode, "is_cash_league") == "1",
                IsProLeague = Value(leagueNode, "is_pro_league") == "1",
                HasDrafted = Value(leagueNode, "draft_status") == "postdraft",
                NumberOfTeams = Int(Value(leagueNode, "num_teams"))
            };

            // Yahoo says "head", "roto" or "point". RM stores H or R for the
            // league type and C or P for the scoring system, and a points
            // league is still head to head.
            var scoringType = Value(leagueNode, "scoring_type");
            settings.LeagueType = scoringType == "roto" ? "R" : "H";
            settings.ScoringSystem = scoringType == "point" ? "P" : "C";

            // weekly_deadline is "1" for weekly lineups, "intraday" for daily
            // leagues that allow same day moves, empty otherwise.
            var deadline = Value(leagueNode, "weekly_deadline");
            if (deadline == "1")
            {
                settings.LineupFrequency = "W";
                settings.SameDayTransactions = true;
            }
            else
            {
                settings.LineupFrequency = "D";
                settings.SameDayTransactions = deadline == "intraday";
            }

            var settingsNode = leagueNode.Element(Ns + "settings");
            if (settingsNode == null) return settings;

            settings.IsAuction = Value(settingsNode, "is_auction_draft") == "1";

            // max_teams is the real cap; num_teams is how many have joined.
            var maxTeams = Int(Value(settingsNode, "max_teams"));
            if (maxTeams > 0) settings.NumberOfTeams = maxTeams;

            settings.WaiverType = Value(settingsNode, "waiver_type");
            settings.WaiverRule = Value(settingsNode, "waiver_rule");
            settings.ContinuousWaivers = settings.WaiverRule == "continuous";

            var draftTime = Value(settingsNode, "draft_time");
            if (!string.IsNullOrEmpty(draftTime))
            {
                long epoch;
                if (long.TryParse(draftTime, NumberStyles.Integer, CultureInfo.InvariantCulture, out epoch))
                {
                    settings.DraftDate = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
                        .AddSeconds(epoch);
                }
            }

            ReadRosterSpots(settingsNode, settings);
            ReadCategories(settingsNode, settings);

            return settings;
        }

        private static void ReadRosterSpots(XElement settingsNode, ProviderLeagueSettings settings)
        {
            var positionsNode = settingsNode.Element(Ns + "roster_positions");
            if (positionsNode == null) return;

            foreach (var posNode in positionsNode.Elements(Ns + "roster_position"))
            {
                var code = Value(posNode, "position");
                if (string.IsNullOrEmpty(code)) continue;

                var count = Int(Value(posNode, "count"));
                settings.RosterSpots.Add(new ProviderRosterSpot { Code = code, Count = count });

                // Knowing that IL, IR and DL mean injured and BN means bench is
                // Yahoo knowledge, so the totals are worked out here rather
                // than left for every caller to redo.
                if (code == "IL" || code == "IR" || code == "DL")
                    settings.IRSpots += count;
                else
                    settings.PlayersPerTeam += count;
            }
        }

        private static void ReadCategories(XElement settingsNode, ProviderLeagueSettings settings)
        {
            var statsNode = settingsNode.Element(Ns + "stat_categories");
            if (statsNode != null)
            {
                var statsList = statsNode.Element(Ns + "stats");
                if (statsList != null)
                {
                    foreach (var statNode in statsList.Elements(Ns + "stat"))
                    {
                        var code = Value(statNode, "stat_id");
                        if (string.IsNullOrEmpty(code)) continue;

                        settings.Categories.Add(new ProviderCategory
                        {
                            Code = code,
                            Name = Value(statNode, "name"),
                            PositionType = Value(statNode, "position_type"),
                            IsDisplayOnly = Value(statNode, "is_only_display_stat") == "1"
                        });
                    }
                }
            }

            // Modifiers only appear in a points league, and their presence is a
            // more reliable signal of one than scoring_type is.
            var modifiersNode = settingsNode.Element(Ns + "stat_modifiers");
            if (modifiersNode == null) return;

            var modifierStats = modifiersNode.Element(Ns + "stats");
            if (modifierStats == null) return;

            settings.ScoringSystem = "P";

            foreach (var statNode in modifierStats.Elements(Ns + "stat"))
            {
                var code = Value(statNode, "stat_id");
                if (string.IsNullOrEmpty(code)) continue;

                var category = settings.Categories.FirstOrDefault(c => c.Code == code);
                if (category == null) continue;

                double points;
                if (double.TryParse(Value(statNode, "value"), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out points))
                {
                    category.PointsPerStat = points;
                }
            }
        }

        private static List<ProviderDraftPick> ReadDraftPicks(XElement leagueNode, string leagueId)
        {
            var picks = new List<ProviderDraftPick>();

            var resultsNode = leagueNode.Element(Ns + "draft_results");
            if (resultsNode == null) return picks;

            foreach (var pickNode in resultsNode.Elements(Ns + "draft_result"))
            {
                var playerKey = Value(pickNode, "player_key");
                if (string.IsNullOrEmpty(playerKey)) continue;

                var pick = new ProviderDraftPick
                {
                    LeagueId = leagueId,
                    PlayerId = LastSegment(playerKey),
                    TeamId = LastSegment(Value(pickNode, "team_key")),
                    PickNumber = Int(Value(pickNode, "pick")),
                    Round = Int(Value(pickNode, "round"))
                };

                // Only auctions carry a cost.
                var cost = Value(pickNode, "cost");
                if (!string.IsNullOrEmpty(cost))
                {
                    int price;
                    if (int.TryParse(cost, NumberStyles.Integer, CultureInfo.InvariantCulture, out price))
                        pick.Price = price;
                }

                picks.Add(pick);
            }

            return picks;
        }

        private static void ReadRosters(XDocument doc, Dictionary<string, ProviderLeagueData> byId)
        {
            foreach (var leagueNode in doc.Descendants(Ns + "league"))
            {
                var leagueId = Value(leagueNode, "league_id");
                if (string.IsNullOrEmpty(leagueId)) continue;

                ProviderLeagueData data;
                if (!byId.TryGetValue(leagueId, out data)) continue;

                if (data.Teams == null) data.Teams = new List<ProviderTeam>();

                var teamsNode = leagueNode.Element(Ns + "teams");
                if (teamsNode == null) continue;

                foreach (var teamNode in teamsNode.Elements(Ns + "team"))
                {
                    var team = new ProviderTeam
                    {
                        LeagueId = leagueId,
                        TeamId = Value(teamNode, "team_id"),
                        Title = Value(teamNode, "name")
                    };

                    // Yahoo has no draft slot on the team, and team_id is
                    // assigned in draft order, which is what RM already relies
                    // on. Carried over rather than invented.
                    team.DraftOrder = Int(team.TeamId);

                    var managersNode = teamNode.Element(Ns + "managers");
                    if (managersNode != null)
                    {
                        team.IsMyTeam = managersNode.Elements(Ns + "manager")
                            .Any(m => Value(m, "is_current_login") == "1");
                    }

                    var rosterNode = teamNode.Element(Ns + "roster");
                    if (rosterNode != null)
                    {
                        var playersNode = rosterNode.Element(Ns + "players");
                        if (playersNode != null)
                        {
                            foreach (var playerNode in playersNode.Elements(Ns + "player"))
                                team.Players.Add(ReadRosterPlayer(playerNode));
                        }
                    }

                    data.Teams.Add(team);
                }
            }
        }

        private static ProviderRosterPlayer ReadRosterPlayer(XElement playerNode)
        {
            var player = new ProviderRosterPlayer
            {
                PlayerId = Value(playerNode, "player_id")
            };

            var nameNode = playerNode.Element(Ns + "name");
            if (nameNode != null)
                player.Name = Value(nameNode, "full");

            var selected = playerNode.Element(Ns + "selected_position");
            if (selected != null)
                player.PositionCode = Value(selected, "position");

            player.IsIR = player.PositionCode == "IL" || player.PositionCode == "IR";
            player.IsActive = !player.IsIR && player.PositionCode != "BN";

            return player;
        }

        // -------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------

        /// <summary>
        /// Yahoo wants full league keys, "gamekey.l.leagueid", but everything
        /// else in RM stores the bare league id, so they are built here.
        /// </summary>
        private static string Keys(string seasonKey, IEnumerable<string> leagueIds)
        {
            return string.Join(",", leagueIds.Select(id => seasonKey + ".l." + id));
        }

        private static IEnumerable<List<string>> Chunk(List<string> items, int size)
        {
            for (var i = 0; i < items.Count; i += size)
                yield return items.Skip(i).Take(Math.Min(size, items.Count - i)).ToList();
        }

        private static void MarkChunkFailed(
            Dictionary<string, ProviderLeagueData> byId,
            IEnumerable<string> chunk,
            string message)
        {
            foreach (var id in chunk)
            {
                ProviderLeagueData data;
                if (byId.TryGetValue(id, out data) && !data.HasError)
                    data.ErrorMessage = message;
            }
        }

        private static bool TryParse(string xml, out XDocument doc)
        {
            doc = null;
            if (string.IsNullOrEmpty(xml)) return false;

            try
            {
                doc = XDocument.Parse(xml);
                return true;
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
        }

        private static string Value(XElement parent, string name)
        {
            if (parent == null) return "";
            var element = parent.Element(Ns + name);
            return element == null ? "" : element.Value.Trim();
        }

        /// <summary>
        /// Yahoo keys look like "431.l.12345" or "431.p.9876". The trailing
        /// segment is the bare id RM stores.
        /// </summary>
        private static string LastSegment(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var index = key.LastIndexOf('.');
            return index < 0 ? key : key.Substring(index + 1);
        }

        private static int Int(string text)
        {
            int value;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : 0;
        }
    }
}
