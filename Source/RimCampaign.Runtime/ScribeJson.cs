using System;
using System.Text;
using Verse;

namespace RimCampaign.Runtime
{
    // Scribe's string loader interprets escaped newlines. Protect embedded JSON rather
    // than letting those escapes turn into illegal literal control characters.
    public static class ScribeJson
    {
        const string Prefix="RCJ1:";
        public static void Look(ref string json,string label){
            string stored=Scribe.mode==LoadSaveMode.Saving&&json!=null?Prefix+Convert.ToBase64String(Encoding.UTF8.GetBytes(json)):json;
            Scribe_Values.Look(ref stored,label);
            if(Scribe.mode==LoadSaveMode.LoadingVars)json=stored!=null&&stored.StartsWith(Prefix,StringComparison.Ordinal)?Encoding.UTF8.GetString(Convert.FromBase64String(stored.Substring(Prefix.Length))):stored;
        }
    }
}
