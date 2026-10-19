using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor
{
    public interface ICharacterEditorBridge
    {
        string Compatibility { get; }
        void Open(Pawn pawn,Page_ConfigureStartingPawns owner);
        IEnumerable<KeyValuePair<int,string>> Slots();
        Pawn Import(int slot);
        Pawn EditedPawn { get; }
        void Close();
    }
    public static class CharacterIntegration { public static ICharacterEditorBridge Bridge; }
    public sealed class AuthoringSession : IDisposable
    {
        readonly Game previous;readonly ProgramState state;
        public AuthoringSession(){
            if(Current.ProgramState==ProgramState.Playing)throw new InvalidOperationException("Character authoring must be opened from the main menu.");
            previous=Current.Game;state=Current.ProgramState;
            try {Root_Play.SetupForQuickTestPlay();}
            catch {Current.Game=previous;Current.ProgramState=state;throw;}
        }
        public void Dispose(){
            CharacterIntegration.Bridge?.Close();PortraitsCache.Clear();
            Current.Game=previous;Current.ProgramState=state;Game.ClearCaches();
        }
    }
    public class ProfilesWindow : Window
    {
        readonly Project project;readonly List<Profile> profiles;readonly Action<List<Profile>> apply;Vector2 scroll;
        public override Vector2 InitialSize=>new Vector2(820,650);
        public ProfilesWindow(Project p,Action<List<Profile>> apply){project=p;profiles=Json.Copy(p.Profiles);this.apply=apply;doCloseX=true;absorbInputAroundWindow=true;}
        public override void DoWindowContents(Rect r){
            if(Widgets.ButtonText(new Rect(0,0,220,36),"原版创建角色"))Open(null,false);
            GUI.enabled=CharacterIntegration.Bridge!=null;
            if(Widgets.ButtonText(new Rect(230,0,250,36),"导入 Character Editor 槽位"))Open(null,true);
            GUI.enabled=true;
            if(Widgets.ButtonText(new Rect(490,0,270,36),"从 PawnKind 随机生成"))
                EditorUI.Pick(DefDatabase<PawnKindDef>.AllDefs.Select(k=>new KeyValuePair<string,string>(k.defName,k.LabelCap+" ["+k.defName+"]")),id=>{
                    var kind=DefDatabase<PawnKindDef>.GetNamed(id);profiles.Add(new Profile{Name=kind.LabelCap+" · 随机",GenerateFrom=Profiles.Ref(kind)});
                });
            Widgets.Label(new Rect(0,45,r.width,32),CharacterIntegration.Bridge?.Compatibility??"Character Editor 未启用；原版档案可独立使用。");
            Widgets.BeginScrollView(new Rect(0,85,r.width,r.height-145),ref scroll,new Rect(0,0,r.width-20,profiles.Count*44));
            for(int i=0;i<profiles.Count;i++){
                var p=profiles[i];Widgets.Label(new Rect(0,i*44,r.width-190,36),p.Name);
                if(Widgets.ButtonText(new Rect(r.width-180,i*44,80,36),"编辑")) {
                    if(p.GenerateFrom!=null)Find.WindowStack.Add(new ObjectWindow(p,o=>profiles[profiles.FindIndex(x=>x.Id==p.Id)]=(Profile)o,project));
                    else Open(p,false);
                }
                if(Widgets.ButtonText(new Rect(r.width-95,i*44,70,36),"删除")){
                    var uses=project.Campaigns.SelectMany(c=>c.Nodes).Where(n=>n.Roles.Values.Contains(p.Id)).Select(n=>n.Name).ToList();
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("删除档案并重新绑定这些节点：\n"+string.Join("\n",uses),()=>profiles.Remove(p)));
                }
            }Widgets.EndScrollView();
            if(Widgets.ButtonText(new Rect(0,r.height-44,r.width,38),"保存档案列表")){apply(profiles);Close();}
        }
        void Open(Profile p,bool slot){
            LongEventHandler.QueueLongEvent(()=>{
                AuthoringSession session=null;
                try {
                    session=new AuthoringSession();var owned=session;
                    LongEventHandler.ExecuteWhenFinished(()=>Find.WindowStack.Add(new PawnDraftWindow(owned,p,slot,result=>{
                        if(p!=null){result.Id=p.Id;profiles[profiles.FindIndex(x=>x.Id==p.Id)]=result;}else profiles.Add(result);
                    })));
                }catch(Exception ex){session?.Dispose();LongEventHandler.ExecuteWhenFinished(()=>EditorUI.Error(ex));}
            },"GeneratingWorld",false,null);
        }
    }
    public class PawnDraftWindow : Page_ConfigureStartingPawns
    {
        readonly AuthoringSession session;readonly Profile profile;readonly bool import;readonly Action<Profile> save;
        Pawn pawn;bool usedCE;string errors="";
        public PawnDraftWindow(AuthoringSession session,Profile p,bool import,Action<Profile> save){this.session=session;profile=p;this.import=import;this.save=save;doCloseX=true;}
        public override void PreOpen(){
            if(profile==null)pawn=Find.GameInitData.startingAndOptionalPawns[0];else pawn=Profiles.Create(profile,Faction.OfPlayer);
            SetPawn(pawn);base.PreOpen();
        }
        void SetPawn(Pawn p){pawn=p;Find.GameInitData.startingAndOptionalPawns=new List<Pawn>{p};Find.GameInitData.startingPawnCount=1;}
        public override void PostOpen(){base.PostOpen();if(import)ChooseSlot();}
        void ChooseSlot(){
            try{var bridge=CharacterIntegration.Bridge;EditorUI.Pick(bridge.Slots().Select(s=>new KeyValuePair<string,string>(s.Key.ToString(),s.Value)),s=>{SetPawn(bridge.Import(int.Parse(s)));usedCE=true;});}
            catch(Exception ex){EditorUI.Error(ex);}
        }
        public override void DoWindowContents(Rect r){
            if(usedCE&&CharacterIntegration.Bridge.EditedPawn!=null)pawn=CharacterIntegration.Bridge.EditedPawn;
            Widgets.Label(new Rect(0,0,r.width,35),"角色档案：确认后保存独立副本");
            GUI.DrawTexture(new Rect(10,50,180,250),PortraitsCache.Get(pawn,new Vector2(180,250),Rot4.South));
            var card=new Rect(210,50,r.width-225,r.height-190);
            CharacterCardUtility.DrawCharacterCard(card,pawn,()=>{
                StartingPawnUtility.RandomizePawn(0);SetPawn(Find.GameInitData.startingAndOptionalPawns[0]);usedCE=false;
            },card);
            GUI.enabled=CharacterIntegration.Bridge!=null;
            if(Widgets.ButtonText(new Rect(0,r.height-125,240,35),"使用 Character Editor 编辑")){
                try{SetPawn(pawn);CharacterIntegration.Bridge.Open(pawn,this);usedCE=true;}catch(Exception ex){EditorUI.Error(ex);}
            }
            if(Widgets.ButtonText(new Rect(250,r.height-125,220,35),"从 CE 槽位导入"))ChooseSlot();
            GUI.enabled=true;
            Widgets.Label(new Rect(0,r.height-85,r.width,40),errors);
            if(Widgets.ButtonText(new Rect(0,r.height-40,r.width,35),"确认保存角色档案")){
                try{
                    var result=Profiles.Capture(pawn);
                    if(result.PortabilityErrors.Count>0){errors=string.Join("; ",result.PortabilityErrors);Find.WindowStack.Add(new Dialog_MessageBox("无法完整转换：\n"+string.Join("\n",result.PortabilityErrors)));return;}
                    save(result);Close();
                }catch(Exception ex){EditorUI.Error(ex);}
            }
        }
        public override void PostClose(){base.PostClose();session.Dispose();}
    }
}
