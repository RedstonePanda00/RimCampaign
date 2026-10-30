using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Verse;

namespace RimCampaign.Runtime {
    // Translate presentation text only. IDs, passwords and the authored project remain unchanged.
    public static class RcText {
        static readonly Dictionary<string,string> keys=new Dictionary<string,string>();
        public static string T(string source){
            if(string.IsNullOrEmpty(source))return source??"";
            if(!keys.TryGetValue(source,out var key)){
                using(var sha=SHA256.Create())key="RC_Text_"+BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(source)),0,8).Replace("-","").ToLowerInvariant();
                keys[source]=key;
            }
            return key.CanTranslate()?key.Translate().ToString():source;
        }
    }
}
