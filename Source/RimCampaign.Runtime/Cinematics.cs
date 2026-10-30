using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimCampaign.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Runtime {
    public partial class CampaignController {
        readonly Dictionary<string,Vector3> cameraOrigins=new Dictionary<string,Vector3>();
        readonly Dictionary<string,float> cameraSizes=new Dictionary<string,float>();
        List<int> presentationFog;
        Map presentationMap;
        bool keepPresentationCamera;
        string presentationActor;
        float? presentationFarClip;
        public bool Cinematic=>Active&&State.Fibers.SelectMany(ActiveActions).Any(a=>a.Kind=="Cinematic");
        public bool RevealingMap=>presentationFog!=null&&presentationMap==Map;
        void BeginCinematic(Act action){
            TakeControl();keepPresentationCamera=true;presentationActor=action.Target;
            Find.TickManager.CurTimeSpeed=TimeSpeed.Normal;
            if(action.Text=="RevealMap"&&presentationFog==null){
                presentationMap=Map;
                presentationFog=Enumerable.Range(0,Map.cellIndices.NumGridCells).Where(i=>Map.fogGrid.IsFogged(i)).ToList();
                Map.fogGrid.ClearAllFog();
            }
        }
        void EndCinematic(){
            if(presentationFarClip.HasValue&&Find.Camera!=null)Find.Camera.farClipPlane=presentationFarClip.Value;
            presentationFarClip=null;
            if(presentationFog!=null&&presentationMap!=null){
                presentationMap.fogGrid.Refog(new CellRect(0,0,presentationMap.Size.x,presentationMap.Size.z));
                var hidden=new HashSet<int>(presentationFog);
                for(int index=0;index<presentationMap.cellIndices.NumGridCells;index++)if(!hidden.Contains(index))presentationMap.fogGrid.Unfog(presentationMap.cellIndices.IndexToCell(index));
            }
            presentationFog=null;presentationMap=null;keepPresentationCamera=false;
            var actor=ResolveTriggerPawn(presentationActor??"");presentationActor=null;
            cameraOrigins.Clear();cameraSizes.Clear();
            if(!Cinematic&&actor?.Spawned==true)Find.Selector.Select(actor);
        }
        bool MoveCamera(Act action,double delta){
            if(!cameraOrigins.ContainsKey(action.Id)){cameraOrigins[action.Id]=Find.CameraDriver.MapPosition.ToVector3();cameraSizes[action.Id]=Find.CameraDriver.RootSize;}
            var marker=MapSpec.Objects.Single(o=>o.Id==action.Target);
            var destination=new Vector3(marker.Position.X,0,marker.Position.Z);
            float size=action.Text=="FitMap"?Math.Max(Map.Size.z/2f,Map.Size.x/(2f*Math.Max(.1f,(float)UI.screenWidth/UI.screenHeight)))+4:
                string.IsNullOrEmpty(action.Text)?cameraSizes[action.Id]:float.Parse(action.Text,System.Globalization.CultureInfo.InvariantCulture);
            if(action.Text!="FitMap"){
                float halfWidth=size*UI.screenWidth/UI.screenHeight;
                destination.x=halfWidth*2>=Map.Size.x?Map.Size.x/2f:Mathf.Clamp(destination.x,halfWidth,Map.Size.x-halfWidth);
                destination.z=size*2>=Map.Size.z?Map.Size.z/2f:Mathf.Clamp(destination.z,size,Map.Size.z-size);
            }
            float fraction=action.Value<=0?1:Progress(action,delta),ease=Mathf.SmoothStep(0,1,fraction);
            // FitMap exceeds vanilla zoom limits and raises the camera above its
            // normal far plane. Keep the terrain visible throughout the overview.
            if(Find.Camera!=null){
                if(!presentationFarClip.HasValue)presentationFarClip=Find.Camera.farClipPlane;
                Find.Camera.farClipPlane=Math.Max(presentationFarClip.Value,size*10+100);
            }
            Find.CameraDriver.SetRootPosAndSize(Vector3.Lerp(cameraOrigins[action.Id],destination,ease),Mathf.Lerp(cameraSizes[action.Id],size,ease));
            return fraction>=1;
        }
    }
    [HarmonyPatch(typeof(CameraDriver),"get_AnythingPreventsCameraMotion")]
    static class CinematicCameraInput {
        static void Postfix(ref bool __result){if(CampaignController.Focused?.Cinematic==true)__result=true;}
    }
    [HarmonyPatch(typeof(TimeControls),nameof(TimeControls.DoTimeControlsGUI))]
    static class CinematicTimeInput {
        static bool Prefix()=>CampaignController.Focused?.Cinematic!=true;
    }
    [HarmonyPatch(typeof(UIRoot_Play),nameof(UIRoot_Play.UIRootOnGUI))]
    static class CinematicInterface {
        // Keep the base UI (including campaign communications) and modal windows, but no map orders or gameplay HUD.
        static bool FinishCinematicGUI(){
            if(CampaignController.Focused?.Cinematic!=true)return false;
            Find.WindowStack.WindowStackOnGUI();Widgets.WidgetsOnGUI();
            if(Event.current.type==EventType.KeyDown&&Event.current.keyCode==KeyCode.Escape){Event.current.Use();Find.MainTabsRoot.SetCurrentTab(MainButtonDefOf.Menu);}
            return true;
        }
        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,ILGenerator generator){
            var baseGui=AccessTools.Method(typeof(UIRoot),nameof(UIRoot.UIRootOnGUI));
            foreach(var instruction in instructions){
                yield return instruction;
                if(instruction.Calls(baseGui)){
                    var normal=generator.DefineLabel();
                    yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(CinematicInterface),nameof(FinishCinematicGUI)));
                    yield return new CodeInstruction(OpCodes.Brfalse,normal);
                    yield return new CodeInstruction(OpCodes.Ret);
                    var next=new CodeInstruction(OpCodes.Nop);next.labels.Add(normal);yield return next;
                }
            }
        }
    }
}
