using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    // Reusable PawnKind appearance constraints; omitted fields keep generated values.
    public sealed class PawnAppearanceExtension : DefModExtension
    {
        public BodyTypeDef body;
        public HeadTypeDef head;
        public HairDef hair;
        public BeardDef beard;
        public GeneDef skin;
        public GeneDef hairColor;
        public TattooDef faceTattoo;
        public TattooDef bodyTattoo;
        public void Apply(Pawn pawn)
        {
            if(pawn?.story==null)return;
            foreach(var desired in new[]{skin,hairColor}.Where(g=>g!=null)){
                if(pawn.genes!=null){
                    foreach(var gene in pawn.genes.GenesListForReading.Where(g=>g.def.endogeneCategory==desired.endogeneCategory&&g.def!=desired).ToList())pawn.genes.RemoveGene(gene);
                    if(!pawn.genes.HasEndogene(desired))pawn.genes.AddGene(desired,false);
                }
            }
            if(body!=null)pawn.story.bodyType=body;
            if(head!=null)pawn.story.headType=head;
            if(hair!=null)pawn.story.hairDef=hair;
            if(skin?.skinColorBase!=null){pawn.story.SkinColorBase=skin.skinColorBase.Value;pawn.story.skinColorOverride=skin.skinColorBase.Value;}
            if(hairColor?.hairColorOverride!=null)pawn.story.HairColor=hairColor.hairColorOverride.Value;
            if(pawn.style!=null){
                if(beard!=null)pawn.style.beardDef=beard;
                if(faceTattoo!=null)pawn.style.FaceTattoo=faceTattoo;
                if(bodyTattoo!=null)pawn.style.BodyTattoo=bodyTattoo;
            }
            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
        }
    }
    [HarmonyPatch(typeof(PawnGenerator),nameof(PawnGenerator.GeneratePawn),new[]{typeof(PawnGenerationRequest)})]
    static class PawnAppearanceGeneration
    {
        static void Postfix(Pawn __result)=>__result?.kindDef.GetModExtension<PawnAppearanceExtension>()?.Apply(__result);
    }
}
