using System;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimCampaign.Core;
using RimWorld;
using UnityEngine;
using Verse;
namespace RimCampaign.Editor {
 // Isolated native editor acceptance, enabled only by an explicit command-line flag.
 [HarmonyPatch(typeof(Root),nameof(Root.Update))]
 static class TaskTreeAudit {
  static int stage,frames;static Workspace workspace;static TaskTreesWindow window;
  static string Folder=>Path.Combine(GenFilePaths.SaveDataFolderPath,"TaskTreeEditorAudit");
  static void Postfix(){
   if(!GenCommandLine.CommandLineArgPassed("rc-task-tree-audit")||Current.ProgramState!=ProgramState.Entry||stage>=9)return;
   try{
    if(stage==0){
     Directory.CreateDirectory(Folder);var path=Environment.GetCommandLineArgs().First(a=>a.StartsWith("-rc-audit-project=")).Substring("-rc-audit-project=".Length);var project=ProjectFiles.Load(path);workspace=new Workspace();var t=Traverse.Create(workspace);t.Field("history").SetValue(new History<Project>(project));t.Field("mapId").SetValue("romulus-sanctuary");t.Field("path").SetValue(Path.Combine(Folder,"hot-test.project.json.gz"));Find.WindowStack.Add(workspace);stage=1;frames=0;return;
    }
    if(++frames<45)return;frames=0;
    if(stage==1){foreach(var log in Find.WindowStack.Windows.Where(w=>w.GetType().Name=="EditWindow_Log").ToList())log.Close();Traverse.Create(workspace).Field("mapView").GetValue<MapViewport>().Fit();stage=2;return;}
    if(stage==2){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"01-map-editor.png"));stage=3;return;}
    if(stage==3){window=new TaskTreesWindow(Json.Copy(workspace.Map.Groups),workspace.Project,workspace.Map,()=>{});Find.WindowStack.Add(window);stage=4;return;}
    if(stage==4){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"02-task-trees.png"));stage=5;return;}
    if(stage==5){window.Close();AccessTools.Method(typeof(Workspace),"FocusObject").Invoke(workspace,new object[]{workspace.Map.Rules.Single(r=>r.Id=="m3-empire-refill-1")});stage=6;return;}
    if(stage==6){ScreenCapture.CaptureScreenshot(Path.Combine(Folder,"03-reinforcement-handle.png"));stage=7;return;}
    if(stage==7){File.WriteAllText(Path.Combine(Folder,"results.txt"),"PASS native map editor and task-tree window rendered.");stage=9;if(GenCommandLine.CommandLineArgPassed("rc-task-tree-preview"))Application.Quit();else Traverse.Create(workspace).Method("HotTest").GetValue();}
   }catch(Exception e){stage=99;Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"failure.txt"),e.ToString());Application.Quit();}
  }
 }
}
