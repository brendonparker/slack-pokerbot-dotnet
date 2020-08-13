namespace slack_pokerbot_dotnet
{
    public class VoteReportRow
    {
        public string Story { get; internal set; }
        public string Date { get; internal set; }
        public string Vote { get; internal set; }
        public int Count { get; internal set; }
        public string People { get; internal set; }
    }
}
