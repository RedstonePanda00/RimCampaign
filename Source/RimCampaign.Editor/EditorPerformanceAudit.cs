using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimWorld;
using UnityEngine;
using Verse;

namespace RimCampaign.Editor {
    // Explicit opt-in benchmark of real IMGUI calls, with the same authored maps before/after.
    [HarmonyPatch(typeof(MainMenuDrawer),nameof(MainMenuDrawer.DoMainMenuControls))]
    static class EditorPerformanceAudit {
        internal static readonly bool Enabled=GenCommandLine.CommandLineArgPassed("rc-editor-performance-audit");
        internal static Workspace Workspace;
        internal static int Frames,Stage;
        internal static readonly List<double> Times=new List<double>();
        internal static long Allocated;
        static string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"EditorPerformanceAudit");
        static void Postfix(){
            if(!Enabled||Workspace!=null||Stage==99)return;
            try {
                Directory.CreateDirectory(Folder);
                var input=Environment.GetCommandLineArgs().First(a=>a.StartsWith("-rc-audit-project=")).Substring("-rc-audit-project=".Length);
                Workspace=new Workspace();var t=Traverse.Create(Workspace);
                t.Field("history").SetValue(new History<Project>(ProjectFiles.Load(input)));
                t.Field("mapId").SetValue("romulus-outpost");
                Find.WindowStack.Add(Workspace);
                File.WriteAllText(Path.Combine(Folder,"metrics.tsv"),"scene\tobjects\tpatrol_points\trepaints\tmean_ms\tp95_ms\tallocated_bytes_per_repaint\tmanaged_heap_bytes\tworking_set_bytes\n");
            }catch(Exception ex){Fail(ex);}
        }
        internal static void Sample(double ms,long bytes){
            if(Stage==99)return;
            try {
                Frames++;
                if(Frames<=30)return;
                Times.Add(ms);Allocated+=bytes;
                if(Times.Count<120)return;
                var map=Workspace.Map;var sorted=Times.OrderBy(v=>v).ToList();
                File.AppendAllText(Path.Combine(Folder,"metrics.tsv"),string.Join("\t",new[]{
                    Stage==0?"mission-two-full":Stage==1?"mission-two-zoom":Stage==2?"mission-one-full":"mission-two-scene-list",
                    map.Objects.Count.ToString(),map.PatrolRoutes.Concat(map.Stealth.Routes).Sum(r=>r.Points.Count).ToString(),Times.Count.ToString(),
                    Times.Average().ToString("F3",CultureInfo.InvariantCulture),sorted[(int)(sorted.Count*.95)].ToString("F3",CultureInfo.InvariantCulture),
                    (Allocated/Times.Count).ToString(),GC.GetTotalMemory(false).ToString(),Process.GetCurrentProcess().WorkingSet64.ToString()})+"\n");
                Stage++;Frames=0;Times.Clear();Allocated=0;
                var t=Traverse.Create(Workspace);var view=t.Field("mapView").GetValue<MapViewport>();
                if(Stage==1){view.ZoomAt(-8,view.ViewWidth/2,view.ViewHeight/2);t.Field("selectedId").SetValue(map.Objects.First(p=>p.Layer=="Patrol").Id);}
                else if(Stage==2){t.Field("selectedId").SetValue("");t.Field("mapId").SetValue(Workspace.Project.Maps.First(m=>m.Id!="romulus-outpost").Id);}
                else if(Stage==3){t.Field("mapId").SetValue("romulus-outpost");t.Field("libraryTab").SetValue("Scene");}
                else {
                    VerifyCaches();
                    var errors=Log.Messages.Where(x=>x.type==LogMessageType.Error).Select(x=>x.text).ToList();
                    File.WriteAllLines(Path.Combine(Folder,"errors.txt"),errors);
                    if(errors.Count>0)throw new Exception("Native editor logged errors.");
                    File.WriteAllText(Path.Combine(Folder,"complete.txt"),"PASS");Stage=99;Application.Quit();
                }
            }catch(Exception ex){Fail(ex);}
        }
        static void VerifyCaches(){
            var w=new Workspace();var t=Traverse.Create(w);
            void Call(string name,params object[] args)=>t.Method(name,args).GetValue();
            void Check(bool ok,string name){File.AppendAllText(Path.Combine(Folder,"checks.txt"),(ok?"PASS ":"FAIL ")+name+"\n");if(!ok)throw new Exception(name);}
            Placement Lookup(string id)=>t.Method("FindPlacement",id).GetValue<Placement>();
            bool Has(int x,int z)=>t.Field("graphicLinks").GetValue<Dictionary<int,LinkFlags>>().ContainsKey(z*w.Map.Width+x);
            var wall=new Placement{Id="wall-probe",Kind="Thing",Def=new DefRef{Name="Wall"},Position=new Cell{X=10,Z=10}};
            Call("Edit",(Action<Project>)(_=>w.Map.Objects.Add(wall)));Call("EnsureGraphicLinks");
            int index=t.Field("sceneRevision").GetValue<int>(),links=t.Field("graphicLinksRevision").GetValue<int>();
            for(int i=0;i<100;i++){Call("EnsureGraphicLinks");Lookup(wall.Id);}
            Check(index==t.Field("sceneRevision").GetValue<int>()&&links==t.Field("graphicLinksRevision").GetValue<int>(),"100 idle requests reuse both indices without rebuilding");
            Check(ReferenceEquals(wall,Lookup(wall.Id))&&Has(10,10),"initial object and linked wall indexed");
            Check(EditorUI.Reference(w.Map,wall.Id)==wall.Id,"reference caption cache initially resolves object");
            Call("Edit",(Action<Project>)(_=>w.Map.Objects.Single(p=>p.Id==wall.Id).Name="Renamed wall"));
            Check(EditorUI.Reference(w.Map,wall.Id)=="Renamed wall","renaming invalidates reference caption cache");
            Call("UndoEdit",false);Check(EditorUI.Reference(w.Map,wall.Id)==wall.Id,"undo restores reference caption without stale name");
            t.Field("selection").SetValue(new HashSet<string>{wall.Id});t.Field("selectedId").SetValue(wall.Id);
            Call("BeginSelectionMove",new Cell{X=10,Z=10});Call("PreviewSelectionMove",new Cell{X=15,Z=12});Call("EnsureGraphicLinks");
            Check(Lookup(wall.Id).Position.X==15&&Has(15,12)&&!Has(10,10),"drag preview updates object and wall connections");
            Call("CancelPointerGestures");Call("EnsureGraphicLinks");
            Check(Lookup(wall.Id).Position.X==10&&Has(10,10)&&!Has(15,12),"cancel drag restores cached connections");
            Call("BeginSelectionMove",new Cell{X=10,Z=10});Call("PreviewSelectionMove",new Cell{X=15,Z=12});Call("FinishSelectionMove");Call("EnsureGraphicLinks");
            Check(Has(15,12)&&!Has(10,10),"committed drag updates connections");
            Call("UndoEdit",false);Call("EnsureGraphicLinks");Check(Lookup(wall.Id).Position.X==10&&Has(10,10),"undo invalidates old map reference");
            Call("UndoEdit",true);Call("EnsureGraphicLinks");Check(Lookup(wall.Id).Position.X==15&&Has(15,12),"redo restores current map index");
            Call("RemoveSelection",new HashSet<string>{wall.Id});Call("EnsureGraphicLinks");Check(Lookup(wall.Id)==null&&!Has(15,12),"deletion removes cached object and wall");
            Call("UndoEdit",false);Call("EnsureGraphicLinks");Check(Lookup(wall.Id)!=null&&Has(15,12),"undo deletion restores indices");
            Call("BeginPaint",wall,new Cell{X=20,Z=20});Call("EnsureGraphicLinks");Check(Has(20,20),"painting updates preview before commit");Call("FinishPaint");
            var other=new MapSpec{Id="other"};w.Project.Maps.Add(other);t.Field("mapId").SetValue(other.Id);Call("EnsureGraphicLinks");Check(Lookup(wall.Id)==null&&!Has(15,12),"map switch releases previous map index");
            t.Field("mapId").SetValue(w.Project.Maps[0].Id);Call("EnsureGraphicLinks");Check(Lookup(wall.Id)!=null&&Has(15,12),"return to edited map rebuilds correct index");
        }
        static void Fail(Exception ex){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"complete.txt"),"FAIL "+ex);Stage=99;Application.Quit();}
    }
    [HarmonyPatch(typeof(Workspace),nameof(Workspace.DoWindowContents))]
    static class EditorPerformanceWindow {
        struct SampleState {public long Time,Bytes;public bool Measure;}
        static void Prefix(out SampleState __state){
            __state=default;
            if(!EditorPerformanceAudit.Enabled||Event.current.type!=EventType.Repaint)return;
            __state.Measure=true;__state.Bytes=GC.GetAllocatedBytesForCurrentThread();__state.Time=Stopwatch.GetTimestamp();
        }
        static void Postfix(SampleState __state){if(__state.Measure)EditorPerformanceAudit.Sample((Stopwatch.GetTimestamp()-__state.Time)*1000.0/Stopwatch.Frequency,GC.GetAllocatedBytesForCurrentThread()-__state.Bytes);}
    }
}
