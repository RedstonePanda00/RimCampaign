using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using Verse;
using UnityEngine;
using System.Runtime.CompilerServices;

namespace RimCampaign.Editor {
    public sealed class NotesWindow:Window {
        string text;readonly Action<string> apply;
        public override Vector2 InitialSize=>new Vector2(650,430);
        public NotesWindow(string value,Action<string> apply){text=value??"";this.apply=apply;doCloseX=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect r){EditorUI.DrawLabel(new Rect(0,0,r.width,34),RcText.T("备注仅供地图作者阅读，不参与游戏逻辑。"));text=Widgets.TextArea(new Rect(0,42,r.width,r.height-94),text);if(EditorUI.Button(new Rect(0,r.height-42,r.width,36),RcText.T("保存备注"))){apply(text);Close();}}
    }
    public static partial class EditorUI {
        static readonly ConditionalWeakTable<MapSpec,Dictionary<string,Named>> referenceNames=new ConditionalWeakTable<MapSpec,Dictionary<string,Named>>();
        internal static void InvalidateReferences(MapSpec map){if(map!=null)referenceNames.Remove(map);}
        internal static readonly Dictionary<string,string> ReadableFields=new Dictionary<string,string>{
            {"Arrival","抵达方式 / Arrival"},{"ShowOnMap","显示地图触发器 / Show map handle"},{"Origin","出生位置 / Spawn origin"},{"Notes","备注（仅编辑器说明）"},{"RelatedObjects","作者关联对象"},{"Kind","类型"},{"Def","定义"},{"Stuff","材质"},{"Role","剧情角色"},{"BindRole","继承战役角色"},{"Group","放置组"},{"Layer","编辑图层"},{"Boss","首领模板"},
            {"Actions","执行动作"},{"When","执行条件"},{"Steps","顺序动作"},{"Else","否则执行"},{"Target","目标 / 状态变量"},{"Text","文本 / 参数"},{"Value","数值"},{"Freeze","暂停游戏时间"},
            {"Label","玩家可见操作名称"},{"OneShot","仅可操作一次"},{"BlockCells","落石阻挡格"},{"ObstacleDef","阻挡物定义"},{"EscapeCell","必须保留通路的撤离格"},{"DestroyHost","操作后销毁宿主"},
            {"Pawn","绑定角色"},{"Marker","地图路点"},{"Op","判断方式"},{"Key","状态变量"},{"Children","组合条件"},{"Seconds","秒数"}
        };
        static readonly Dictionary<string,string> ActionNames=new Dictionary<string,string>{
            {"rc.buildOutpost","工兵施工 / Build outpost"},{"WaitUntil","等待条件 / Await condition"},{"rc.task.move","前往路点 / Walk to waypoint"},{"rc.task.operate","接触操作 / Operate building"},{"rc.task.engage","交战清场 / Clear defenders"},{"rc.task.signal","发送事件 / Emit event"},{"rc.reinforce","周期补员 / Reinforce"},{"rc.defendGroup","编组驻守 / Defend point"},
            {"Set","设置状态"},{"Add","增加状态"},{"CampaignSet","设置战役状态"},{"Communication","播放通讯"},{"Dialogue","播放对话"},{"Message","显示消息"},{"Subtitle","显示字幕"},
            {"Wait","等待"},{"Sequence","依次执行"},{"Parallel","同时执行"},{"Cinematic","接管演出"},{"If","条件分支"},{"Group","执行公共动作组"},{"Enable","启用 / 禁用触发器"},{"Objective","更新目标"},
            {"Spawn","生成对象"},{"Destroy","销毁对象"},{"Despawn","移除对象"},{"Move","移动角色"},{"Camera","移动镜头"},{"Fade","画面淡入淡出"},{"Win","任务完成"},{"Fail","任务失败"},{"Timer","设置计时器"},{"Sound","播放音效"},{"PasswordPrompt","输入密码"},
            {"campaign.collect-reward","领取奖励"},{"campaign.warp-building","折跃建筑"},
            {"rc.collect","领取奖励"},{"rc.pmcWarpBuilding","折跃建筑"},{"rc.pmcGunSmithTutorial","开始枪匠教学"},{"rc.sequenceLock","记录谜题顺序"},{"rc.escort","开始引路"},{"rc.hediff","添加状态效果"},{"rc.bossActive","启用 / 停用首领"},{"rc.effect","播放特效"},{"rc.fire","命令角色射击"},{"rc.fireAtWill","设置自由开火"},{"rc.openingTitle","显示开场行动文字"},
            {"pmp.m2.brief","播放沙盘简报"},{"pmp.m2.setup","初始化第二关"},{"pmp.m2.banner","显示任务阶段标题"},{"pmp.m2.focus","聚焦演出目标"},{"pmp.m2.identity","揭示骑士身份"}
        };
        public static string ActionTitle(string kind)=>ActionNames.TryGetValue(kind,out var name)?RcText.T(name):RcText.T(RimCampaign.SDK.Modules.All.FirstOrDefault(m=>m.Id==kind)?.Sentence??kind);
        public static bool RelevantField(object value,string field){
            if(value is Act structure&&structure.Kind=="rc.spawnStructure")return new[]{"Name","Notes","Kind","Target","Origin","Arrival","Text"}.Contains(field);
            if(value is Act arrivalField&&field=="Arrival"&&arrivalField.Kind!="rc.reinforce"&&arrivalField.Kind!="rc.spawnStructure")return false;
            if(value is Act task&&(task.Kind.StartsWith("rc.task.")||task.Kind=="rc.reinforce"||task.Kind=="rc.defendGroup"||task.Kind=="rc.buildOutpost"||task.Kind=="WaitUntil")){
                if(new[]{"Name","Notes","Kind"}.Contains(field))return true;
                if(task.Kind=="WaitUntil")return field=="When";
                if(field=="When")return task.Kind=="rc.task.engage";
                if(field=="Arrival")return task.Kind=="rc.reinforce";
                if(field=="Origin")return task.Kind=="rc.reinforce"||task.Kind=="rc.buildOutpost";
                if(field=="Target")return true;
                if(field=="Value")return task.Kind=="rc.task.move"||task.Kind=="rc.task.operate"||task.Kind=="rc.reinforce"||task.Kind=="rc.defendGroup";
                if(field=="Text")return task.Kind=="rc.buildOutpost"||task.Kind=="rc.task.signal"||task.Kind=="rc.reinforce"||task.Kind=="rc.defendGroup";
                return false;
            }
            if(!(value is Placement p))return true;
            bool pawn=p.Kind=="Pawn"||p.Kind=="PawnKind"||p.Kind=="Boss",thing=p.Kind=="Thing";
            if(p.Kind=="Marker")return new[]{"Name","Notes","RelatedObjects","Position","Rotation","Layer","Group"}.Contains(field);
            if(field=="Stuff")return thing&&DefDatabase<ThingDef>.GetNamedSilentFail(p.Def?.Name??"")?.MadeFromStuff==true;
            if(field=="GraphicIndex")return thing&&PlacementGraphics.Variants(DefDatabase<ThingDef>.GetNamedSilentFail(p.Def?.Name??""))!=null;
            if(field=="Contents")return thing&&PlacementStorage.Supported(DefDatabase<ThingDef>.GetNamedSilentFail(p.Def?.Name??""));
            if(field=="Book")return thing&&DefDatabase<ThingDef>.GetNamedSilentFail(p.Def?.Name??"")?.thingClass!=null&&typeof(Book).IsAssignableFrom(DefDatabase<ThingDef>.GetNamedSilentFail(p.Def.Name).thingClass);
            if(field=="Role"||field=="BindRole")return pawn;
            if(field=="Boss")return p.Kind=="Boss";
            if(field=="Count")return thing&&DefDatabase<ThingDef>.GetNamedSilentFail(p.Def?.Name??"")?.stackLimit>1;
            if(field=="Faction")return pawn||thing;
            if(field=="Def")return p.Kind!="Pawn"&&p.Kind!="Boss";
            return true;
        }
        public static string Reference(MapSpec map,string id){
            if(string.IsNullOrEmpty(id))return RcText.T("未指定");
            if(map==null)return id;
            var lookup=referenceNames.GetValue(map,m=>{var names=new Dictionary<string,Named>();foreach(var n in m.Objects.Cast<Named>().Concat(m.Rules).Concat(m.Objectives).Concat(m.Groups).Concat(m.PatrolRoutes).Concat(m.Stealth.Routes).Concat(m.Interactions).Concat(m.Rewards))if(!names.ContainsKey(n.Id))names.Add(n.Id,n);return names;});
            lookup.TryGetValue(id,out var value);
            return value==null?id:RcText.T(string.IsNullOrEmpty(value.Name)?value.Id:value.Name);
        }
        public static string Describe(object value,MapSpec map){
            if(value is Act a){
                string name=RcText.T(string.IsNullOrEmpty(a.Name)?ActionTitle(a.Kind):a.Name);
                if(a.Kind=="Communication")return name+" · "+RcText.T(a.Communication?.SpeakerName??a.Communication?.Speaker??"")+"："+RcText.T(a.Text);
                if(a.Kind=="Set"||a.Kind=="Add"||a.Kind=="CampaignSet")return name+" · "+Reference(map,a.Target)+(a.Kind=="Add"?" + ":" = ")+a.Value;
                if(a.Kind=="Wait")return name+" · "+a.Value+RcText.T(" 秒");
                if(a.Kind=="WaitUntil"||a.Kind=="If")return name+" · "+Describe(a.When,map);
                if(a.Kind=="rc.task.operate")return name+" · "+Reference(map,a.Target)+" · "+a.Value+RcText.T(" 秒");
                return name+(string.IsNullOrEmpty(a.Target)?"":" · "+Reference(map,a.Target))+(string.IsNullOrEmpty(a.Text)?"":" · "+RcText.T(a.Text));
            }
            if(value is EventSpec e)return EventLabel(e.Kind)+(string.IsNullOrEmpty(e.Target)?"":" · "+Reference(map,e.Target))+(e.Kind=="Elapsed"||e.Kind=="Periodic"?" · "+e.Seconds+RcText.T(" 秒"):"");
            if(value is Condition c){
                if(c.Op=="Always")return RcText.T("无附加条件");
                if(c.Op=="And"||c.Op=="Or")return string.Join(c.Op=="And"?RcText.T(" 且 "):RcText.T(" 或 "),c.Children.Select(x=>Describe(x,map)));
                if(c.Op=="Not")return RcText.T("不满足：")+string.Join(", ",c.Children.Select(x=>Describe(x,map)));
                if(c.Op=="rc.near")return RcText.T("接近：")+string.Join(" → ",c.Key.Split('|').Select(k=>Reference(map,k)))+" ≤ "+c.Value+RcText.T(" 格");
                if(c.Op=="rc.escortArrived")return RcText.T("带路角色已到达撤离点");
                if(c.Op=="rc.targetsCleared")return "清除对象组 / Clear group: "+c.Key;
                var symbol=c.Op=="Equal"?" = ":c.Op=="NotEqual"?" ≠ ":c.Op=="AtLeast"?" ≥ ":c.Op=="Greater"?" > ":c.Op=="Less"?" < ":c.Op=="AtMost"?" ≤ ":" "+c.Op+" ";
                return c.Key+symbol+c.Value;
            }
            if(value is CombatPatrolSpec b)return Reference(map,b.Pawn)+" → "+Reference(map,b.Route);
            if(value is SceneInteractionSpec s)return RcText.T(s.Label)+" · "+Reference(map,s.Host);
            return Label(value);
        }
    }
}
