using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;
using UnityEngine;
using Verse;
namespace RimCampaign.Editor {
 public sealed class TaskTreesWindow:Window {
  readonly List<ActionGroup> trees;readonly Project project;readonly MapSpec map;readonly Action apply;int selected;Vector2 scroll;
  public override Vector2 InitialSize=>new Vector2(Math.Min(1080,UI.screenWidth-50),Math.Min(780,UI.screenHeight-50));
  public TaskTreesWindow(List<ActionGroup> trees,Project project,MapSpec map,Action apply){this.trees=trees;this.project=project;this.map=map;this.apply=apply;doCloseX=true;absorbInputAroundWindow=true;}
  public override void PostClose(){apply();base.PostClose();}
  public override void DoWindowContents(Rect r){
   if(EditorUI.Button(new Rect(0,0,240,32),"＋ 任务树 / Add tree")){trees.Add(new ActionGroup{Name="新任务树 / New task tree"});selected=trees.Count-1;}
   for(int i=0;i<trees.Count;i++){int index=i;if(EditorUI.Button(new Rect(0,42+i*37,240,32),trees[i].Name))selected=index;}
   if(trees.Count==0)return;selected=Math.Min(selected,trees.Count-1);var tree=trees[selected];float x=254,w=r.width-x;
   tree.Name=Widgets.TextField(new Rect(x,0,w-50,30),tree.Name);
   if(EditorUI.Button(new Rect(x+w-45,0,40,30),"×")){trees.RemoveAt(selected);return;}
   if(EditorUI.Button(new Rect(x,40,w,32),"绑定 / Actor: "+(string.IsNullOrEmpty(tree.Actor)?"无 / None":EditorUI.Reference(map,tree.Actor))))EditorUI.Pick(new[]{new KeyValuePair<string,string>("","无绑定 / None"),new KeyValuePair<string,string>("$source","本次单位 / Event actor")}.Concat(map.Roles.Select(a=>new KeyValuePair<string,string>(a,a))).Concat(map.Objects.Where(o=>o.Kind=="Pawn"||o.Kind=="PawnKind").Select(o=>new KeyValuePair<string,string>(o.Id,o.Name))),v=>tree.Actor=v);
   if(EditorUI.Button(new Rect(x,80,w,30),"＋ 顺序节点 / Add step"))tree.Actions.Add(new Act{Kind="WaitUntil",Name="等待条件 / Await condition"});
   var rows=new List<Tuple<List<Act>,Act,int>>();Flatten(tree.Actions,0,rows);
   Widgets.BeginScrollView(new Rect(x,120,w,r.height-130),ref scroll,new Rect(0,0,w-20,rows.Count*42));
   for(int row=0;row<rows.Count;row++){var entry=rows[row];var list=entry.Item1;var a=entry.Item2;int depth=entry.Item3,index=list.IndexOf(a);float y=row*42,left=depth*20;
    if(EditorUI.Button(new Rect(left,y,w-left-155,34),EditorUI.Describe(a,map)))Find.WindowStack.Add(new ObjectWindow(a,v=>list[index]=(Act)v,project,map));
    if(EditorUI.Button(new Rect(w-150,y,30,34),"↑")&&index>0){list.RemoveAt(index);list.Insert(index-1,a);}
    if(EditorUI.Button(new Rect(w-115,y,30,34),"↓")&&index<list.Count-1){list.RemoveAt(index);list.Insert(index+1,a);}
    if(new[]{"Sequence","Parallel","If"}.Contains(a.Kind)&&EditorUI.Button(new Rect(w-80,y,30,34),"+"))a.Steps.Add(new Act{Kind="WaitUntil",Name="等待条件 / Await condition"});
    if(EditorUI.Button(new Rect(w-45,y,25,34),"×"))list.Remove(a);
   }Widgets.EndScrollView();
  }
  static void Flatten(List<Act> list,int depth,List<Tuple<List<Act>,Act,int>> rows){if(depth>32)return;foreach(var a in list){rows.Add(Tuple.Create(list,a,depth));Flatten(a.Steps,depth+1,rows);Flatten(a.Else,depth+1,rows);}}
 }
}
