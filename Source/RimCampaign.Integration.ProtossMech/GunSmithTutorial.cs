using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using GunSmithFramework;
using HarmonyLib;
using ProtossMech.GunSmith;
using RimCampaign.Core;
using RimCampaign.Runtime;
using RimCampaign.SDK;
using UnityEngine;
using Verse;

namespace RimCampaign.Integration.ProtossMech {
    // The adapter observes PMC's actual controls. It owns no editor layout or pricing rules.
    [StaticConstructorOnStartup]
    static class TutorialHooks {
        static TutorialHooks(){new Harmony("rimcampaign.pmc.tutorial").PatchAll(typeof(TutorialHooks).Assembly);}
        public static TutorialMapComponent Active=>TutorialMapComponent.Current?.Id=="pmc.gunsmith"&&TutorialMapComponent.Current.Running?TutorialMapComponent.Current:null;
        public static string Value(string key)=>Active?.Data.TryGetValue(key,out var value)==true?value:"";
        public static Thing Weapon=>Current.Game?.GetComponent<CampaignController>()?.ResolveTriggerPawn(Value("actor"))?.equipment?.Primary;
        public static Dialog_GunSmithWeaponPicker Picker;
        public static GunSmithPartDef Part=>DefDatabase<GunSmithPartDef>.GetNamedSilentFail(Value("part"));
        public static bool CloseEditors;
        public static Game CloseEditorsGame;
        public static Map CloseEditorsMap;
    }
    [CampaignModule("rc.pmcGunSmithTutorial","Action","Tutorial JSON in Text: steps, completion and forge/actor/slot/part identifiers.")]
    public sealed class GunSmithTutorialAction:IActionModule {
        public sealed class Setup {public System.Collections.Generic.List<TutorialStep> Steps{get;set;} public System.Collections.Generic.List<Act> Completion{get;set;} public System.Collections.Generic.Dictionary<string,string> Data{get;set;}}
        public bool Execute(Act a,IHost host,string source,double delta){
            var c=(CampaignController)host;var spec=Json.Read<Setup>(a.Text);var module=c.Map.GetComponent<TutorialMapComponent>();
            if(module==null)throw new InvalidOperationException("Attach TutorialMapComponent before starting a tutorial.");
            Find.Selector.ClearSelection();Find.Selector.Select(c.Objects[spec.Data["forge"]]);
            module.Begin("pmc.gunsmith",spec.Steps,spec.Completion,spec.Data);return true;
        }
    }
    [HarmonyPatch(typeof(Command),"GizmoOnGUIInt")]
    static class ForgeTutorialGizmo {
        static void Prefix(Command __instance,Rect butRect){if(__instance.defaultLabel=="ProtossMech.GunSmithForge".Translate().ToString())TutorialHooks.Active?.Report("forge",butRect);}
    }
    [HarmonyPatch(typeof(Dialog_GunSmithWeaponPicker),nameof(Dialog_GunSmithWeaponPicker.PreOpen))]
    static class ForgeTutorialOpen {static void Postfix()=>TutorialHooks.Active?.Satisfy("forge");}
    [HarmonyPatch(typeof(Dialog_GunSmithWeaponPicker),"DrawContents")]
    static class ForgeTutorialPickerScope {
        static void Prefix(Dialog_GunSmithWeaponPicker __instance){TutorialHooks.Picker=__instance;}
        static void Postfix(Dialog_GunSmithWeaponPicker __instance){
            if(Traverse.Create(__instance).Field<Thing>("selected").Value==TutorialHooks.Weapon&&TutorialHooks.Weapon!=null)TutorialHooks.Active?.Satisfy("weapon");
            TutorialHooks.Picker=null;
        }
        static Exception Finalizer(Exception __exception){TutorialHooks.Picker=null;return __exception;}
    }
    [HarmonyPatch(typeof(Widgets),nameof(Widgets.Label),new[]{typeof(Rect),typeof(string)})]
    static class ForgeTutorialWeaponRow {
        static void Prefix(Rect rect,string label){
            if(TutorialHooks.Active?.Target!="weapon"||TutorialHooks.Picker==null)return;
            foreach(var entry in (IEnumerable)Traverse.Create(TutorialHooks.Picker).Field("weapons").GetValue()){
                var row=Traverse.Create(entry);
                if(row.Field<Thing>("thing").Value==TutorialHooks.Weapon&&row.Field<string>("displayLabel").Value==label){TutorialHooks.Active.Report("weapon",new Rect(rect.x-14,rect.y-8,rect.width+28,rect.height+16));break;}
            }
        }
    }
    [HarmonyPatch(typeof(GunSmithEditorUI),"DrawSlotGizmo")]
    static class ForgeTutorialSlot {
        static void Prefix(string slotId,Rect gizmo){if(slotId==TutorialHooks.Value("slot"))TutorialHooks.Active?.Report("slot",gizmo);}
        static void Postfix(GunSmithEditorState state){if(state.selectedSlotId==TutorialHooks.Value("slot"))TutorialHooks.Active?.Satisfy("slot");}
    }
    [HarmonyPatch(typeof(GunSmithEditorUI),nameof(GunSmithEditorUI.DrawAttachmentOption))]
    static class ForgeTutorialPart {
        static void Prefix(Rect row,Texture icon){if(TutorialHooks.Part?.Icon==icon)TutorialHooks.Active?.Report("part",row);}
    }
    [HarmonyPatch(typeof(GunSmithEditorUI),"DrawAttachmentList")]
    static class ForgeTutorialPartApplied {
        static void Postfix(GunSmithEditorState state){if(state.loadout.GetRawPart(TutorialHooks.Value("slot"))==TutorialHooks.Part&&TutorialHooks.Part!=null)TutorialHooks.Active?.Satisfy("part");}
    }
    [HarmonyPatch(typeof(GunSmithEditorUI),"DrawActions")]
    static class ForgeTutorialApplyRect {
        static void Prefix(Rect rect){TutorialHooks.Active?.Report("apply",new Rect(rect.xMax-160,rect.y,150,32));}
    }
    [HarmonyPatch(typeof(GunSmithOperations),nameof(GunSmithOperations.TryApplyForgeLoadout))]
    static class ForgeTutorialApplied {
        static void Postfix(bool __result,Thing target,GunSmithLoadout draft){
            if(__result&&TutorialHooks.Active?.Target=="apply"&&target==TutorialHooks.Weapon&&draft.GetRawPart(TutorialHooks.Value("slot"))==TutorialHooks.Part){TutorialHooks.Active.Satisfy("apply");TutorialHooks.CloseEditors=true;TutorialHooks.CloseEditorsGame=Current.Game;TutorialHooks.CloseEditorsMap=Find.CurrentMap;}
        }
    }
    public sealed class GunSmithTutorialLifecycle:GameComponent {
        bool restore;
        public GunSmithTutorialLifecycle(Game game){TutorialHooks.CloseEditors=false;TutorialHooks.CloseEditorsGame=null;TutorialHooks.CloseEditorsMap=null;TutorialHooks.Picker=null;}
        public override void LoadedGame(){restore=true;}
        public override void GameComponentUpdate(){
            if(TutorialHooks.CloseEditors){
                bool owned=TutorialHooks.CloseEditorsGame==Current.Game&&TutorialHooks.CloseEditorsMap!=null&&
                    TutorialHooks.CloseEditorsMap==Current.Game.GetComponent<CampaignController>()?.Map&&TutorialHooks.CloseEditorsMap==Find.CurrentMap;
                TutorialHooks.CloseEditors=false;TutorialHooks.CloseEditorsGame=null;TutorialHooks.CloseEditorsMap=null;
                if(owned)foreach(var w in Find.WindowStack.Windows.Where(w=>w is Dialog_GunSmithEditor||w is Dialog_GunSmithWeaponPicker).ToList())w.Close(false);
            }
            var tutorial=TutorialHooks.Active;if(tutorial==null)return;
            // Windows are transient; restart the UI walk-through against the weapon actually saved.
            if(restore){restore=false;tutorial.Step=0;}
            if(tutorial.Target!="forge")return;var c=Current.Game.GetComponent<CampaignController>();
            if(c.Objects.TryGetValue(TutorialHooks.Value("forge"),out var forge)&&Find.Selector.SingleSelectedThing!=forge){Find.Selector.ClearSelection();Find.Selector.Select(forge);Find.CameraDriver.JumpToCurrentMapLoc(forge.Position);}
        }
    }
}
