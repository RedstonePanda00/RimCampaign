using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    public partial class CampaignController
    {
        readonly Dictionary<string,Pawn> taskPawns=new Dictionary<string,Pawn>();
        public void RegisterTaskPawn(Pawn pawn){taskPawns[pawn.ThingID]=pawn;}
        public bool QueueBossPhase(Map owner,BossRecord record,int phaseIndex) {
            var phase=record.Template.Phases[phaseIndex];
            // Empty phases need no campaign action runner, including standalone Bosses.
            if(phase.OnEnter.Count==0){if(phase.LockHealth&&phase.AutoUnlock&&record.Phase==phaseIndex)record.HealthLocked=false;return true;}
            if(!Active||Map!=owner||engine==null||EntryPending||Transitioning)return false;
            var steps=Json.Copy(phase.OnEnter);
            if(phase.LockHealth&&phase.AutoUnlock)steps.Add(new Act{Kind="BossPhaseComplete",Target=record.Id,Text=record.Proxy.ThingID,Value=phaseIndex});
            engine.Enqueue(steps,source:record.Id);return true;
        }
        public Pawn ResolveTriggerPawn(string id) {
            if(string.IsNullOrEmpty(id))return null;
            if(id.StartsWith("role:"))id=id.Substring(5);
            if(Actors.TryGetValue(id,out var actor))return actor;
            if(Objects.TryGetValue(id,out var thing))return thing as Pawn;
            if(taskPawns.TryGetValue(id,out actor))return actor;
            string role=TaskCatalog.For(this).Role(id);
            if(role!=null&&Actors.TryGetValue(role,out actor))return actor;
            // Resolve serialized instance references once, including pawns still inside pods.
            // A task tick must not scan every wall/terrain placement to resolve a soldier.
            foreach(var value in Objects.Values)if(value is Pawn pawn)RegisterTaskPawn(pawn);
            if(taskPawns.TryGetValue(id,out actor))return actor;
            actor=Map?.mapPawns.AllPawnsSpawned.FirstOrDefault(p=>p.ThingID==id);
            if(actor!=null)RegisterTaskPawn(actor);return actor;
        }
        bool TriggerEnabled(Rule rule)=>State.Phase=="Running"&&
            (State.Enabled.TryGetValue(rule.Id,out var enabled)?enabled:rule.Enabled)&&
            !(rule.Once&&State.Firings.ContainsKey(rule.Id));
        RuleMemory TriggerMemory(Rule rule,Pawn host) {
            string id=host?.ThingID??"";
            if(!State.Triggers.TryGetValue(rule.Id,out var memory)||memory.HostId!=id)
                State.Triggers[rule.Id]=memory=new RuleMemory{HostId=id};
            return memory;
        }
        string TriggerSource(Pawn pawn) => Actors.FirstOrDefault(p=>p.Value==pawn).Key??
            Objects.FirstOrDefault(p=>p.Value==pawn).Key??pawn.ThingID;
        public void RecordTriggerDamage(Pawn pawn,double actualDamage) {
            if(!Active||Transitioning||EntryPending||pawn.MapHeld!=Map||actualDamage<=0)return;
            foreach(var rule in MapSpec.Rules.Where(r=>TriggerEnabled(r)&&r.Events.Any(e=>e.Kind=="DamageWindow")&&!string.IsNullOrEmpty(r.Host)))
                if(ResolveTriggerPawn(rule.Host)==pawn)TriggerMemory(rule,pawn).AddDamage(Find.TickManager.TicksGame,actualDamage);
        }
        public void PollTriggers() {
            if(!Active||Map==null||Transitioning||EntryPending)return;
            List<Pawn> candidates=null;
            foreach(var rule in MapSpec.Rules) {
                if(!TriggerEnabled(rule)){State.Triggers.Remove(rule.Id);continue;}
                var host=ResolveTriggerPawn(rule.Host);
                var memory=TriggerMemory(rule,host);
                if(host!=null) {
                    if(host.Dead&&!memory.Dead)engine?.EmitForRule(rule.Id,"Death",TriggerSource(host));
                    if(host.Downed&&!host.Dead&&!memory.Downed)engine?.EmitForRule(rule.Id,"Downed",TriggerSource(host));
                    memory.Dead=host.Dead;memory.Downed=host.Downed&&!host.Dead;
                    if(rule.Events.Any(e=>e.Kind=="DamageWindow")&&memory.ConsumeDamage(Find.TickManager.TicksGame,rule.DamageWindowSeconds,rule.DamageThreshold))
                        engine?.EmitForRule(rule.Id,"DamageWindow",TriggerSource(host));
                }
                if(!rule.Events.Any(e=>e.Kind=="RangeEnter"||e.Kind=="RangeLeave"))continue;
                if(!string.IsNullOrEmpty(rule.Host)&&(host==null||!host.Spawned||host.Map!=Map||host.Dead)){memory.Inside.Clear();continue;}
                var center=host?.Position??new IntVec3(rule.Position.X,0,rule.Position.Z);
                if(candidates==null)candidates=Map.mapPawns.AllPawnsSpawned.Where(p=>!p.Dead).ToList();
                var seen=new HashSet<string>();
                foreach(var pawn in candidates) {
                    if(pawn==host||!TriggerMatches(rule,pawn))continue;
                    string id=pawn.ThingID;seen.Add(id);
                    bool inside=TriggerGeometry.Contains(rule,center.x,center.z,pawn.Position.x,pawn.Position.z);
                    bool was=memory.Inside.TryGetValue(id,out var old)&&old;
                    if(inside!=was)engine?.EmitForRule(rule.Id,inside?"RangeEnter":"RangeLeave",TriggerSource(pawn));
                    memory.Inside[id]=inside;
                }
                // Disappearance/death is not a movement out of the radius.
                foreach(var id in memory.Inside.Keys.Where(id=>!seen.Contains(id)).ToList())memory.Inside.Remove(id);
            }
        }
        bool TriggerMatches(Rule rule,Pawn pawn){
            switch(rule.Detect){
                case "AnyPawn":return true;
                case "Hostile":return pawn.HostileTo(Faction.OfPlayer);
                case "Friendly":return pawn.Faction==Faction.OfPlayer||pawn.Faction?.RelationKindWith(Faction.OfPlayer)==FactionRelationKind.Ally;
                case "Specific":return ResolveTriggerPawn(rule.Subject)==pawn;
                default:return pawn.Faction==Faction.OfPlayer&&(pawn.RaceProps.Humanlike||pawn.RaceProps.IsMechanoid);
            }
        }
        IEnumerable<IntVec3> TriggerCells(string target) {
            var region=MapSpec.Regions.FirstOrDefault(r=>r.Id==target);
            if(region!=null) {
                for(int x=region.Min.X;x<=region.Max.X;x++)for(int z=region.Min.Z;z<=region.Max.Z;z++)yield return new IntVec3(x,0,z);
            } else {
                var placement=MapSpec.Objects.FirstOrDefault(p=>p.Id==target)??throw new InvalidOperationException("Missing terrain target: "+target);
                yield return new IntVec3(placement.Position.X,0,placement.Position.Z);
            }
        }
    }
    [HarmonyPatch(typeof(Pawn),nameof(Pawn.PostApplyDamage))]
    static class TriggerPawnDamagePatch
    {
        static void Postfix(Pawn __instance,float totalDamageDealt) =>
            CampaignController.ForMap(__instance.MapHeld)?.RecordTriggerDamage(__instance,totalDamageDealt);
    }
    // Natural rock walls are normally compressed into a grid and lose their Thing IDs.
    // Script targets must remain referenceable after save/load.
    [HarmonyPatch(typeof(CompressibilityDecider),"DetermineReferences")]
    static class TriggerObjectSavePatch
    {
        static void Postfix(Map ___map,HashSet<Thing> ___referencedThings) {
            var controller=CampaignController.ForMap(___map);
            if(controller==null||!controller.Active)return;
            foreach(var thing in controller.Objects.Values)
                if(thing!=null&&thing.Spawned&&thing.Map==___map)___referencedThings.Add(thing);
        }
    }
}
