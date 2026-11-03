using System;
using System.Collections.Generic;
using System.Text;

namespace NbaBetting
{
    /// <summary>
    /// The 30 NBA franchises: full name, short code and conference.
    /// Lookup accepts whatever the CSV uses - abbreviations ("bos", "GS", "NOP"), city names,
    /// nicknames or full names - and returns the current franchise (so old names such as
    /// Seattle SuperSonics, New Jersey Nets or Charlotte Bobcats map to today's team).
    /// Conferences are the current ones.
    /// </summary>
    public sealed class TeamInfo
    {
        public readonly string Abbrev;      // e.g. "BOS"
        public readonly string FullName;    // e.g. "Boston Celtics"
        public readonly string Conference;  // "East" or "West"

        private TeamInfo(string abbrev, string fullName, string conference)
        {
            Abbrev = abbrev;
            FullName = fullName;
            Conference = conference;
        }

        private static readonly Dictionary<string, TeamInfo> index = new Dictionary<string, TeamInfo>();
        public static readonly List<TeamInfo> All = new List<TeamInfo>();

        static TeamInfo()
        {
            // Aliases are written in plain lower case, separated by spaces (punctuation/spaces are ignored when matching).
            Add("ATL", "Atlanta Hawks", "East", "atlanta hawks");
            Add("BOS", "Boston Celtics", "East", "boston celtics");
            Add("BKN", "Brooklyn Nets", "East", "bro brk nj njn nets brooklyn newjersey newjerseynets");
            Add("CHA", "Charlotte Hornets", "East", "cho charlotte hornets bobcats charlottebobcats");
            Add("CHI", "Chicago Bulls", "East", "chicago bulls");
            Add("CLE", "Cleveland Cavaliers", "East", "cleveland cavaliers cavs");
            Add("DET", "Detroit Pistons", "East", "detroit pistons");
            Add("IND", "Indiana Pacers", "East", "indiana pacers");
            Add("MIA", "Miami Heat", "East", "miami heat");
            Add("MIL", "Milwaukee Bucks", "East", "milwaukee bucks");
            Add("NYK", "New York Knicks", "East", "ny newyork knicks");
            Add("ORL", "Orlando Magic", "East", "orlando magic");
            Add("PHI", "Philadelphia 76ers", "East", "phl philadelphia 76ers sixers");
            Add("TOR", "Toronto Raptors", "East", "toronto raptors");
            Add("WAS", "Washington Wizards", "East", "wsh washington wizards");

            Add("DAL", "Dallas Mavericks", "West", "dallas mavericks mavs");
            Add("DEN", "Denver Nuggets", "West", "denver nuggets");
            Add("GSW", "Golden State Warriors", "West", "gs gol golden goldenstate warriors");
            Add("HOU", "Houston Rockets", "West", "houston rockets");
            Add("LAC", "Los Angeles Clippers", "West", "laclippers clippers losangelesclippers");
            Add("LAL", "Los Angeles Lakers", "West", "lalakers lakers losangeleslakers");
            Add("MEM", "Memphis Grizzlies", "West", "memphis grizzlies");
            Add("MIN", "Minnesota Timberwolves", "West", "minnesota timberwolves wolves");
            Add("NOP", "New Orleans Pelicans", "West", "no noh nor neworleans pelicans neworleanshornets");
            Add("OKC", "Oklahoma City Thunder", "West", "oklahomacity thunder sea seattle supersonics sonics");
            Add("PHX", "Phoenix Suns", "West", "pho phoenix suns");
            Add("POR", "Portland Trail Blazers", "West", "portland trailblazers blazers");
            Add("SAC", "Sacramento Kings", "West", "sacramento kings");
            Add("SAS", "San Antonio Spurs", "West", "sa sanantonio spurs");
            Add("UTA", "Utah Jazz", "West", "utah jazz");
        }

        private static void Add(string abbrev, string fullName, string conference, string aliases)
        {
            TeamInfo t = new TeamInfo(abbrev, fullName, conference);
            All.Add(t);
            Index(t, abbrev);
            Index(t, fullName);
            foreach (string a in aliases.Split(' '))
                if (a.Length > 0) Index(t, a);
        }

        private static void Index(TeamInfo t, string text)
        {
            index[Key(text)] = t;
        }

        /// <summary>Lower-cases and removes everything that is not a letter or digit.</summary>
        private static string Key(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }

        /// <summary>Returns the franchise for a name as written in the CSV, or null if not recognised.</summary>
        public static TeamInfo Lookup(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            TeamInfo t;
            return index.TryGetValue(Key(raw), out t) ? t : null;
        }
    }
}
