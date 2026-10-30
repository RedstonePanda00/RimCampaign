using System;
using HarmonyLib;
using RimCampaign.Core;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    public static class CampaignItems
    {
        [ThreadStatic] internal static bool InitializingBook;
        static readonly AccessTools.FieldRef<Book,string> Title=AccessTools.FieldRefAccess<Book,string>("title");
        static readonly AccessTools.FieldRef<Book,string> Flavor=AccessTools.FieldRefAccess<Book,string>("descriptionFlavor");
        static readonly AccessTools.FieldRef<Book,bool> Invalidate=AccessTools.FieldRefAccess<Book,bool>("descCanBeInvalidated");
        public static void Validate(ThingDef def,BookText text)
        {
            if(text!=null&&!typeof(Book).IsAssignableFrom(def.thingClass))throw new InvalidOperationException(RimCampaign.Runtime.RcText.T("书名与正文只能用于书籍：")+def.defName);
        }
        public static Thing Make(ThingDef def,ThingDef stuff=null,BookText text=null)
        {
            Validate(def,text);
            bool previous=InitializingBook;InitializingBook=previous||typeof(Book).IsAssignableFrom(def.thingClass);
            try{
            var item=ThingMaker.MakeThing(def,stuff);
            InitializeBook(item);
            if(item is Book book&&text!=null){
                if(!string.IsNullOrEmpty(text.Title))Title(book)=RcText.T(text.Title);
                if(!string.IsNullOrEmpty(text.Text))Flavor(book)=RcText.T(text.Text);
                // Let the native description builder retain the generated reading benefits.
                Invalidate(book)=true;
                _=book.DescriptionDetailed;
            }
            return item;
            }finally{InitializingBook=previous;}
        }
        public static void InitializeBook(Thing item)
        {
            if(!(item is Book book)||!string.IsNullOrEmpty(book.Title))return;
            bool previous=InitializingBook;InitializingBook=true;
            try{
            var quality=book.TryGetComp<CompQuality>();
            if(quality!=null)quality.SetQuality(QualityUtility.GenerateQualityRandomEqualChance(),ArtGenerationContext.Outsider);
            else book.GenerateBook();
            }finally{InitializingBook=previous;}
        }
        public static bool Matches(Thing item,DeviceLoot row)=>item.def.defName==row.Def.Name&&
            (row.Book==null||item is Book book&&
                (string.IsNullOrEmpty(row.Book.Title)||(book.Title==row.Book.Title||book.Title==RcText.T(row.Book.Title)))&&
                (string.IsNullOrEmpty(row.Book.Text)||(BookText.Normalize(book.FlavorUI)==BookText.Normalize(row.Book.Text)||BookText.Normalize(book.FlavorUI)==BookText.Normalize(RcText.T(row.Book.Text)))));
    }
    // Book grammar creates a temporary random author. That author must not create
    // family members for authored campaign actors while generating decorative text.
    [HarmonyPatch(typeof(PawnGenerator),nameof(PawnGenerator.GeneratePawn),new[]{typeof(PawnGenerationRequest)})]
    static class CampaignBookAuthorRelations
    {
        static void Prefix(ref PawnGenerationRequest request){if(CampaignItems.InitializingBook)request.CanGeneratePawnRelations=false;}
    }
}
