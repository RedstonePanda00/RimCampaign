using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public static class EditorFocus
    {
        public static int Revision { get; private set; }
        public static void Clear(){UI.UnfocusCurrentControl();GUIUtility.keyboardControl=0;}
        public static void Release(){Clear();Revision++;}
        public static bool Typing=>GUIUtility.keyboardControl!=0;
    }
}
