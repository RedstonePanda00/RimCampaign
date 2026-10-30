using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.SDK;
using RimWorld;
using Verse;

namespace RimCampaign.Runtime {
    // Generation owns the stock. Runtime scripts transfer these same instances.
    public sealed class MapRewardStock : IExposable, IThingHolder {
        public string Id;
        public bool Collected;
        ThingOwner<Thing> items;
        public MapRewardStock(){items=new ThingOwner<Thing>(this);}
        public IThingHolder ParentHolder=>null;
        public ThingOwner GetDirectlyHeldThings()=>items;
        public void GetChildHolders(List<IThingHolder> children)=>ThingOwnerUtility.AppendThingHoldersFromThings(children,items);
        public void ExposeData(){Scribe_Values.Look(ref Id,"id");Scribe_Values.Look(ref Collected,"collected");Scribe_Deep.Look(ref items,"items",this);}
    }
    public partial class CampaignController {
        public List<MapRewardStock> RewardStocks=new List<MapRewardStock>();
        void GenerateMapRewards(){
            foreach(var stock in RewardStocks)stock.GetDirectlyHeldThings().ClearAndDestroyContents();
            RewardStocks.Clear();
            foreach(var spec in MapSpec.Rewards){
                var stock=new MapRewardStock{Id=spec.Id};RewardStocks.Add(stock);
                foreach(var row in spec.Contents){
                    var def=DefDatabase<ThingDef>.GetNamed(row.Def.Name);
                    if(def.category!=ThingCategory.Item||row.Count<1)throw new InvalidOperationException("Invalid map reward: "+spec.Id);
                    int remaining=row.Count;
                    while(remaining>0){var item=CampaignItems.Make(def,PlacementStorage.StuffFor(def,row.Stuff),row.Book);item.stackCount=Math.Min(remaining,def.stackLimit);remaining-=item.stackCount;stock.GetDirectlyHeldThings().TryAdd(item,false);}
                }
            }
        }
        public bool CollectMapReward(string id,string source=""){
            var spec=MapSpec.Rewards.Single(r=>r.Id==id);var stock=RewardStocks.Single(r=>r.Id==id);
            if(stock.Collected)return true;
            if(!string.IsNullOrEmpty(spec.StockpileMarker)){
                var marker=MapSpec.Objects.Single(o=>o.Id==spec.StockpileMarker);
                var at=new IntVec3(marker.Position.X,0,marker.Position.Z);
                var zone=at.GetZone(Map) as Zone_Stockpile;
                if(zone==null){zone=new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile,Map.zoneManager);Map.zoneManager.RegisterZone(zone);zone.AddCell(at);}
                foreach(var item in stock.GetDirectlyHeldThings().ToList()){
                    stock.GetDirectlyHeldThings().Remove(item);
                    if(!GenPlace.TryPlaceThing(item,at,Map,ThingPlaceMode.Near,out var placed)){stock.GetDirectlyHeldThings().TryAdd(item,false);return false;}
                    if(placed.Position.GetZone(Map)==null)zone.AddCell(placed.Position);
                    placed.SetForbidden(false,false);
                }
                stock.Collected=true;return true;
            }
            var receiver=string.IsNullOrEmpty(spec.Recipient)?Map.mapPawns.AllPawnsSpawned.FirstOrDefault(p=>p.ThingID==source):Actors[spec.Recipient];
            if(receiver?.inventory==null||receiver.Dead||receiver.Downed)return false;
            var given=new List<Thing>();
            foreach(var item in stock.GetDirectlyHeldThings().ToList()){
                stock.GetDirectlyHeldThings().Remove(item);
                if(receiver.inventory.innerContainer.TryAdd(item,false)){given.Add(item);continue;}
                stock.GetDirectlyHeldThings().TryAdd(item,false);
                foreach(var previous in given){receiver.inventory.innerContainer.Remove(previous);stock.GetDirectlyHeldThings().TryAdd(previous,false);}
                return false;
            }
            stock.Collected=true;return true;
        }
    }
    [CampaignModule("rc.collect","Action","领取地图生成时已初始化的奖励；Target 奖励 ID，重复触发不会再次发放")]
    public sealed class CollectMapReward : IActionModule {
        public bool Execute(Act action,IHost host,string source,double delta)=>((CampaignController)host).CollectMapReward(action.Target,source);
    }
}
