using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimCampaign.Runtime
{
    public sealed partial class StealthMapComponent
    {
        public Dictionary<string,ConversationMemory> Conversations=new Dictionary<string,ConversationMemory>();
        int nextConversationTick;
        readonly CommunicationPresenter conversationPresenter=new CommunicationPresenter();
        void ExposeConversations(){
            Scribe_Values.Look(ref nextConversationTick,"rcNextPatrolConversationTick");
            string data=Json.Write(Conversations);ScribeJson.Look(ref data,"rcPatrolConversations");
            if(Scribe.mode==LoadSaveMode.LoadingVars)Conversations=data==null?new Dictionary<string,ConversationMemory>():Json.Read<Dictionary<string,ConversationMemory>>(data);
        }
        bool Calm(Pawn p)=>p?.Spawned==true&&!p.Dead&&!p.Downed&&!Sleeping(p)&&Guard(p)!=null&&
            (State.Guards[Guard(p).Id].Mode=="Patrol"||State.Guards[Guard(p).Id].Mode=="Observing")&&
            !State.Guards[Guard(p).Id].Alarmed&&string.IsNullOrEmpty(State.Guards[Guard(p).Id].ScriptedAction);
        bool ConversationHolds(Pawn p)=>Config.Conversations.Any(c=>Conversations.TryGetValue(c.Id,out var m)&&m.Active&&(Resolve(c.First)==p||Resolve(c.Second)==p));
        bool Audible(PatrolConversation c){
            if(Actor?.Spawned!=true||Actor.Dead||Actor.Downed)return false;
            bool Appears(string id){
                var pawn=Resolve(id) as Pawn;
                if(pawn?.Spawned!=true||pawn.Position.DistanceToSquared(Actor.Position)>Config.PlayerSight*Config.PlayerSight||!PlayerCanTarget(pawn))return false;
                return c.HearThroughWalls||AcousticField.Costs(map,pawn.Position,Config.PlayerSight).ContainsKey(Actor.Position);
            }
            return Appears(c.First)&&Appears(c.Second);
        }
        void TickConversations(int tick){
            var audibility=new Dictionary<string,bool>();
            bool Heard(PatrolConversation c){if(!audibility.TryGetValue(c.Id,out var heard))audibility[c.Id]=heard=Audible(c);return heard;}
            foreach(var c in Config.Conversations){
                if(!Conversations.TryGetValue(c.Id,out var m))Conversations[c.Id]=m=new ConversationMemory();
                if(m.Complete||c.Lines.Count==0)continue;
                var a=Resolve(c.First) as Pawn;var b=Resolve(c.Second) as Pawn;
                bool ready=Calm(a)&&Calm(b)&&a.Position.DistanceTo(b.Position)<=c.ParticipantDistance;
                if(m.Active&&!ready){m.Active=false;continue;}
                if(!m.Active){
                    if(tick<nextConversationTick||!ready||!Heard(c)||Conversations.Values.Any(other=>other.Active))continue;
                    m.Active=true;m.Line=0;m.Ticks=0;m.HeardAll=true;m.StartNotified=false;
                    Wait(a);Wait(b);
                }
                a.Rotation=Rot4.FromAngleFlat((b.DrawPos-a.DrawPos).AngleFlat());b.Rotation=a.Rotation.Opposite;
                bool heard=Heard(c)&&CampaignController.ForMap(map).Communications.Current==null&&Config.Conversations.FirstOrDefault(x=>Conversations.TryGetValue(x.Id,out var active)&&active.Active&&Heard(x))==c;
                // Hidden/interrupted dialogue cannot age offscreen. Restart the
                // entire exchange when this conversation becomes audible again.
                if(!heard){m.Line=0;m.Ticks=0;m.HeardAll=true;m.StartNotified=false;if(!Heard(c))m.Active=false;continue;}
                m.Ticks+=Config.UpdateTicks;
                if(m.Ticks>=(c.Lines[m.Line].Communication?.Duration??5)*60){m.Ticks=0;m.Line++;}
                if(m.Line>=c.Lines.Count){m.Active=false;m.Complete=m.HeardAll;if(m.Complete)nextConversationTick=tick+1800;}
            }
        }
        void DrawConversations(){
            var controller=CampaignController.ForMap(map);
            if(controller?.State.Phase!="Running"||controller.Communications.Current!=null)return;
            var c=Config.Conversations.FirstOrDefault(x=>Conversations.TryGetValue(x.Id,out var m)&&m.Active&&m.Line<x.Lines.Count&&Audible(x));
            if(c==null||!Audible(c))return;
            var state=Conversations[c.Id];var line=c.Lines[state.Line];
            if(Event.current.type==EventType.Repaint&&state.TryNotifyStart())CommunicationAudio.PlayStartCue();
            conversationPresenter.Draw(new CommunicationMessage{Text=line.Text,Spec=line.Communication,Elapsed=state.Ticks/60d},Resolve(line.Communication.Speaker) as Pawn,new Rect(0,0,UI.screenWidth,UI.screenHeight));
        }
    }
}
