using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public static class EditorUI
    {
        public static readonly string[] Actions={"Wait","Sequence","Parallel","If","Group","Set","Add","CampaignSet","Objective","Timer","Enable","Win","Fail","Spawn","SpawnGroup","Destroy","RemoveTerrain","UnlockBoss","Despawn","Move","Camera","Fade","Subtitle","Dialogue","Sound","Message"};
        public static readonly string[] Events={"Entry","Elapsed","Timer","Death","Downed","DamageWindow","RangeEnter","RangeLeave","Enter","Leave","Objective","Variable","BossPhase","BossDefeated","BossRemoved"};
        public static readonly string[] Conditions={"Always","And","Or","Not","Equal","NotEqual","Greater","AtLeast","Less","AtMost"};
        public static string Label(object x) {
            if(x is Act a)return (a.Name==""?a.Kind:a.Name)+" "+a.Target+" "+a.Text;
            if(x is EventSpec e)return e.Kind+" "+e.Target+(e.Kind=="Elapsed"?" "+e.Seconds+"s":"");
            if(x is Condition c)return c.Op+" "+c.Key+" "+c.Value;
            if(x is DefRef d)return d.Kind+": "+d.Name;
            if(x is Named n)return string.IsNullOrEmpty(n.Name)?n.Id:n.Name;
            return x?.ToString()??"(none)";
        }
        public static string FieldLabel(string name) {
            switch(name){case "Host":return "宿主";case "Once":return "触发方式";case "Radius":return "触发半径（格）";case "DamageWindowSeconds":return "累计受伤时间（秒）";case "DamageThreshold":return "累计伤害阈值";case "SpawnAtStart":return "开局生成";case "LockHealth":return "进入阶段时锁血";case "AutoUnlock":return "阶段动作结束后解锁";case "OnEnter":return "进入阶段动作";case "HealthFraction":return "阶段血量比例（0–1）";default:return name;}
        }
        public static string HostLabel(MapSpec map,string host) {
            if(string.IsNullOrEmpty(host))return "无宿主";
            if(host.StartsWith("role:"))return "角色："+host.Substring(5);
            var p=map?.Objects.FirstOrDefault(o=>o.Id==host);return p==null?"宿主不存在："+host:p.Name+" ["+p.Kind+"]";
        }
        public static object Clone(object value) => typeof(Json).GetMethod("Copy").MakeGenericMethod(value.GetType()).Invoke(null,new[]{value});
        public static void Error(Exception ex) {Log.Error("[RimCampaign Editor] "+ex);Find.WindowStack.Add(new Dialog_MessageBox(ex.GetBaseException().Message));}
        public static void Pick(IEnumerable<KeyValuePair<string,string>> choices,Action<string> set) {
            Find.WindowStack.Add(new SearchWindow(choices.ToList(),set));
        }
        public static void Pick(IEnumerable<string> choices,Action<string> set)=>Pick(choices.Select(x=>new KeyValuePair<string,string>(x,x)),set);
        public static IEnumerable<Def> Defs(string type) {
            switch(type) {
                case "TerrainDef":return DefDatabase<TerrainDef>.AllDefs.Cast<Def>();
                case "RoofDef":return DefDatabase<RoofDef>.AllDefs.Cast<Def>();
                case "SoundDef":return DefDatabase<SoundDef>.AllDefs.Cast<Def>();
                case "PawnKindDef":return DefDatabase<PawnKindDef>.AllDefs.Cast<Def>();
                case "AbilityDef":return DefDatabase<AbilityDef>.AllDefs.Cast<Def>();
                default:return DefDatabase<ThingDef>.AllDefs.Cast<Def>();
            }
        }
    }
    public class SearchWindow : Window
    {
        readonly List<KeyValuePair<string,string>> choices;readonly Action<string> select;string query="";Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(600,650);
        public SearchWindow(List<KeyValuePair<string,string>> choices,Action<string> select){this.choices=choices;this.select=select;doCloseX=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect r){
            query=Widgets.TextField(new Rect(0,0,r.width-30,30),query);
            var list=choices.Where(x=>x.Value.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0||x.Key.IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToList();
            Widgets.BeginScrollView(new Rect(0,40,r.width,r.height-88),ref scroll,new Rect(0,0,r.width-20,list.Count*34));
            for(int i=0;i<list.Count;i++)if(Widgets.ButtonText(new Rect(0,i*34,r.width-24,30),list[i].Value)){select(list[i].Key);Close();}
            Widgets.EndScrollView();
            if(Widgets.ButtonText(new Rect(0,r.height-40,r.width,36),"返回"))Close();
        }
    }
    public class ObjectWindow : Window
    {
        readonly object draft;readonly Action<object> confirm;readonly Project project;readonly MapSpec mapContext;Vector2 scroll;
        readonly Dictionary<string,string> buffers=new Dictionary<string,string>();
        public override Vector2 InitialSize=>new Vector2(780,720);
        public ObjectWindow(object value,Action<object> confirm,Project project,MapSpec map=null){draft=EditorUI.Clone(value);this.confirm=confirm;this.project=project;mapContext=draft as MapSpec??(draft is Node n?project.Maps.FirstOrDefault(m=>m.Id==n.Map):map);doCloseX=true;absorbInputAroundWindow=true;forcePause=true;}
        public override void DoWindowContents(Rect rect) {
            Widgets.Label(new Rect(0,0,rect.width-40,35),EditorUI.Label(draft)+(draft is Rule rule?"  |  "+EditorUI.HostLabel(mapContext,rule.Host):""));
            var properties=draft.GetType().GetProperties().Where(p=>p.CanRead&&p.CanWrite&&p.Name!="Id"&&!(draft is MapSpec&&p.Name=="FactionRelations")).ToArray();
            Widgets.BeginScrollView(new Rect(0,40,rect.width,rect.height-100),ref scroll,new Rect(0,0,rect.width-20,properties.Length*44+10));
            for(int i=0;i<properties.Length;i++) {
                var p=properties[i];var row=new Rect(0,i*44,rect.width-24,38);
                Widgets.Label(new Rect(row.x,row.y,165,row.height),EditorUI.FieldLabel(p.Name));
                Field(p,new Rect(170,row.y,row.width-174,row.height));
            }
            Widgets.EndScrollView();
            if(Widgets.ButtonText(new Rect(0,rect.height-45,rect.width*.66f,40),"应用并返回")){try{confirm(draft);Close();}catch(Exception ex){EditorUI.Error(ex);}}
            if(Widgets.ButtonText(new Rect(rect.width*.68f,rect.height-45,rect.width*.32f,40),"取消并返回"))Close();
        }
        void Field(PropertyInfo p,Rect r) {
            var value=p.GetValue(draft);var type=p.PropertyType;
            if(type==typeof(bool)) {if(draft is Rule&&p.Name=="Once"){if(Widgets.ButtonText(r,(bool)value?"一次性":"持久（可重复）"))p.SetValue(draft,!(bool)value);return;}bool b=(bool)value;Widgets.CheckboxLabeled(r,"",ref b);p.SetValue(draft,b);return;}
            if(type==typeof(string)) {
                var text=value as string??"";
                if(draft is Rule && p.Name=="Host") {
                    if(Widgets.ButtonText(r,EditorUI.HostLabel(mapContext,text))&&mapContext!=null)
                        EditorUI.Pick(new[]{new KeyValuePair<string,string>("","无宿主")}
                            .Concat(mapContext.Roles.Select(role=>new KeyValuePair<string,string>("role:"+role,"角色："+role)))
                            .Concat(mapContext.Objects.Where(o=>new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind)).Select(o=>new KeyValuePair<string,string>(o.Id,o.Name+" ["+o.Kind+"]"))),id=>p.SetValue(draft,id));
                    return;
                }
                if(draft is Act groupAction&&p.Name=="Target"&&groupAction.Kind=="SpawnGroup"&&mapContext!=null) {
                    if(Widgets.ButtonText(r,string.IsNullOrEmpty(text)?"选择放置组":text))EditorUI.Pick(mapContext.Objects.Select(o=>o.Group).Where(g=>!string.IsNullOrEmpty(g)).Distinct(),id=>p.SetValue(draft,id));return;
                }
                if(draft is MapFaction team && p.Name=="Side") {
                    if(team.Id=="Player"){Widgets.Label(r,"玩家（盟友队）");return;}
                    if(Widgets.ButtonText(r,text=="Ally"?"盟友":text=="Hostile"?"敌人":"中立"))
                        EditorUI.Pick(new[]{"Ally","Hostile","Neutral"}.Select(k=>new KeyValuePair<string,string>(k,k=="Ally"?"盟友":k=="Hostile"?"敌人":"中立")),id=>p.SetValue(draft,id));
                    return;
                }
                if(draft is Placement placement && p.Name=="Faction") {
                    var map=mapContext??project.Maps.FirstOrDefault(m=>m.Objects.Any(o=>o.Id==placement.Id))??project.Maps.FirstOrDefault();
                    var label=map?.Factions.FirstOrDefault(f=>f.Id==text)?.Name??(text=="Neutral"?"无阵营（旧版）":text);
                    if(Widgets.ButtonText(r,label)&&map!=null)
                        EditorUI.Pick(map.Factions.Select(f=>new KeyValuePair<string,string>(f.Id,f.Name)),id=>p.SetValue(draft,id));
                    return;
                }
                if(draft is Placement && p.Name=="BindRole") {
                    if(Widgets.ButtonText(r,string.IsNullOrEmpty(text)?"不绑定角色（使用模板来源）":text))
                        EditorUI.Pick(new[]{""}.Concat(project.Maps.SelectMany(m=>m.Roles).Distinct()),id=>p.SetValue(draft,id));
                    return;
                }
                if(draft is DefRef def && p.Name=="Name") {
                    if(Widgets.ButtonText(r,text==""?"选择定义 / Choose Def":text)) {
                        var choices=EditorUI.Defs(def.Kind).ToList();
                        EditorUI.Pick(choices.Select(d=>new KeyValuePair<string,string>(d.defName,d.LabelCap+" ["+d.defName+"]")),id=>{
                            var selected=choices.Single(d=>d.defName==id);def.Name=id;def.Package=selected.modContentPack?.PackageIdPlayerFacing??"ludeon.rimworld";
                        });
                    }return;
                }
                var options=Options(draft,p.Name);
                if(options!=null) {if(Widgets.ButtonText(r,text))EditorUI.Pick(options,s=>p.SetValue(draft,s));return;}
                var field=r;field.width-=74;
                p.SetValue(draft,Widgets.TextField(field,text));
                if(Widgets.ButtonText(new Rect(r.xMax-70,r.y,70,r.height),"选择")) {
                    var refs=project.Maps.Cast<Named>().Concat(project.Profiles).Concat(project.Bosses??new List<BossTemplate>()).Concat(project.Campaigns)
                        .Concat(project.Maps.SelectMany(m=>m.Objects.Cast<Named>().Concat(m.Regions).Concat(m.Objectives).Concat(m.Rules).Concat(m.Groups)))
                        .Concat(project.Campaigns.SelectMany(c=>c.Nodes));
                    EditorUI.Pick(refs.Select(n=>new KeyValuePair<string,string>(n.Id,n.Name+" ["+n.Id.Substring(0,Math.Min(8,n.Id.Length))+"]")),s=>p.SetValue(draft,s));
                }return;
            }
            if(type.IsPrimitive||type==typeof(decimal)) {
                if(!buffers.TryGetValue(p.Name,out var buffer))buffer=Convert.ToString(value,CultureInfo.InvariantCulture);
                buffer=Widgets.TextField(r,buffer);buffers[p.Name]=buffer;
                try{p.SetValue(draft,Convert.ChangeType(buffer,type,CultureInfo.InvariantCulture));}catch(FormatException){}catch(OverflowException){}return;
            }
            if(value is IList list) {
                if(Widgets.ButtonText(r,"编辑列表 / Edit list ("+list.Count+")"))Find.WindowStack.Add(new ListWindow(list,project,mapContext));
                return;
            }
            if(value is IDictionary dictionary) {
                if(Widgets.ButtonText(r,"编辑绑定 / Edit bindings ("+dictionary.Count+")"))Find.WindowStack.Add(new DictionaryWindow(dictionary,project));
                return;
            }
            if(Widgets.ButtonText(r,EditorUI.Label(value))) {
                if(value==null)value=Activator.CreateInstance(type);
                Find.WindowStack.Add(new ObjectWindow(value,x=>p.SetValue(draft,x),project,mapContext));
            }
        }
        static IEnumerable<string> Options(object obj,string name) {
            if(obj is Condition&&name=="Op")return EditorUI.Conditions;
            if(obj is EventSpec&&name=="Kind")return EditorUI.Events.Concat(Modules.All.Where(m=>m.Category=="Event").Select(m=>m.Id));
            if(obj is Act&&name=="Kind")return EditorUI.Actions.Concat(Modules.All.Where(m=>m.Category=="Action").Select(m=>m.Id));
            if(obj is Placement&&name=="Kind")return new[]{"Thing","Terrain","Roof","Pawn","PawnKind","Boss","Marker"};
            if(obj is BossAction&&name=="Module")return Modules.All.Where(m=>m.Category=="BossBehavior").Select(m=>m.Id);

            if(obj is MapSpec&&name=="Base")return new[]{"Blank","Generated"};
            if(obj is Node&&name=="Kind")return new[]{"Story","Open"};
            if(obj is Node&&name=="Arrival")return new[]{"Position","Edge","Pods"}.Concat(Modules.All.Where(m=>m.Category=="Arrival").Select(m=>m.Id));
            if(obj is Objective&&name=="Mode")return new[]{"Standard","Survive"};
            if(obj is HudWidget&&name=="Kind")return new[]{"Text","Counter","Timer","Progress","Objectives"};
            if(obj is DefRef&&name=="Kind")return new[]{"ThingDef","TerrainDef","RoofDef","SoundDef","PawnKindDef","AbilityDef"};
            return null;
        }
    }
    public class ListWindow : Window
    {
        readonly IList list;readonly Project project;readonly MapSpec mapContext;Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(760,650);
        public ListWindow(IList list,Project project,MapSpec map=null){this.list=list;this.project=project;mapContext=map;doCloseX=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect r) {
            Widgets.Label(new Rect(0,0,r.width-40,30),"有序列表 / Ordered list");
            var itemType=list.GetType().GetGenericArguments()[0];
            if(Widgets.ButtonText(new Rect(0,35,r.width,32),"+ 添加 / Add"))list.Add(itemType==typeof(string)?"":Activator.CreateInstance(itemType));
            Widgets.BeginScrollView(new Rect(0,76,r.width,r.height-122),ref scroll,new Rect(0,0,r.width-20,list.Count*42));
            for(int i=0;i<list.Count;i++) {
                int index=i;var item=list[i];float y=i*42;
                if(itemType==typeof(string))list[i]=Widgets.TextField(new Rect(0,y,r.width-160,34),(string)item);
                else if(Widgets.ButtonText(new Rect(0,y,r.width-160,34),EditorUI.Label(item)))Find.WindowStack.Add(new ObjectWindow(item,x=>list[index]=x,project,mapContext));
                if(Widgets.ButtonText(new Rect(r.width-155,y,40,34),"↑")&&i>0){var t=list[i-1];list[i-1]=list[i];list[i]=t;}
                if(Widgets.ButtonText(new Rect(r.width-110,y,40,34),"↓")&&i<list.Count-1){var t=list[i+1];list[i+1]=list[i];list[i]=t;}
                if(Widgets.ButtonText(new Rect(r.width-65,y,40,34),"×")){list.RemoveAt(i);break;}
            }Widgets.EndScrollView();
            if(Widgets.ButtonText(new Rect(0,r.height-40,r.width,36),"返回"))Close();
        }
    }
    public class DictionaryWindow : Window
    {
        readonly IDictionary map;readonly Project project;string key="";Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(700,600);
        public DictionaryWindow(IDictionary map,Project project){this.map=map;this.project=project;doCloseX=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect r) {
            key=Widgets.TextField(new Rect(0,0,r.width-100,32),key);var type=map.GetType().GetGenericArguments()[1];
            if(Widgets.ButtonText(new Rect(r.width-95,0,90,32),"添加")&&!string.IsNullOrWhiteSpace(key)&&!map.Contains(key))map.Add(key,type==typeof(string)?"":Activator.CreateInstance(type));
            var keys=map.Keys.Cast<string>().ToList();
            Widgets.BeginScrollView(new Rect(0,45,r.width,r.height-92),ref scroll,new Rect(0,0,r.width-20,keys.Count*44));
            for(int i=0;i<keys.Count;i++){
                string k=keys[i];float y=i*44;
                Widgets.Label(new Rect(0,y,180,34),k);
                var value=Widgets.TextField(new Rect(180,y,r.width-315,34),Convert.ToString(map[k],CultureInfo.InvariantCulture));
                try{map[k]=Convert.ChangeType(value,type,CultureInfo.InvariantCulture);}catch(FormatException){}catch(OverflowException){}
                if(type==typeof(string)&&Widgets.ButtonText(new Rect(r.width-130,y,65,34),"角色"))EditorUI.Pick(project.Profiles.Select(p=>new KeyValuePair<string,string>(p.Id,p.Name)),s=>map[k]=s);
                if(Widgets.ButtonText(new Rect(r.width-60,y,40,34),"×")){map.Remove(k);break;}
            }Widgets.EndScrollView();
            if(Widgets.ButtonText(new Rect(0,r.height-40,r.width,36),"返回"))Close();
        }
    }
}
