using System.Collections.Generic;

namespace RimCampaign.Core {
    public sealed class SceneInteractionSpec : Named {
        public string Host {get;set;}="";
        public string Label {get;set;}="操作";
        public int UseTicks {get;set;}=60;
        public bool OneShot {get;set;}=true;
        public Condition When {get;set;}=new Condition();
        public List<Act> Actions {get;set;}=new List<Act>();
        public List<Cell> BlockCells {get;set;}=new List<Cell>();
        public string ObstacleDef {get;set;}="";
        public bool DestroyHost {get;set;}=true;
        public Cell EscapeCell {get;set;}
    }
}
