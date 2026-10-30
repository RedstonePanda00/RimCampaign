using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace RimCampaign.Core
{
    // Rendering is provided by the runtime/SDK; the timeline remains engine independent.
    public class CommunicationSpec
    {
        public string Speaker { get; set; } = "";
        public string SpeakerName { get; set; } = "";
        public DefRef Portrait { get; set; } = new DefRef { Kind="CommunicationPortraitDef", Package="", Name="" };
        public string PortraitPath { get; set; } = "";
        public string PortraitPackage { get; set; } = "";
        // Normalized coordinates measured from the image's top left.
        public float CropX { get; set; }
        public float CropY { get; set; }
        public float CropWidth { get; set; } = 1;
        public float CropHeight { get; set; } = 1;
        public double Duration { get; set; } = 6;
        public float X { get; set; } = 16;
        public float Y { get; set; } = 260;
        public float Width { get; set; } = 620;
        public float PortraitSize { get; set; } = 96;
        public float BackgroundOpacity { get; set; } = .28f;
        public CommunicationSpectrumSpec Spectrum { get; set; }
        public string Renderer { get; set; } = "";
        public string RendererPackage { get; set; } = "";
        public Dictionary<string,string> RendererData { get; set; } = new Dictionary<string,string>();
        [OnDeserializing] void Defaults(StreamingContext context) {
            Speaker=SpeakerName=PortraitPath=PortraitPackage=Renderer=RendererPackage="";
            Portrait=new DefRef{Kind="CommunicationPortraitDef",Package="",Name=""};
            CropWidth=CropHeight=1; Duration=6;X=16;Y=260;Width=620;PortraitSize=96;BackgroundOpacity=.28f;
            RendererData=new Dictionary<string,string>();
        }
        public static bool ValidCrop(float x,float y,float w,float h) =>
            x>=0&&y>=0&&w>0&&h>0&&x+w<=1.00001f&&y+h<=1.00001f;
        public IEnumerable<string> Errors() {
            if(double.IsNaN(Duration)||double.IsInfinity(Duration)||Duration<=0||Duration>3600)yield return "Communication duration must be within (0, 3600] seconds.";
            if(!ValidCrop(CropX,CropY,CropWidth,CropHeight))yield return "Communication crop must fit inside the image.";
            if(!(BackgroundOpacity>=0&&BackgroundOpacity<=1))yield return "Communication background opacity must be between 0 and 1.";
            if(Spectrum!=null)foreach(var error in Spectrum.Errors())yield return error;
            if(!(X>=0&&X<=10000&&Y>=0&&Y<=10000&&PortraitSize>=48&&PortraitSize<=256&&Width>=PortraitSize+180&&Width<=2000))yield return "Invalid communication layout.";
            if(!string.IsNullOrEmpty(Portrait?.Name)&&Portrait.Kind!="CommunicationPortraitDef")yield return "Communication portrait must reference a CommunicationPortraitDef.";
        }
    }
    public class CommunicationMessage
    {
        public string ActionId { get; set; }
        public string Source { get; set; }
        public string Text { get; set; }
        public CommunicationSpec Spec { get; set; }
        public double Elapsed { get; set; }
        public string ConversationId { get; set; } = "";
        public bool Completed { get; set; }
        public bool StartNotified { get; set; }
    }
    public class CommunicationQueue
    {
        public List<CommunicationMessage> Messages { get; set; } = new List<CommunicationMessage>();
        [IgnoreDataMember] public CommunicationMessage Current => Messages.FirstOrDefault(m=>!m.Completed);
        public bool TryNotifyConversationStart() {
            var current=Current;if(current==null||current.StartNotified)return false;
            if(string.IsNullOrEmpty(current.ConversationId))current.StartNotified=true;
            else foreach(var message in Messages.Where(m=>m.ConversationId==current.ConversationId))message.StartNotified=true;
            return true;
        }
        // Keep the whole consecutive dialogue block until it finishes, so a new
        // conversation can interrupt it without replaying surrounding story effects.
        public bool ExecuteConversation(List<Act> lines,string source,double delta) {
            string id=lines[0].Id;
            if(!Messages.Any(m=>m.ConversationId==id)){
                var incoming=lines.Select(a=>{
                    var spec=a.Communication??new CommunicationSpec();
                    var error=spec.Errors().FirstOrDefault();if(error!=null)throw new InvalidOperationException(error);
                    return new CommunicationMessage{ActionId=a.Id,ConversationId=id,Source=source,Text=a.Text,Spec=Json.Copy(spec)};
                }).ToList();
                string interrupted=Current?.ConversationId;
                if(!string.IsNullOrEmpty(interrupted))foreach(var m in Messages.Where(m=>m.ConversationId==interrupted)){m.Completed=false;m.Elapsed=0;m.StartNotified=false;}
                Messages.InsertRange(0,incoming);return false;
            }
            var current=Current;if(current?.ConversationId!=id)return false;
            if(delta>0&&!double.IsNaN(delta)&&!double.IsInfinity(delta))current.Elapsed+=delta;
            if(current.Elapsed<current.Spec.Duration)return false;
            current.Completed=true;
            if(Messages.Any(m=>m.ConversationId==id&&!m.Completed))return false;
            Messages.RemoveAll(m=>m.ConversationId==id);return true;
        }
        public bool Execute(Act action,string source,double delta) {
            var message=Messages.FirstOrDefault(m=>m.ActionId==action.Id);
            if(message==null) {
                var spec=action.Communication??new CommunicationSpec();
                var error=spec.Errors().FirstOrDefault();if(error!=null)throw new InvalidOperationException(error);
                Messages.Add(new CommunicationMessage{ActionId=action.Id,Source=source,Text=action.Text,Spec=Json.Copy(spec)});
                return false;
            }
            if(message!=Current)return false;
            if(delta>0&&!double.IsNaN(delta)&&!double.IsInfinity(delta))message.Elapsed+=delta;
            if(message.Elapsed<message.Spec.Duration)return false;
            Messages.Remove(message);return true;
        }
        public void Retain(IEnumerable<string> activeIds) {
            var alive=new HashSet<string>(activeIds);Messages.RemoveAll(m=>!alive.Contains(string.IsNullOrEmpty(m.ConversationId)?m.ActionId:m.ConversationId));
        }
    }
}
