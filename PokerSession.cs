using System;
using System.Collections.Generic;

namespace slack_pokerbot_dotnet
{
    public class PokerSession
    {
        public long Id { get; set; } = DateTime.UtcNow.Ticks;
        public DateTime StartedOn { get; set; } = DateTime.UtcNow;
        public string JiraTicket { get; set; }
        public List<PokerSessionVote> Votes { get; set; } = new List<PokerSessionVote>();
    }

    public class PokerSessionVote
    {
        public string UserId { get; set; }
        public string UserName { get; set; }
        public string Vote { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
