using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime
{
    public static class PlacementStorage
    {
        public static bool Supported(ThingDef def)=>def!=null&&(typeof(Building_Bookcase).IsAssignableFrom(def.thingClass)||typeof(Building_Storage).IsAssignableFrom(def.thingClass)||typeof(Building_Crate).IsAssignableFrom(def.thingClass));
        public static bool Allows(ThingDef host,ThingDef item) {
            if(!Supported(host)||item==null||item.category!=ThingCategory.Item)return false;
            if(typeof(Building_Bookcase).IsAssignableFrom(host.thingClass)&&!typeof(Book).IsAssignableFrom(item.thingClass))return false;
            return host.building?.fixedStorageSettings?.filter?.Allows(item)??true;
        }
        public static ThingDef StuffFor(ThingDef def,DefRef value) {
            if(!def.MadeFromStuff)return null;
            if(value==null||string.IsNullOrEmpty(value.Name))return GenStuff.DefaultStuffFor(def);
            var stuff=DefDatabase<ThingDef>.GetNamedSilentFail(value.Name);
            if(stuff?.stuffProps?.CanMake(def)!=true)throw new InvalidOperationException(def.LabelCap+RimCampaign.Runtime.RcText.T(" 不支持材质：")+value.Name);
            return stuff;
        }
        public static void Validate(Placement p) {
            if(p.Kind!="Thing")return;
            var host=DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name)??throw new InvalidOperationException(RimCampaign.Runtime.RcText.T("找不到物品定义：")+p.Def.Name+RimCampaign.Runtime.RcText.T("。请检查所需模组是否已启用。"));
            StuffFor(host,p.Stuff);
            CampaignItems.Validate(host,p.Book);
            PlacementGraphics.Validate(p,host);
            if(p.Contents==null||p.Contents.Count==0)return;
            if(!Supported(host))throw new InvalidOperationException(host.LabelCap+RimCampaign.Runtime.RcText.T(" 不支持预置内容物。"));
            int stacks=0;
            foreach(var row in p.Contents) {
                var item=DefDatabase<ThingDef>.GetNamedSilentFail(row.Def?.Name??"");
                if(!Allows(host,item))throw new InvalidOperationException(host.LabelCap+RimCampaign.Runtime.RcText.T(" 不能存放：")+(row.Def?.Name??RimCampaign.Runtime.RcText.T("未选择物品")));
                if(row.Count<1||row.Count>10000)throw new InvalidOperationException(RimCampaign.Runtime.RcText.T("物品数量必须为 1–10000。"));
                StuffFor(item,row.Stuff);
                CampaignItems.Validate(item,row.Book);
                stacks+=(row.Count+item.stackLimit-1)/item.stackLimit;
            }
            if(!typeof(Building_Crate).IsAssignableFrom(host.thingClass)&&stacks>host.building.maxItemsInCell*host.size.Area)
                throw new InvalidOperationException(host.LabelCap+RimCampaign.Runtime.RcText.T(" 内容物超出容量：")+stacks+RimCampaign.Runtime.RcText.T(" 堆 / ")+host.building.maxItemsInCell*host.size.Area+RimCampaign.Runtime.RcText.T(" 堆。"));
        }
        public static void Populate(Thing host,Placement p) {
            if(p.Contents==null||p.Contents.Count==0)return;
            Validate(p);
            foreach(var row in p.Contents) {
                var def=DefDatabase<ThingDef>.GetNamed(row.Def.Name);int remaining=row.Count;
                if(host is IStoreSettingsParent store)store.GetStoreSettings().filter.SetAllow(def,true);
                while(remaining>0) {
                    var item=CampaignItems.Make(def,StuffFor(def,row.Stuff),row.Book);
                    item.stackCount=Math.Min(remaining,def.stackLimit);remaining-=item.stackCount;
                    bool accepted=false;
                    if(host is Building_Bookcase bookcase)accepted=bookcase.GetDirectlyHeldThings().TryAdd(item);
                    else if(host is Building_Crate crate)accepted=crate.TryAcceptThing(item);
                    else if(host is Building_Storage shelf) {
                        foreach(var cell in shelf.AllSlotCells()) {
                            if(cell.GetThingList(host.Map).Count(t=>t.def.category==ThingCategory.Item)>=host.def.building.maxItemsInCell)continue;
                            GenSpawn.Spawn(item,cell,host.Map);accepted=true;break;
                        }
                    }
                    if(!accepted){item.Destroy();throw new InvalidOperationException(host.LabelCap+RimCampaign.Runtime.RcText.T(" 无法容纳指定内容物，请检查容量和重叠布置。"));}
                }
            }
        }
    }
}
