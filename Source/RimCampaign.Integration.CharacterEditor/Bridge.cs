using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimCampaign.Editor;
using RimWorld;
using Verse;

namespace RimCampaign.Integration.CharacterEditor
{
    [StaticConstructorOnStartup]
    public static class Registration
    {
        static Registration(){
            try{CharacterIntegration.Bridge=new Bridge();}
            catch(Exception ex){Log.Warning("[RimCampaign] Character Editor adapter unavailable: "+ex.Message);}
        }
    }
    public class Bridge : ICharacterEditorBridge
    {
        readonly Type editor,preset;readonly PropertyInfo api,pawn,slots;readonly MethodInfo start,close,getSlot,load,generate,post;readonly FieldInfo result;
        const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
        public Bridge(){
            editor=AccessTools.TypeByName("CharacterEditor.CEditor")??throw new InvalidOperationException("CEditor not found.");
            preset=AccessTools.TypeByName("CharacterEditor.PresetPawn")??throw new InvalidOperationException("PresetPawn not found.");
            api=editor.GetProperty("API",Flags);pawn=editor.GetProperty("Pawn",Flags);slots=editor.GetProperty("NumSlots",Flags);
            start=editor.GetMethod("StartEditor",Flags);close=editor.GetMethod("CloseEditor",Flags);getSlot=editor.GetMethod("GetSlot",Flags);
            load=preset.GetMethod("Load",Flags);generate=preset.GetMethod("GeneratePawn",Flags);post=preset.GetMethod("PostProcess",Flags);result=preset.GetField("pawn",Flags);
            if(new MemberInfo[]{api,pawn,slots,start,close,getSlot,load,generate,post,result}.Any(x=>x==null))throw new InvalidOperationException("This CE version does not match the supported adapter contract.");
        }
        object Api=>api.GetValue(null)??throw new InvalidOperationException("Character Editor has not initialized.");
        public string Compatibility=>"Character Editor "+editor.Assembly.GetName().Version+"：适配接口已检测";
        public Pawn EditedPawn=>(Pawn)pawn.GetValue(Api);
        public void Open(Pawn p,Page_ConfigureStartingPawns owner){editor.GetField("pstartInstance",Flags)?.SetValue(null,owner);start.Invoke(Api,new object[]{p});}
        public IEnumerable<KeyValuePair<int,string>> Slots(){
            int count=(int)slots.GetValue(Api);
            for(int i=0;i<count;i++){
                string data=(string)getSlot.Invoke(Api,new object[]{i});
                if(!string.IsNullOrEmpty(data))yield return new KeyValuePair<int,string>(i,"槽位 "+(i+1)+" — "+data.Split('*')[0]);
            }
        }
        public Pawn Import(int slot){
            var temporary=Activator.CreateInstance(preset,true);
            if(!(bool)load.Invoke(temporary,new object[]{slot,""}))throw new InvalidOperationException("Slot is empty or unreadable.");
            // Generate into the isolated authoring world. Do not call LoadPawn/AddOrCreateExistingPawn.
            generate.Invoke(temporary,new object[]{true,true});post.Invoke(temporary,null);
            var p=(Pawn)result.GetValue(temporary)??throw new InvalidOperationException("CE failed to create a pawn.");
            pawn.SetValue(Api,p);return p;
        }
        public void Close(){if(api.GetValue(null)!=null){close.Invoke(Api,null);pawn.SetValue(Api,null);}editor.GetField("pstartInstance",Flags)?.SetValue(null,null);}
    }
}
