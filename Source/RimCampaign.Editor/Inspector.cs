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
    [StaticConstructorOnStartup]
    public static partial class EditorUI
    {
        static Texture2D roundCorner;
        public static void Rounded(Rect rect,Color color,float radius=4){
            if(roundCorner==null){roundCorner=new Texture2D(32,32){filterMode=FilterMode.Bilinear};var pixels=new Color[1024];for(int y=0;y<32;y++)for(int x=0;x<32;x++){float distance=Vector2.Distance(new Vector2(x+.5f,y+.5f),new Vector2(16,16));pixels[y*32+x]=new Color(1,1,1,Mathf.Clamp01(16-distance));}roundCorner.SetPixels(pixels);roundCorner.Apply();}
            radius=Math.Min(radius,Math.Min(rect.width,rect.height)/2);Widgets.DrawBoxSolid(new Rect(rect.x+radius,rect.y,rect.width-radius*2,rect.height),color);Widgets.DrawBoxSolid(new Rect(rect.x,rect.y+radius,rect.width,rect.height-radius*2),color);
            var old=GUI.color;GUI.color=color;
            GUI.DrawTextureWithTexCoords(new Rect(rect.x,rect.y,radius,radius),roundCorner,new Rect(0,.5f,.5f,.5f));GUI.DrawTextureWithTexCoords(new Rect(rect.xMax-radius,rect.y,radius,radius),roundCorner,new Rect(.5f,.5f,.5f,.5f));
            GUI.DrawTextureWithTexCoords(new Rect(rect.x,rect.yMax-radius,radius,radius),roundCorner,new Rect(0,0,.5f,.5f));GUI.DrawTextureWithTexCoords(new Rect(rect.xMax-radius,rect.yMax-radius,radius,radius),roundCorner,new Rect(.5f,0,.5f,.5f));GUI.color=old;
        }
        public static bool Button(Rect rect,string label,bool background=true){
            if(rect.width<=0||rect.height<=0)return false;
            bool hover=Mouse.IsOver(rect)&&GUI.enabled;
            if(hover){Rounded(rect,new Color(.29f,.32f,.36f));Rounded(rect.ContractedBy(1),new Color(.105f,.12f,.14f),3);}
            else if(background)Rounded(rect,new Color(.155f,.175f,.20f));
            var oldFont=Text.Font;var oldAnchor=Text.Anchor;Text.Font=GameFont.Tiny;Text.Anchor=TextAnchor.MiddleCenter;
            string shown=RcText.T(label??"");
            if(Text.CalcSize(shown).x>rect.width-14){TooltipHandler.TipRegion(rect,shown);while(shown.Length>1&&Text.CalcSize(shown+"…").x>rect.width-14)shown=shown.Substring(0,shown.Length-1);shown+="…";}
            EditorUI.DrawLabel(rect,shown);Text.Font=oldFont;Text.Anchor=oldAnchor;
            return GUI.enabled&&Widgets.ButtonInvisible(rect,true);
        }
        public static void DrawLabel(Rect rect,string text)=>Widgets.Label(rect,RcText.T(text));
        public static string Serialize(object value)=>(string)typeof(Json).GetMethod("Write").MakeGenericMethod(value.GetType()).Invoke(null,new[]{value});
        public static string EventLabel(string kind)=>kind=="RangeEnter"?RimCampaign.Runtime.RcText.T("进入检测范围"):kind=="RangeLeave"?RimCampaign.Runtime.RcText.T("离开检测范围"):kind=="Entry"?RimCampaign.Runtime.RcText.T("地图开始"):kind=="Death"?RimCampaign.Runtime.RcText.T("宿主死亡"):kind=="Downed"?RimCampaign.Runtime.RcText.T("宿主倒地"):kind=="Elapsed"?RimCampaign.Runtime.RcText.T("经过时间"):kind=="Signal"?RimCampaign.Runtime.RcText.T("收到信号"):kind;
        public static readonly string[] Actions={"WaitUntil","Wait","Sequence","Cinematic","Parallel","If","Group","Set","Add","CampaignSet","Objective","Timer","Enable","Win","Fail","Spawn","SpawnGroup","Destroy","RemoveTerrain","UnlockBoss","Despawn","Move","Camera","Fade","Subtitle","Dialogue","Sound","Message","Noise","PasswordPrompt","SoundPulse","Communication"};
        public static readonly string[] Events={"Periodic","Entry","Elapsed","Timer","Death","Downed","DamageWindow","RangeEnter","RangeLeave","Enter","Leave","Objective","Variable","BossPhase","BossDefeated","BossRemoved","Password","PasswordRejected","NoiseHeard","Noise","CameraDetected","CameraCaptured","UplinkDisabled","GuardState","BodyFound","Subdued","InteractionCompleted","Signal","IntelLeaked","SoundHeard"};
        public static readonly string[] Conditions={"Always","And","Or","Not","Equal","NotEqual","Greater","AtLeast","Less","AtMost"};
        public static string Label(object x) {
            if(x is BookText book)return string.IsNullOrEmpty(book.Title)?RimCampaign.Runtime.RcText.T("原版生成书籍文本"):RcText.T(book.Title);
            if(x is Act||x is EventSpec||x is Condition)return Describe(x,null);
            if(x is DefRef d)return d.Kind+": "+d.Name;
            if(x is Named n)return string.IsNullOrEmpty(n.Name)?n.Id:RcText.T(n.Name);
            if(x is PatrolPoint point)return "("+point.Position.X+", "+point.Position.Z+RimCampaign.Runtime.RcText.T(") 朝向 ")+point.Facing+" / "+(point.Checkpoint?RimCampaign.Runtime.RcText.T("检查点，停留 ")+point.WaitTicks+" ticks":RimCampaign.Runtime.RcText.T("经过点"));
            if(x is StealthSpec)return RimCampaign.Runtime.RcText.T("潜行设置：巡逻、感知、设备");
            return x?.ToString()??"(none)";
        }
        public static string FieldLabel(object owner,string name){
            if(owner is Objective&&name=="VisibleWhen")return RcText.T("显示条件 / Visibility condition");
            if(owner is BossTemplate&&name=="Components")return RcText.T("可选 Boss 组件");
            if(owner is BossComponentSpec&&name=="DamageExceptions")return RcText.T("不压缩的伤害类型");
            if(owner is Act action){
                if(action.Kind=="rc.task.engage"&&name=="When")return RcText.T("清场后的完成条件 / Completion gate");
                if(action.Kind=="rc.buildOutpost"){
                    if(name=="Target")return RcText.T("工兵放置组 / Engineer group");
                    if(name=="Text")return RcText.T("施工放置组 / Blueprint group");
                    if(name=="Origin")return RcText.T("驻地中心 / Outpost center");
                }
            }
            return FieldLabel(name);
        }
        public static string FieldLabel(string name) {
            if(ReadableFields.TryGetValue(name,out var readable))return RcText.T(readable);
            if(name=="RefillNeedsOnSuccess")return RimCampaign.Runtime.RcText.T("通关补满饮食、休息与娱乐");
            switch(name){case "Name":return RimCampaign.Runtime.RcText.T("名称");case "Enabled":return RimCampaign.Runtime.RcText.T("启用");case "Position":return RimCampaign.Runtime.RcText.T("位置 X / Z");case "Events":return RimCampaign.Runtime.RcText.T("事件");case "When":return RimCampaign.Runtime.RcText.T("条件");case "Actions":return RimCampaign.Runtime.RcText.T("执行动作");case "RangeShape":return RimCampaign.Runtime.RcText.T("检测范围形状");case "RangeWidth":return RimCampaign.Runtime.RcText.T("矩形宽度（格）");case "RangeHeight":return RimCampaign.Runtime.RcText.T("矩形高度（格）");case "Detect":return RimCampaign.Runtime.RcText.T("检测对象");case "Subject":return RimCampaign.Runtime.RcText.T("指定检测角色");case "Rotation":return RimCampaign.Runtime.RcText.T("朝向 0北1东2南3西");case "Faction":return RimCampaign.Runtime.RcText.T("阵营");case "Count":return RimCampaign.Runtime.RcText.T("数量");}
            if(name=="Book")return RimCampaign.Runtime.RcText.T("书籍文本（留空使用原版生成）");
            if(name=="GraphicIndex")return RimCampaign.Runtime.RcText.T("固定贴图款式");
            if(name=="Recipient")return RimCampaign.Runtime.RcText.T("奖励接收角色（空为事件操作者）");
            if(name=="Rewards")return RimCampaign.Runtime.RcText.T("地图生成奖励（仅生成一次）");
            if(name=="Components")return RimCampaign.Runtime.RcText.T("挂载的地图组件");
            if(name=="CombatPatrols")return RimCampaign.Runtime.RcText.T("普通战斗巡逻绑定");
            if(name=="PatrolRoutes")return RimCampaign.Runtime.RcText.T("普通巡逻路线");
            if(name=="KeepForExtraction")return RimCampaign.Runtime.RcText.T("取得后必须随队携带");
            if(name=="PartialCoverConcealsFromCalmGuards")return RimCampaign.Runtime.RcText.T("部分掩体遮蔽未警觉守卫");
            if(name=="PlayerCornerPeek")return RimCampaign.Runtime.RcText.T("玩家贴角探视（一格）");
            if(name=="PlayerHearing")return RimCampaign.Runtime.RcText.T("听声辨位（视野半径内隔墙显示敌人）");
            if(name=="BackgroundOpacity")return RimCampaign.Runtime.RcText.T("背景不透明度 0–1（默认 0.28）");
            if(name=="Spectrum")return RimCampaign.Runtime.RcText.T("通讯频谱条");
            if(name=="BarCount")return RimCampaign.Runtime.RcText.T("频谱柱数量（3–256）");
            if(name=="SimulateSpeech")return RimCampaign.Runtime.RcText.T("文字通讯使用模拟频谱");
            if(CommunicationLabels.TryGetValue(name,out var communicationLabel))return communicationLabel;
            if(name=="SoundPulse")return RimCampaign.Runtime.RcText.T("声音范围触发配置");
            if(name=="Receivers")return RimCampaign.Runtime.RcText.T("接收对象类型");
            if(name=="RespectWalls")return RimCampaign.Runtime.RcText.T("遮挡与门的衰减");
            if(name=="GuardReaction")return RimCampaign.Runtime.RcText.T("守卫同时响应声源");
            if(name=="Dangerous")return RimCampaign.Runtime.RcText.T("危险声响");
            if(StealthLabels.TryGetValue(name,out var label))return label;
            switch(name){case "Host":return RimCampaign.Runtime.RcText.T("宿主");case "Once":return RimCampaign.Runtime.RcText.T("触发方式");case "Radius":return RimCampaign.Runtime.RcText.T("触发半径（格）");case "DamageWindowSeconds":return RimCampaign.Runtime.RcText.T("累计受伤时间（秒）");case "DamageThreshold":return RimCampaign.Runtime.RcText.T("累计伤害阈值");case "SpawnAtStart":return RimCampaign.Runtime.RcText.T("开局生成");case "LockHealth":return RimCampaign.Runtime.RcText.T("进入阶段时锁血");case "AutoUnlock":return RimCampaign.Runtime.RcText.T("阶段动作结束后解锁");case "OnEnter":return RimCampaign.Runtime.RcText.T("进入阶段动作");case "HealthFraction":return RimCampaign.Runtime.RcText.T("阶段血量比例（0–1）");default:return name;}
        }
        static readonly Dictionary<string,string> CommunicationLabels=new Dictionary<string,string>{
            {"Communication",RimCampaign.Runtime.RcText.T("自动剧情通讯")},{"Speaker",RimCampaign.Runtime.RcText.T("说话者（无头像时渲染角色）")},{"SpeakerName",RimCampaign.Runtime.RcText.T("显示名称（可覆盖角色名）")},{"Portrait",RimCampaign.Runtime.RcText.T("头像预设")},{"PortraitPath",RimCampaign.Runtime.RcText.T("自定义头像路径")},{"PortraitPackage",RimCampaign.Runtime.RcText.T("头像所属 packageId")},{"CropX",RimCampaign.Runtime.RcText.T("裁剪左侧 0–1")},{"CropY",RimCampaign.Runtime.RcText.T("裁剪顶部 0–1")},{"CropWidth",RimCampaign.Runtime.RcText.T("裁剪宽度 0–1")},{"CropHeight",RimCampaign.Runtime.RcText.T("裁剪高度 0–1")},{"Duration",RimCampaign.Runtime.RcText.T("自动显示秒数")},{"PortraitSize",RimCampaign.Runtime.RcText.T("头像尺寸")},{"Renderer",RimCampaign.Runtime.RcText.T("通讯渲染模块")},{"RendererPackage",RimCampaign.Runtime.RcText.T("渲染模块 packageId")},{"RendererData",RimCampaign.Runtime.RcText.T("渲染器参数")},{"Contents",RimCampaign.Runtime.RcText.T("箱内实物")}
        };
        static readonly Dictionary<string,string> StealthLabels=new Dictionary<string,string>{
            {"Checkpoint",RimCampaign.Runtime.RcText.T("检查点（关闭则经过不停留）")},{"FarDetectionMultiplier",RimCampaign.Runtime.RcText.T("最远距离辨认速度倍率")},{"FullSuspicionHoldTicks",RimCampaign.Runtime.RcText.T("满怀疑度保持 tick")},{"AutoReport",RimCampaign.Runtime.RcText.T("摄像头自动对外传输")},{"Stealth",RimCampaign.Runtime.RcText.T("潜行系统")},{"DynamicFog",RimCampaign.Runtime.RcText.T("动态视野遮蔽")},{"Actor",RimCampaign.Runtime.RcText.T("行动员角色")},{"PlayerSight",RimCampaign.Runtime.RcText.T("玩家视野（格）")},{"UpdateTicks",RimCampaign.Runtime.RcText.T("感知刷新（tick）")},
            {"Routes",RimCampaign.Runtime.RcText.T("巡逻路线")},{"Guards",RimCampaign.Runtime.RcText.T("守卫控制器")},{"Devices",RimCampaign.Runtime.RcText.T("交互设备")},{"Route",RimCampaign.Runtime.RcText.T("巡逻路线")},{"Pawn",RimCampaign.Runtime.RcText.T("控制的 Pawn")},{"Points",RimCampaign.Runtime.RcText.T("有序路点")},{"PingPong",RimCampaign.Runtime.RcText.T("往返巡逻")},
            {"Facing",RimCampaign.Runtime.RcText.T("朝向 0北1东2南3西")},{"WaitTicks",RimCampaign.Runtime.RcText.T("停留（tick）")},{"SightRange",RimCampaign.Runtime.RcText.T("视距（格）")},{"ViewAngle",RimCampaign.Runtime.RcText.T("视野角度")},{"HearingMultiplier",RimCampaign.Runtime.RcText.T("听觉距离倍率")},
            {"DetectionTicks",RimCampaign.Runtime.RcText.T("辨认所需 tick（守卫近距／摄像头连续）")},{"SearchTicks",RimCampaign.Runtime.RcText.T("搜索持续 tick")},{"SearchRadius",RimCampaign.Runtime.RcText.T("搜索半径")},{"RadioChannel",RimCampaign.Runtime.RcText.T("局部通报频道")},{"RadioRange",RimCampaign.Runtime.RcText.T("通报距离")},{"RadioDelayTicks",RimCampaign.Runtime.RcText.T("通报延迟 tick")},
            {"Uplink",RimCampaign.Runtime.RcText.T("对外通讯设备")},{"ReportTicks",RimCampaign.Runtime.RcText.T("泄密报告所需 tick")},{"UseTicks",RimCampaign.Runtime.RcText.T("交互所需 tick")},{"DestroyHost",RimCampaign.Runtime.RcText.T("封路后销毁交互宿主")},{"StartsCaptured",RimCampaign.Runtime.RcText.T("开局由玩家控制")},{"PasswordRule",RimCampaign.Runtime.RcText.T("密码触发器")},
            {"Password",RimCampaign.Runtime.RcText.T("指定字符（密码）")},{"PasswordCaseSensitive",RimCampaign.Runtime.RcText.T("区分大小写")},{"PasswordTrimInput",RimCampaign.Runtime.RcText.T("忽略输入首尾空白")},{"HearThroughWalls",RimCampaign.Runtime.RcText.T("允许隔墙听到闲谈")},{"AllowTakedown",RimCampaign.Runtime.RcText.T("启用背后制伏")},
            {"ParticipantDistance",RimCampaign.Runtime.RcText.T("闲谈双方最大间距（格）")},
            {"TakedownTicks",RimCampaign.Runtime.RcText.T("制伏所需 tick")},{"GunshotRadius",RimCampaign.Runtime.RcText.T("枪声半径")}};
        public static string HostLabel(MapSpec map,string host) {
            if(string.IsNullOrEmpty(host))return RimCampaign.Runtime.RcText.T("无宿主");
            if(host.StartsWith("role:"))return RimCampaign.Runtime.RcText.T("角色：")+host.Substring(5);
            var p=map?.Objects.FirstOrDefault(o=>o.Id==host);return p==null?RimCampaign.Runtime.RcText.T("宿主不存在：")+host:p.Name+" ["+p.Kind+"]";
        }
        public static object Clone(object value) => typeof(Json).GetMethod("Copy").MakeGenericMethod(value.GetType()).Invoke(null,new[]{value});
        public static void Error(Exception ex) {if(!(ex is System.IO.IOException)&&!(ex is ArgumentException)&&!(ex is UnauthorizedAccessException)&&!(ex is InvalidOperationException))Log.Error("[RimCampaign Editor] "+ex);Find.WindowStack.Add(new Dialog_MessageBox(ex.Message));}
        public static void PickStuff(ThingDef def,Action<DefRef> apply){
            if(!def.MadeFromStuff){apply(null);return;}
            var choices=GenStuff.AllowedStuffsFor(def).ToList();
            Pick(choices.Select(d=>new KeyValuePair<string,string>(d.defName,d.LabelCap+" ["+d.defName+"]")),id=>apply(Profiles.Ref(choices.Single(d=>d.defName==id))));
        }
        public static void Pick(IEnumerable<KeyValuePair<string,string>> choices,Action<string> set) {
            Find.WindowStack.Add(new SearchWindow(choices.ToList(),set));
        }
        public static void Pick(IEnumerable<string> choices,Action<string> set)=>Pick(choices.Select(x=>new KeyValuePair<string,string>(x,x)),set);
        public static IEnumerable<Def> Defs(string type) {
            switch(type) {
                case "CommunicationPortraitDef":return DefDatabase<CommunicationPortraitDef>.AllDefs.Cast<Def>();
                case "TerrainDef":return DefDatabase<TerrainDef>.AllDefs.Cast<Def>();
                case "RoofDef":return DefDatabase<RoofDef>.AllDefs.Cast<Def>();
                case "SoundDef":return DefDatabase<SoundDef>.AllDefs.Cast<Def>();
                case "PawnKindDef":return DefDatabase<PawnKindDef>.AllDefs.Cast<Def>();
                case "AbilityDef":return DefDatabase<AbilityDef>.AllDefs.Cast<Def>();
                case "DamageDef":return DefDatabase<DamageDef>.AllDefs.Cast<Def>();
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
            for(int i=0;i<list.Count;i++)if(EditorUI.Button(new Rect(0,i*34,r.width-24,30),list[i].Value)){EditorFocus.Release();select(list[i].Key);Close();EditorFocus.Release();}
            Widgets.EndScrollView();
            if(EditorUI.Button(new Rect(0,r.height-40,r.width,36),RimCampaign.Runtime.RcText.T("返回")))Close();
        }
    }
    public class ObjectWindow : Window
    {
        public HashSet<string> VisibleFields;
        string appliedSnapshot;
        readonly object draft;readonly Action<object> confirm;readonly Project project;readonly MapSpec mapContext;Vector2 scroll;
        readonly Dictionary<string,string> buffers=new Dictionary<string,string>();
        public override Vector2 InitialSize=>new Vector2(780,720);
        public ObjectWindow(object value,Action<object> confirm,Project project,MapSpec map=null){draft=EditorUI.Clone(value);this.confirm=confirm;this.project=project;mapContext=draft as MapSpec??(draft is Node n?project.Maps.FirstOrDefault(m=>m.Id==n.Map):map);if(draft is StealthSpec stealth&&mapContext!=null){mapContext=Json.Copy(mapContext);mapContext.Stealth=stealth;}doCloseX=true;absorbInputAroundWindow=true;forcePause=true;}
        public override void DoWindowContents(Rect rect) {
            EditorUI.DrawLabel(new Rect(0,0,rect.width-40,35),EditorUI.Label(draft)+(draft is Rule rule?"  |  "+EditorUI.HostLabel(mapContext,rule.Host):""));
            var properties=Properties();
            Widgets.BeginScrollView(new Rect(0,40,rect.width,rect.height-100),ref scroll,new Rect(0,0,rect.width-20,properties.Length*44+10));
            for(int i=0;i<properties.Length;i++) {
                var p=properties[i];var row=new Rect(0,i*44,rect.width-24,38);
                EditorUI.DrawLabel(new Rect(row.x,row.y,165,row.height),EditorUI.FieldLabel(draft,p.Name));
                Field(p,new Rect(170,row.y,row.width-174,row.height));
            }
            Widgets.EndScrollView();
            if(EditorUI.Button(new Rect(0,rect.height-45,rect.width*.66f,40),RimCampaign.Runtime.RcText.T("应用并返回"))){try{confirm(draft);Close();}catch(Exception ex){EditorUI.Error(ex);}}
            if(EditorUI.Button(new Rect(rect.width*.68f,rect.height-45,rect.width*.32f,40),RimCampaign.Runtime.RcText.T("取消并返回")))Close();
        }
        PropertyInfo[] Properties()=>draft.GetType().GetProperties().Where(p=>p.CanRead&&p.CanWrite&&p.Name!="Id"&&EditorUI.RelevantField(draft,p.Name)&&!(draft is StealthSpec&&p.Name=="Enabled")&&!(draft is MapSpec&&p.Name=="FactionRelations")&&(VisibleFields==null||VisibleFields.Contains(p.Name))).Where(p=>{
            if(!(draft is Rule rule))return true;bool spatial=TriggerGeometry.Spatial(rule);
            if(p.Name=="Position")return spatial||rule.ShowOnMap;
            if(new[]{"RangeShape","Detect"}.Contains(p.Name))return spatial;
            if(p.Name=="Subject")return spatial&&rule.Detect=="Specific";
            if(p.Name=="Radius")return spatial&&rule.RangeShape=="Circle";
            if(p.Name=="RangeWidth"||p.Name=="RangeHeight")return spatial&&rule.RangeShape=="Rectangle";
            if(p.Name.StartsWith("Password"))return rule.Events.Any(e=>e.Kind=="Password");
            if(p.Name.StartsWith("Damage"))return rule.Events.Any(e=>e.Kind=="DamageWindow");return true;
        }).OrderBy(p=>p.Name=="Name"?0:p.Name=="Notes"?1:p.Name=="Events"?2:p.Name=="Position"?3:4).ToArray();
        public void DrawEmbedded(Rect area){
            if(appliedSnapshot==null)appliedSnapshot=EditorUI.Serialize(draft);
            var fields=Properties();Widgets.BeginScrollView(area,ref scroll,new Rect(0,0,area.width-18,fields.Length*65+8));
            for(int i=0;i<fields.Length;i++){Text.Font=GameFont.Tiny;EditorUI.DrawLabel(new Rect(0,i*65,area.width-24,25),EditorUI.FieldLabel(draft,fields[i].Name));Text.Font=GameFont.Small;Field(fields[i],new Rect(0,i*65+25,area.width-24,32));}
            Widgets.EndScrollView();string changed=EditorUI.Serialize(draft);if(changed!=appliedSnapshot){try{confirm(EditorUI.Clone(draft));appliedSnapshot=changed;}catch(Exception ex){EditorUI.Error(ex);appliedSnapshot=changed;}}
        }
        void Field(PropertyInfo p,Rect r) {
            var value=p.GetValue(draft);var type=p.PropertyType;
            if(p.Name=="Notes"){if(EditorUI.Button(r,string.IsNullOrWhiteSpace(value as string)?RcText.T("添加用途备注…"):RcText.T("编辑备注：")+(value as string)))Find.WindowStack.Add(new NotesWindow(value as string,text=>p.SetValue(draft,text)));return;}
            if((p.Name=="Facing"||p.Name=="Rotation")&&type==typeof(int)){var labels=new[]{"北","东","南","西"};if(EditorUI.Button(r,RcText.T(labels[Math.Abs((int)value)%4])))EditorUI.Pick(labels.Select((v,i)=>new KeyValuePair<string,string>(i.ToString(),RcText.T(v))),v=>p.SetValue(draft,int.Parse(v)));return;}
            if(p.Name=="Position"&&value is Cell cell){float width=(r.width-8)/2;foreach(var axis in new[]{"X","Z"}){string key="Position."+axis;if(!buffers.TryGetValue(key,out var str))str=(axis=="X"?cell.X:cell.Z).ToString();str=Widgets.TextField(new Rect(r.x+(axis=="X"?0:width+8),r.y,width,r.height),str);buffers[key]=str;if(int.TryParse(str,out var n)&&n>=0&&n<(axis=="X"?(mapContext?.Width??int.MaxValue):(mapContext?.Height??int.MaxValue))){if(axis=="X")cell.X=n;else cell.Z=n;}}return;}
            if(draft is Rule spatialRule){
                if(p.Name=="Events"){
                    if(EditorUI.Button(new Rect(r.x,r.y,r.width-44,r.height),string.Join(" / ",spatialRule.Events.Select(e=>EditorUI.EventLabel(e.Kind)))))EditorUI.Pick(EditorUI.Events.Select(e=>new KeyValuePair<string,string>(e,EditorUI.EventLabel(e))),kind=>spatialRule.Events=new List<EventSpec>{new EventSpec{Kind=kind}});
                    if(EditorUI.Button(new Rect(r.xMax-40,r.y,40,r.height),"…"))Find.WindowStack.Add(new ListWindow(spatialRule.Events,project,mapContext));return;
                }
                if(p.Name=="RangeShape"){if(EditorUI.Button(r,spatialRule.RangeShape=="Circle"?RimCampaign.Runtime.RcText.T("圆形"):RimCampaign.Runtime.RcText.T("矩形")))EditorUI.Pick(new[]{new KeyValuePair<string,string>("Circle",RimCampaign.Runtime.RcText.T("圆形")),new KeyValuePair<string,string>("Rectangle",RimCampaign.Runtime.RcText.T("矩形"))},v=>spatialRule.RangeShape=v);return;}
                if(p.Name=="Detect"){var labels=new Dictionary<string,string>{{"PlayerUnits",RimCampaign.Runtime.RcText.T("玩家人形与机械体")},{"AnyPawn",RimCampaign.Runtime.RcText.T("所有角色")},{"Hostile",RimCampaign.Runtime.RcText.T("敌对角色")},{"Friendly",RimCampaign.Runtime.RcText.T("玩家与盟友")},{"Specific",RimCampaign.Runtime.RcText.T("指定角色")}};if(EditorUI.Button(r,labels.TryGetValue(spatialRule.Detect,out var label)?label:spatialRule.Detect))EditorUI.Pick(labels,v=>spatialRule.Detect=v);return;}
            }
            if(p.Name=="GraphicIndex"&&draft is Placement variantPlacement){
                var variants=PlacementGraphics.Variants(DefDatabase<ThingDef>.GetNamedSilentFail(variantPlacement.Def?.Name??""));
                if(variants==null){EditorUI.DrawLabel(r,RimCampaign.Runtime.RcText.T("此对象没有可选随机款式"));return;}
                if(EditorUI.Button(r,RimCampaign.Runtime.RcText.T("款式 #")+(PlacementGraphics.Index(variantPlacement,variants.SubGraphicsCount)+1)+RimCampaign.Runtime.RcText.T(" · 选择 / 随机固定")))
                    Find.WindowStack.Add(new GraphicVariantWindow(variantPlacement,false,index=>variantPlacement.GraphicIndex=index));
                return;
            }
            if(p.Name=="Stuff"&&(draft is Placement||draft is DeviceLoot)){
                var reference=draft is Placement placed?placed.Def:((DeviceLoot)draft).Def;
                var def=DefDatabase<ThingDef>.GetNamedSilentFail(reference?.Name??"");
                if(def?.MadeFromStuff!=true){EditorUI.DrawLabel(r,RimCampaign.Runtime.RcText.T("此对象无需材质"));return;}
                if(EditorUI.Button(r,((DefRef)value)?.Name??RimCampaign.Runtime.RcText.T("默认材质")))EditorUI.PickStuff(def,stuff=>p.SetValue(draft,stuff));
                return;
            }
            if(p.Name=="Contents"&&draft is Placement storage){
                var def=DefDatabase<ThingDef>.GetNamedSilentFail(storage.Def?.Name??"");
                if(!PlacementStorage.Supported(def)){EditorUI.DrawLabel(r,RimCampaign.Runtime.RcText.T("此对象没有可编辑的储物空间"));return;}
                if(EditorUI.Button(r,RimCampaign.Runtime.RcText.T("编辑初始内容物 (")+(storage.Contents?.Count??0)+")"))Find.WindowStack.Add(new ContentsWindow(storage,copy=>storage.Contents=copy.Contents));
                return;
            }
            if(draft is SoundPulseSpec pulse&&p.Name=="Targets"&&mapContext!=null){
                if(EditorUI.Button(r,RimCampaign.Runtime.RcText.T("指定接收目标（")+pulse.Targets.Count+RimCampaign.Runtime.RcText.T("；空列表表示不限制）"))){
                    var targets=mapContext.Objects.Select(o=>new KeyValuePair<string,string>(o.Id,o.Name)).Concat(mapContext.Roles.Select(role=>new KeyValuePair<string,string>("role:"+role,RimCampaign.Runtime.RcText.T("角色：")+role)));
                    Find.WindowStack.Add(new SoundTargetsWindow(targets.ToList(),pulse.Targets));
                }return;
            }
            if(type==typeof(bool)) {if(draft is Rule&&p.Name=="Once"){if(EditorUI.Button(r,(bool)value?RimCampaign.Runtime.RcText.T("一次性"):RimCampaign.Runtime.RcText.T("持久（可重复）")))p.SetValue(draft,!(bool)value);return;}bool b=(bool)value;Widgets.CheckboxLabeled(r,"",ref b);p.SetValue(draft,b);return;}
            if(type==typeof(string)) {
                var text=value as string??"";
                if(StealthReference(p,r,text))return;
                if(draft is Act arrival&&(arrival.Kind=="rc.reinforce"||arrival.Kind=="rc.spawnStructure")&&p.Name=="Arrival"){if(EditorUI.Button(r,text=="Pods"?"空投舱 / Drop pods":"地面 / Ground"))EditorUI.Pick(new[]{new KeyValuePair<string,string>("Ground","地面 / Ground"),new KeyValuePair<string,string>("Pods","空投舱 / Drop pods")},id=>p.SetValue(draft,id));return;}
                if(draft is Act structure&&structure.Kind=="rc.spawnStructure"){
                    if(p.Name=="Target"){if(EditorUI.Button(r,"结构 / Structure: "+text))EditorUI.Pick((project.Structures??new List<StructureSpec>()).Select(s=>new KeyValuePair<string,string>(s.Id,s.Name)).Concat(DefDatabase<CampaignStructureDef>.AllDefsListForReading.Select(d=>new KeyValuePair<string,string>(d.defName,d.LabelCap))),id=>p.SetValue(draft,id));return;}
                    if(p.Name=="Origin"&&mapContext!=null){if(EditorUI.Button(r,"结构中心: "+EditorUI.Reference(mapContext,text)))EditorUI.Pick(mapContext.Objects.Where(o=>o.Kind=="Marker").Select(o=>new KeyValuePair<string,string>(o.Id,o.Name)),id=>p.SetValue(draft,id));return;}
                    if(p.Name=="Text"&&mapContext!=null){if(EditorUI.Button(r,"阵营: "+(text==""?"沿用结构":text)))EditorUI.Pick(new[]{new KeyValuePair<string,string>("","沿用结构")}.Concat(mapContext.Factions.Select(f=>new KeyValuePair<string,string>(f.Id,f.Name))),id=>p.SetValue(draft,id));return;}
                }
                if(mapContext!=null&&draft is Act build&&build.Kind=="rc.buildOutpost"&&p.Name=="Text"){if(EditorUI.Button(r,"施工放置组 / Blueprints: "+text))EditorUI.Pick(mapContext.Objects.Where(o=>o.Kind=="Thing"&&!string.IsNullOrEmpty(o.Group)).Select(o=>o.Group).Distinct(),id=>p.SetValue(draft,id));return;}
                if(mapContext!=null&&draft is Act relocation&&(relocation.Kind=="rc.reinforce"||relocation.Kind=="rc.buildOutpost")&&p.Name=="Origin"){if(EditorUI.Button(r,"出生位置 / Origin: "+EditorUI.Reference(mapContext,text)))EditorUI.Pick(new[]{new KeyValuePair<string,string>("","使用席位原位置 / Slot position")}.Concat(mapContext.Rules.Where(TriggerGeometry.Visible).Select(o=>new KeyValuePair<string,string>(o.Id,o.Name))).Concat(mapContext.Objects.Where(o=>o.Kind=="Marker").Select(o=>new KeyValuePair<string,string>(o.Id,o.Name))),id=>p.SetValue(draft,id));return;}

                if(draft is Act task&&(task.Kind=="rc.task.move"||task.Kind=="rc.task.operate")&&p.Name=="Target"&&mapContext!=null){if(EditorUI.Button(r,EditorUI.Reference(mapContext,text)))EditorUI.Pick(mapContext.Objects.Where(o=>task.Kind=="rc.task.move"?o.Kind=="Marker":o.Kind=="Thing").Select(o=>new KeyValuePair<string,string>(o.Id,o.Name)),id=>p.SetValue(draft,id));return;}
                if(mapContext!=null&&((draft is Act grouped&&(grouped.Kind=="rc.task.engage"||grouped.Kind=="rc.reinforce"||grouped.Kind=="rc.defendGroup"||grouped.Kind=="rc.buildOutpost")&&p.Name=="Target")||(draft is Condition cleared&&cleared.Op=="rc.targetsCleared"&&p.Name=="Key"))){if(EditorUI.Button(r,"对象组 / Group: "+text))EditorUI.Pick(mapContext.Objects.Select(o=>o.Group).Where(g=>!string.IsNullOrEmpty(g)).Distinct(),id=>p.SetValue(draft,id));return;}
                if(mapContext!=null&&draft is Act defense&&defense.Kind=="rc.defendGroup"&&p.Name=="Text"){if(EditorUI.Button(r,"驻守路点 / Defense point: "+EditorUI.Reference(mapContext,text)))EditorUI.Pick(mapContext.Objects.Where(o=>o.Kind=="Marker").Select(o=>new KeyValuePair<string,string>(o.Id,o.Name)),id=>p.SetValue(draft,id));return;}
                if(mapContext!=null&&draft is Act reinforcement&&reinforcement.Kind=="rc.reinforce"&&p.Name=="Text"){if(EditorUI.Button(r,"任务树 / Tasks: "+(mapContext.Groups.FirstOrDefault(g=>g.Id==text)?.Name??text)))EditorUI.Pick(mapContext.Groups.Select(g=>new KeyValuePair<string,string>(g.Id,g.Name)),id=>p.SetValue(draft,id));return;}
                if((draft is Rule && (p.Name=="Host"||p.Name=="Subject")) || (draft is ActionGroup && p.Name=="Actor")) {
                    if(EditorUI.Button(r,EditorUI.HostLabel(mapContext,text))&&mapContext!=null)
                        EditorUI.Pick(new[]{new KeyValuePair<string,string>("",RimCampaign.Runtime.RcText.T("无宿主"))}
                            .Concat(mapContext.Roles.Select(role=>new KeyValuePair<string,string>("role:"+role,RimCampaign.Runtime.RcText.T("角色：")+role)))
                            .Concat(mapContext.Objects.Where(o=>new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind)).Select(o=>new KeyValuePair<string,string>(o.Id,o.Name+" ["+o.Kind+"]"))),id=>p.SetValue(draft,id));
                    return;
                }
                if(draft is Act groupAction&&p.Name=="Target"&&groupAction.Kind=="SpawnGroup"&&mapContext!=null) {
                    if(EditorUI.Button(r,string.IsNullOrEmpty(text)?RimCampaign.Runtime.RcText.T("选择放置组"):text))EditorUI.Pick(mapContext.Objects.Select(o=>o.Group).Where(g=>!string.IsNullOrEmpty(g)).Distinct(),id=>p.SetValue(draft,id));return;
                }
                if(draft is MapFaction team && p.Name=="Side") {
                    if(team.Id=="Player"){EditorUI.DrawLabel(r,RimCampaign.Runtime.RcText.T("玩家（盟友队）"));return;}
                    if(EditorUI.Button(r,text=="Ally"?RimCampaign.Runtime.RcText.T("盟友"):text=="Hostile"?RimCampaign.Runtime.RcText.T("敌人"):RimCampaign.Runtime.RcText.T("中立")))
                        EditorUI.Pick(new[]{"Ally","Hostile","Neutral"}.Select(k=>new KeyValuePair<string,string>(k,k=="Ally"?RimCampaign.Runtime.RcText.T("盟友"):k=="Hostile"?RimCampaign.Runtime.RcText.T("敌人"):RimCampaign.Runtime.RcText.T("中立"))),id=>p.SetValue(draft,id));
                    return;
                }
                if(draft is Placement placement && p.Name=="Faction") {
                    var map=mapContext??project.Maps.FirstOrDefault(m=>m.Objects.Any(o=>o.Id==placement.Id))??project.Maps.FirstOrDefault();
                    var label=map?.Factions.FirstOrDefault(f=>f.Id==text)?.Name??(text=="Neutral"?RimCampaign.Runtime.RcText.T("无阵营（旧版）"):text);
                    if(EditorUI.Button(r,label)&&map!=null)
                        EditorUI.Pick(map.Factions.Select(f=>new KeyValuePair<string,string>(f.Id,f.Name)),id=>p.SetValue(draft,id));
                    return;
                }
                if(draft is Placement && p.Name=="BindRole") {
                    if(EditorUI.Button(r,string.IsNullOrEmpty(text)?RimCampaign.Runtime.RcText.T("不绑定角色（使用模板来源）"):text))
                        EditorUI.Pick(new[]{""}.Concat(project.Maps.SelectMany(m=>m.Roles).Distinct()),id=>p.SetValue(draft,id));
                    return;
                }
                if(draft is DefRef def && p.Name=="Name") {
                    if(EditorUI.Button(r,text==""?RimCampaign.Runtime.RcText.T("选择定义 / Choose Def"):text)) {
                        var choices=EditorUI.Defs(def.Kind).ToList();
                        EditorUI.Pick(choices.Select(d=>new KeyValuePair<string,string>(d.defName,d.LabelCap+" ["+d.defName+"]")),id=>{
                            var selected=choices.Single(d=>d.defName==id);def.Name=id;def.Package=selected.modContentPack?.PackageIdPlayerFacing??"ludeon.rimworld";
                        });
                    }return;
                }
                var options=Options(draft,p.Name);
                if(options!=null) {string Caption(string v)=>draft is Act&&p.Name=="Kind"?EditorUI.ActionTitle(v):draft is EventSpec&&p.Name=="Kind"?EditorUI.EventLabel(v):v;if(EditorUI.Button(r,Caption(text)))EditorUI.Pick(options.Select(v=>new KeyValuePair<string,string>(v,Caption(v))),s=>{p.SetValue(draft,s);if(draft is BossComponentSpec spec&&p.Name=="Module"&&Modules.Create(s) is BossComponentPlugin plugin)spec.Parameters=plugin.DefaultParameters;});return;}
                if(p.Name=="Name"){p.SetValue(draft,Widgets.TextField(r,text));return;}
                var field=r;field.width-=74;
                p.SetValue(draft,Widgets.TextField(field,text));
                if(EditorUI.Button(new Rect(r.xMax-70,r.y,70,r.height),RimCampaign.Runtime.RcText.T("选择"))) {
                    var refs=project.Maps.Cast<Named>().Concat(project.Profiles).Concat(project.Bosses??new List<BossTemplate>()).Concat(project.Campaigns)
                        .Concat(project.Maps.SelectMany(m=>m.Objects.Cast<Named>().Concat(m.Regions).Concat(m.Objectives).Concat(m.Rules).Concat(m.Groups).Concat(m.Stealth.Routes).Concat(m.Stealth.Guards).Concat(m.Stealth.Devices)))
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
                if(EditorUI.Button(r,RimCampaign.Runtime.RcText.T("编辑列表 / Edit list (")+list.Count+")"))Find.WindowStack.Add(new ListWindow(list,project,mapContext));
                return;
            }
            if(value is IDictionary dictionary) {
                if(EditorUI.Button(r,RimCampaign.Runtime.RcText.T("编辑绑定 / Edit bindings (")+dictionary.Count+")"))Find.WindowStack.Add(new DictionaryWindow(dictionary,project));
                return;
            }
            if(EditorUI.Button(r,EditorUI.Label(value))) {
                if(value==null)value=Activator.CreateInstance(type);
                Find.WindowStack.Add(new ObjectWindow(value,x=>p.SetValue(draft,x),project,mapContext));
            }
        }
        static IEnumerable<string> Options(object obj,string name) {
            if(obj is SoundPulseSpec&&name=="Receivers")return new[]{"Pawns","Guards","Things"};
            if(obj is DeviceSpec&&name=="Kind")return new[]{"Camera","Broadcast","Uplink","Password","Interaction","SupplyCrate"};
            if(obj is Condition&&name=="Op")return EditorUI.Conditions.Concat(Modules.All.Where(m=>m.Category=="Condition").Select(m=>m.Id));
            if(obj is EventSpec&&name=="Kind")return EditorUI.Events.Concat(Modules.All.Where(m=>m.Category=="Event").Select(m=>m.Id));
            if(obj is Act&&name=="Kind")return EditorUI.Actions.Concat(Modules.All.Where(m=>m.Category=="Action").Select(m=>m.Id));
            if(obj is Placement&&name=="Kind")return new[]{"Thing","Terrain","Roof","Pawn","PawnKind","Boss","Marker"};
            if(obj is BossAction&&name=="Module")return Modules.All.Where(m=>m.Category=="BossBehavior").Select(m=>m.Id);
            if(obj is BossComponentSpec&&name=="Module")return Modules.All.Where(m=>m.Category=="BossComponent").Select(m=>m.Id);

            if(obj is MapSpec&&name=="Base")return new[]{"Blank","Generated","PocketMission"};
            if(obj is Node&&name=="Kind")return new[]{"Story","Open"};
            if(obj is Node&&name=="Arrival")return new[]{"Position","Edge","Pods"}.Concat(Modules.All.Where(m=>m.Category=="Arrival").Select(m=>m.Id));
            if(obj is Objective&&name=="Mode")return new[]{"Standard","Survive"};
            if(obj is HudWidget&&name=="Kind")return new[]{"Text","Counter","Timer","Progress","Objectives"};
            if(obj is DefRef&&name=="Kind")return new[]{"ThingDef","TerrainDef","RoofDef","SoundDef","PawnKindDef","AbilityDef","DamageDef","CommunicationPortraitDef"};
            if(obj is CommunicationSpec&&name=="Renderer")return new[]{""}.Concat(Modules.All.Where(m=>m.Category=="CommunicationRenderer").Select(m=>m.Id));
            return null;
        }
        bool StealthReference(PropertyInfo field,Rect rect,string text)
        {
            var map=mapContext;if(map==null)return false;
            IEnumerable<KeyValuePair<string,string>> choices=null;
            if(draft is CommunicationSpec&&field.Name=="Speaker")choices=new[]{new KeyValuePair<string,string>("$source",RimCampaign.Runtime.RcText.T("本次触发源"))}.Concat(map.Roles.Select(r=>new KeyValuePair<string,string>(r,RimCampaign.Runtime.RcText.T("角色：")+r))).Concat(map.Objects.Where(o=>new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind)).Select(o=>new KeyValuePair<string,string>(o.Id,o.Name)));
            else if((draft is StealthSpec&&field.Name=="Actor")||(draft is GuardSpec&&field.Name=="Pawn"))
                choices=map.Roles.Select(r=>new KeyValuePair<string,string>(r,RimCampaign.Runtime.RcText.T("角色：")+r)).Concat(map.Objects.Where(o=>o.Kind=="PawnKind"||o.Kind=="Pawn").Select(o=>new KeyValuePair<string,string>(o.Id,o.Name)));
            else if(draft is CombatPatrolSpec&&field.Name=="Pawn")choices=map.Objects.Where(o=>new[]{"Pawn","PawnKind","Boss"}.Contains(o.Kind)).Select(o=>new KeyValuePair<string,string>(o.Id,o.Name));
            else if(draft is SceneInteractionSpec&&field.Name=="Host")choices=map.Objects.Where(o=>o.Kind=="Thing").Select(o=>new KeyValuePair<string,string>(o.Id,o.Name+" ("+o.Position.X+", "+o.Position.Z+")"));
            else if(draft is CombatPatrolSpec&&field.Name=="Route")choices=map.PatrolRoutes.Select(o=>new KeyValuePair<string,string>(o.Id,o.Name));
            else if(draft is GuardSpec&&field.Name=="Route")choices=map.Stealth.Routes.Select(o=>new KeyValuePair<string,string>(o.Id,o.Name));
            else if((draft is GuardSpec||draft is DeviceSpec)&&field.Name=="Uplink")choices=map.Stealth.Devices.Where(o=>o.Kind=="Uplink").Select(o=>new KeyValuePair<string,string>(o.Id,o.Name));
            else if(draft is DeviceSpec&&field.Name=="Host")choices=map.Objects.Where(o=>o.Kind=="Thing").Select(o=>new KeyValuePair<string,string>(o.Id,o.Name));
            else if(draft is SoundPulseSpec&&field.Name=="Faction")choices=map.Factions.Select(f=>new KeyValuePair<string,string>(f.Id,f.Name));
            else if(draft is SoundPulseSpec&&field.Name=="Group")choices=map.Objects.Select(o=>o.Group).Where(g=>!string.IsNullOrEmpty(g)).Distinct().Select(g=>new KeyValuePair<string,string>(g,g));
            else if((draft is DeviceSpec&&field.Name=="PasswordRule")||(draft is Act a&&a.Kind=="PasswordPrompt"&&field.Name=="Target"))choices=map.Rules.Where(o=>o.Events.Any(e=>e.Kind=="Password")).Select(o=>new KeyValuePair<string,string>(o.Id,o.Name));
            if(choices==null)return false;
            var all=new[]{new KeyValuePair<string,string>("",RimCampaign.Runtime.RcText.T("无"))}.Concat(choices).ToList();
            if(EditorUI.Button(rect,all.FirstOrDefault(o=>o.Key==text).Value??text))EditorUI.Pick(all,id=>field.SetValue(draft,id));return true;
        }
    }
    public class ListWindow : Window
    {
        readonly IList list;readonly Project project;readonly MapSpec mapContext;readonly Action apply;Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(760,650);
        public ListWindow(IList list,Project project,MapSpec map=null,Action apply=null){this.list=list;this.project=project;mapContext=map;this.apply=apply;doCloseX=true;absorbInputAroundWindow=true;}
        public override void PostClose(){apply?.Invoke();base.PostClose();}
        public override void DoWindowContents(Rect r) {
            EditorUI.DrawLabel(new Rect(0,0,r.width-40,30),list is List<Act>?RcText.T("按顺序执行的动作"):RcText.T("有序列表 / Ordered list"));
            var itemType=list.GetType().GetGenericArguments()[0];
            if(EditorUI.Button(new Rect(0,35,r.width,32),RimCampaign.Runtime.RcText.T("+ 添加 / Add")))list.Add(itemType==typeof(string)?"":Activator.CreateInstance(itemType));
            Widgets.BeginScrollView(new Rect(0,76,r.width,r.height-122),ref scroll,new Rect(0,0,r.width-20,list.Count*42));
            for(int i=0;i<list.Count;i++) {
                int index=i;var item=list[i];float y=i*42;
                if(itemType==typeof(string))list[i]=Widgets.TextField(new Rect(0,y,r.width-160,34),(string)item);
                else if(EditorUI.Button(new Rect(0,y,r.width-160,34),EditorUI.Describe(item,mapContext)))Find.WindowStack.Add(new ObjectWindow(item,x=>list[index]=x,project,mapContext));
                if(EditorUI.Button(new Rect(r.width-155,y,40,34),"↑")&&i>0){var t=list[i-1];list[i-1]=list[i];list[i]=t;}
                if(EditorUI.Button(new Rect(r.width-110,y,40,34),"↓")&&i<list.Count-1){var t=list[i+1];list[i+1]=list[i];list[i]=t;}
                if(EditorUI.Button(new Rect(r.width-65,y,40,34),"×")){list.RemoveAt(i);break;}
            }Widgets.EndScrollView();
            if(EditorUI.Button(new Rect(0,r.height-40,r.width,36),RimCampaign.Runtime.RcText.T("返回")))Close();
        }
    }
    public class DictionaryWindow : Window
    {
        readonly IDictionary map;readonly Project project;string key="";Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(700,600);
        public DictionaryWindow(IDictionary map,Project project){this.map=map;this.project=project;doCloseX=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect r) {
            key=Widgets.TextField(new Rect(0,0,r.width-100,32),key);var type=map.GetType().GetGenericArguments()[1];
            if(EditorUI.Button(new Rect(r.width-95,0,90,32),RimCampaign.Runtime.RcText.T("添加"))&&!string.IsNullOrWhiteSpace(key)&&!map.Contains(key))map.Add(key,type==typeof(string)?"":Activator.CreateInstance(type));
            var keys=map.Keys.Cast<string>().ToList();
            Widgets.BeginScrollView(new Rect(0,45,r.width,r.height-92),ref scroll,new Rect(0,0,r.width-20,keys.Count*44));
            for(int i=0;i<keys.Count;i++){
                string k=keys[i];float y=i*44;
                EditorUI.DrawLabel(new Rect(0,y,180,34),k);
                var value=Widgets.TextField(new Rect(180,y,r.width-315,34),Convert.ToString(map[k],CultureInfo.InvariantCulture));
                try{map[k]=Convert.ChangeType(value,type,CultureInfo.InvariantCulture);}catch(FormatException){}catch(OverflowException){}
                if(type==typeof(string)&&EditorUI.Button(new Rect(r.width-130,y,65,34),RimCampaign.Runtime.RcText.T("角色")))EditorUI.Pick(project.Profiles.Select(p=>new KeyValuePair<string,string>(p.Id,p.Name)),s=>map[k]=s);
                if(EditorUI.Button(new Rect(r.width-60,y,40,34),"×")){map.Remove(k);break;}
            }Widgets.EndScrollView();
            if(EditorUI.Button(new Rect(0,r.height-40,r.width,36),RimCampaign.Runtime.RcText.T("返回")))Close();
        }
    }
}
