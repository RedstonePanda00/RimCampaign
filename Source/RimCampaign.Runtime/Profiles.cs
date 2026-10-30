using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Runtime
{
    public class ItemData
    {
        public string Def { get; set; } public string Stuff { get; set; }
        public int Count { get; set; } = 1; public int HitPoints { get; set; }
        public int Quality { get; set; } = -1; public float[] Color { get; set; }
    }
    public class SkillData { public string Def { get; set; } public int Level { get; set; } public int Passion { get; set; } public float Xp { get; set; } }
    public class TraitData { public string Def { get; set; } public int Degree { get; set; } }
    public class GeneData { public string Def { get; set; } public bool Xeno { get; set; } }
    public class HealthData { public string Def { get; set; } public string Part { get; set; } public float Severity { get; set; } public bool Permanent { get; set; } }
    public class PawnData
    {
        public string Kind { get; set; } = "Colonist"; public string First { get; set; } public string Nick { get; set; } public string Last { get; set; }
        public int Gender { get; set; } public long Age { get; set; } public long Chronological { get; set; }
        public string Childhood { get; set; } public string Adulthood { get; set; }
        public string Body { get; set; } public string Head { get; set; } public string Hair { get; set; }
        public float[] HairColor { get; set; } public float[] SkinColor { get; set; }
        public string Beard { get; set; } public string FaceTattoo { get; set; } public string BodyTattoo { get; set; }
        public string Xenotype { get; set; }
        public List<TraitData> Traits { get; set; } = new List<TraitData>();
        public List<SkillData> Skills { get; set; } = new List<SkillData>();
        public List<GeneData> Genes { get; set; } = new List<GeneData>();
        public List<HealthData> Health { get; set; } = new List<HealthData>();
        public List<ItemData> Apparel { get; set; } = new List<ItemData>();
        public List<ItemData> Equipment { get; set; } = new List<ItemData>();
        public List<ItemData> Inventory { get; set; } = new List<ItemData>();
        public List<string> Abilities { get; set; } = new List<string>();
        public Dictionary<string,int> AbilityCooldowns { get; set; } = new Dictionary<string,int>();
        public float Psyfocus { get; set; }
    }
    public static class Profiles
    {
        static float[] ColorData(Color c) => new[] { c.r, c.g, c.b, c.a };
        static Color ColorValue(float[] a) => a == null || a.Length != 4 ? Color.white : new Color(a[0],a[1],a[2],a[3]);
        public static DefRef Ref(Def def) => def == null ? null : new DefRef { Kind = def.GetType().Name, Name = def.defName, Package = def.modContentPack?.PackageIdPlayerFacing ?? "ludeon.rimworld" };
        public static string PartPath(BodyPartRecord part) => part == null ? "" : PartPath(part.parent) + "/" + part.def.defName + ":" + (part.parent?.parts.IndexOf(part) ?? 0);
        public static Profile Capture(Pawn pawn)
        {
            var p = new Profile { Name = pawn.LabelShort };
            void Add(Def def) { var r = Ref(def); if (r != null && !p.References.Any(x => x.Kind == r.Kind && x.Name == r.Name)) p.References.Add(r); }
            string D(Def def) { Add(def); return def?.defName; }
            var name = pawn.Name as NameTriple;
            var data = new PawnData { Kind = D(pawn.kindDef), First = name?.First ?? pawn.LabelShort, Nick = name?.Nick ?? pawn.LabelShort, Last = name?.Last ?? "",
                Gender = (int)pawn.gender, Age = pawn.ageTracker.AgeBiologicalTicks, Chronological = pawn.ageTracker.AgeChronologicalTicks };
            Add(pawn.def);
            if (pawn.story != null) {
                data.Childhood = D(pawn.story.Childhood); data.Adulthood = D(pawn.story.Adulthood);
                data.Body = D(pawn.story.bodyType); data.Head = D(pawn.story.headType); data.Hair = D(pawn.story.hairDef);
                data.HairColor = ColorData(pawn.story.HairColor); data.SkinColor = ColorData(pawn.story.SkinColor);
                data.Traits = pawn.story.traits.allTraits.Select(t => new TraitData { Def = D(t.def), Degree = t.Degree }).ToList();
            }
            if (pawn.skills != null) data.Skills = pawn.skills.skills.Select(s => new SkillData { Def=D(s.def),Level=s.Level,Passion=(int)s.passion,Xp=s.xpSinceLastLevel }).ToList();
            if (pawn.style != null) { data.Beard=D(pawn.style.beardDef);data.FaceTattoo=D(pawn.style.FaceTattoo);data.BodyTattoo=D(pawn.style.BodyTattoo); }
            if (pawn.genes != null) {
                data.Xenotype=D(pawn.genes.Xenotype);
                data.Genes=pawn.genes.GenesListForReading.Select(g=>new GeneData{Def=D(g.def),Xeno=pawn.genes.Xenogenes.Contains(g)}).ToList();
            }
            ItemData Item(Thing t) {
                var quality=t.TryGetComp<CompQuality>(); var color=t.TryGetComp<CompColorable>();
                // Additional stateful components require a registered portable adapter rather than silent loss.
                if (t is ThingWithComps twc) foreach (var comp in twc.AllComps)
                    if (comp.GetType().Assembly != typeof(Pawn).Assembly) p.PortabilityErrors.Add("Unsupported item component: " + comp.GetType().FullName);
                return new ItemData{Def=D(t.def),Stuff=D(t.Stuff),Count=t.stackCount,HitPoints=t.HitPoints,Quality=quality==null?-1:(int)quality.Quality,Color=color==null?null:ColorData(color.Color)};
            }
            data.Apparel=pawn.apparel?.WornApparel.Select(Item).ToList()??new List<ItemData>();
            data.Equipment=pawn.equipment?.AllEquipmentListForReading.Select(Item).ToList()??new List<ItemData>();
            data.Inventory=pawn.inventory?.innerContainer.Select(Item).ToList()??new List<ItemData>();
            foreach (var h in pawn.health.hediffSet.hediffs) {
                if (h.GetType().Assembly != typeof(Pawn).Assembly) p.PortabilityErrors.Add("Unsupported hediff: " + h.GetType().FullName);
                data.Health.Add(new HealthData{Def=D(h.def),Part=PartPath(h.Part),Severity=h.Severity,Permanent=h.TryGetComp<HediffComp_GetsPermanent>()?.IsPermanent==true});
            }
            foreach (var comp in pawn.AllComps.Where(c=>c.GetType().Assembly!=typeof(Pawn).Assembly))
                p.PortabilityErrors.Add("Unsupported pawn component: "+comp.GetType().FullName);
            if(pawn.relations!=null)foreach(var relation in pawn.relations.DirectRelations){
                p.Relations.Add(new RelationRef{Relation=D(relation.def),TargetName=relation.otherPawn.LabelShort});
            }
            if (pawn.royalty?.AllTitlesForReading.Count>0) p.PortabilityErrors.Add("Royal titles require a world-faction binding.");
            if (pawn.abilities != null) {
                data.Psyfocus=pawn.psychicEntropy?.CurrentPsyfocus??0;
                data.Abilities = pawn.abilities.abilities.Select(a => D(a.def)).ToList();
                foreach(var ability in pawn.abilities.abilities) {
                    data.AbilityCooldowns[ability.def.defName]=ability.CooldownTicksRemaining;
                    if(ability.GetType().Assembly!=typeof(Pawn).Assembly)p.PortabilityErrors.Add("Unsupported custom ability state: "+ability.GetType().FullName);
                }
            }
            p.Payload=Json.Write(data); return p;
        }
        static T Def<T>(string name) where T : Def => string.IsNullOrEmpty(name) ? null : DefDatabase<T>.GetNamed(name);
        public static Pawn Create(Profile profile, Faction faction = null)
        {
            if (profile.GenerateFrom != null) return Generate(profile.GenerateFrom, faction);
            if (profile.PortabilityErrors.Count>0) throw new InvalidOperationException(string.Join("\n",profile.PortabilityErrors));
            var d=Json.Read<PawnData>(profile.Payload);
            var request = new PawnGenerationRequest(Def<PawnKindDef>(d.Kind), faction, PawnGenerationContext.NonPlayer, forceGenerateNewPawn:true, canGeneratePawnRelations:false, colonistRelationChanceFactor:0, fixedGender:(Gender)d.Gender);
            var pawn=PawnGenerator.GeneratePawn(request);
            pawn.Name=new NameTriple(d.First,d.Nick,d.Last); pawn.ageTracker.AgeBiologicalTicks=d.Age; pawn.ageTracker.AgeChronologicalTicks=d.Chronological;
            if(pawn.genes!=null) {
                if(!string.IsNullOrEmpty(d.Xenotype))pawn.genes.SetXenotype(Def<XenotypeDef>(d.Xenotype));
                foreach(var g in pawn.genes.GenesListForReading.ToList()) pawn.genes.RemoveGene(g);
                foreach(var g in d.Genes) pawn.genes.AddGene(Def<GeneDef>(g.Def),g.Xeno);
            }
            if(pawn.story!=null) {
                pawn.story.Childhood=Def<BackstoryDef>(d.Childhood);pawn.story.Adulthood=Def<BackstoryDef>(d.Adulthood);
                pawn.story.bodyType=Def<BodyTypeDef>(d.Body);pawn.story.headType=Def<HeadTypeDef>(d.Head);pawn.story.hairDef=Def<HairDef>(d.Hair);
                pawn.story.HairColor=ColorValue(d.HairColor);pawn.story.skinColorOverride=ColorValue(d.SkinColor);
                pawn.story.traits.allTraits.Clear();foreach(var t in d.Traits) pawn.story.traits.GainTrait(new Trait(Def<TraitDef>(t.Def),t.Degree));
            }
            if(pawn.style!=null) { pawn.style.beardDef=Def<BeardDef>(d.Beard);pawn.style.FaceTattoo=Def<TattooDef>(d.FaceTattoo);pawn.style.BodyTattoo=Def<TattooDef>(d.BodyTattoo); }
            if(pawn.skills!=null) foreach(var s in d.Skills) {var t=pawn.skills.GetSkill(Def<SkillDef>(s.Def));t.Level=s.Level;t.passion=(Passion)s.Passion;t.xpSinceLastLevel=s.Xp;}
            Thing Item(ItemData x) {
                var t=ThingMaker.MakeThing(Def<ThingDef>(x.Def),Def<ThingDef>(x.Stuff));t.stackCount=x.Count;
                if(t.def.useHitPoints)t.HitPoints=Math.Max(1,Math.Min(x.HitPoints,t.MaxHitPoints));
                if(x.Quality>=0)t.TryGetComp<CompQuality>()?.SetQuality((QualityCategory)x.Quality,ArtGenerationContext.Outsider);
                CampaignItems.InitializeBook(t);
                if(x.Color!=null)t.TryGetComp<CompColorable>()?.SetColor(ColorValue(x.Color));return t;
            }
            pawn.apparel?.DestroyAll();pawn.equipment?.DestroyAllEquipment();pawn.inventory?.innerContainer.ClearAndDestroyContents();
            foreach(var x in d.Apparel)pawn.apparel.Wear((Apparel)Item(x),false);
            foreach(var x in d.Equipment)pawn.equipment.AddEquipment((ThingWithComps)Item(x));
            foreach(var x in d.Inventory)pawn.inventory.innerContainer.TryAdd(Item(x));
            if (d.Abilities != null && pawn.abilities != null) foreach (var ability in d.Abilities) pawn.abilities.GainAbility(Def<AbilityDef>(ability));
            if(d.AbilityCooldowns!=null&&pawn.abilities!=null)foreach(var cooldown in d.AbilityCooldowns) {
                var ability=pawn.abilities.abilities.FirstOrDefault(a=>a.def.defName==cooldown.Key);
                if(ability!=null&&cooldown.Value>0)ability.StartCooldown(cooldown.Value);
            }
            foreach(var h in pawn.health.hediffSet.hediffs.ToList())pawn.health.RemoveHediff(h);
            foreach(var x in d.Health) {
                var part=string.IsNullOrEmpty(x.Part)?null:pawn.RaceProps.body.AllParts.FirstOrDefault(b=>PartPath(b)==x.Part);
                if(!string.IsNullOrEmpty(x.Part)&&part==null)throw new InvalidOperationException("Missing body part: "+x.Part);
                var h=HediffMaker.MakeHediff(Def<HediffDef>(x.Def),pawn,part);h.Severity=x.Severity;
                var permanent=h.TryGetComp<HediffComp_GetsPermanent>();if(permanent!=null)permanent.IsPermanent=x.Permanent;
                pawn.health.AddHediff(h);
            }
            if(pawn.psychicEntropy!=null)pawn.psychicEntropy.OffsetPsyfocusDirectly(d.Psyfocus-pawn.psychicEntropy.CurrentPsyfocus);
            return pawn;
        }
        public static Pawn Generate(DefRef kind, Faction faction = null)
        {
            if (kind == null || kind.Kind != "PawnKindDef" || string.IsNullOrWhiteSpace(kind.Name)) throw new InvalidOperationException("Select a PawnKindDef.");
            return PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed(kind.Name), faction,
                PawnGenerationContext.NonPlayer, forceGenerateNewPawn:true, canGeneratePawnRelations:false, colonistRelationChanceFactor:0));
        }
    }
}
