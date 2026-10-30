namespace RimCampaign.Core
{
    public class BookText {
        public string Title { get; set; } = ""; public string Text { get; set; } = "";
        // Native XML saving uses platform line endings; this does not alter content.
        public static string Normalize(string value)=>value?.Replace("\r\n","\n").Replace("\r","\n");
    }
    public class DeviceLoot { public DefRef Def { get; set; } = new DefRef(); public DefRef Stuff { get; set; } public int Count { get; set; } = 1; public BookText Book { get; set; } }
}
