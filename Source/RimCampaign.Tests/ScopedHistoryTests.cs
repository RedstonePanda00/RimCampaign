using System;
using System.Collections.Generic;
using System.Linq;
using RimCampaign.Core;

static class ScopedHistoryTests {
 public static void Run(Action<bool,string> check){
  var h=new History<Project>(new Project{Maps=new List<MapSpec>{new MapSpec{Id="map",Name="before"}}});
  void Scoped(string value)=>h.EditScope(p=>p.Maps.Single(m=>m.Id=="map").Name,(p,s)=>p.Maps.Single(m=>m.Id=="map").Name=s,p=>p.Maps[0].Name=value);
  Scoped("scope1");h.Edit(p=>p.Name="full");Scoped("scope2");
  h.Undo();check(h.Value.Name=="full"&&h.Value.Maps[0].Name=="scope1","scoped undo preserves unrelated fields");
  h.Undo();check(h.Value.Name==""&&h.Value.Maps[0].Name=="scope1","full undo mixed with scoped entries");
  h.Undo();check(h.Value.Maps[0].Name=="before","scope resolves current document after full replacement");
  h.Redo();h.Redo();h.Redo();check(h.Value.Name=="full"&&h.Value.Maps[0].Name=="scope2","mixed redo sequence restores final state");
  h.Undo();int before=h.UndoCount;Scoped("scope1");check(h.UndoCount==before&&h.RedoCount==1,"no-op scope preserves history branch");
  try{h.EditScope(p=>p.Maps[0].Name,(p,s)=>p.Maps[0].Name=s,p=>{p.Maps[0].Name="broken";throw new InvalidOperationException();});}catch(InvalidOperationException){}
  check(h.Value.Maps[0].Name=="scope1"&&h.RedoCount==1,"failed scoped edit restores state and history");
  Scoped("branch");check(h.RedoCount==0,"successful scope clears redo branch");
  var bounded=new History<List<int>>(new List<int>{0},maxEntries:3,maxBytes:1000);
  for(int i=1;i<=20;i++)bounded.EditScope(p=>p[0],(p,s)=>p[0]=s,p=>p[0]=i);
  check(bounded.UndoCount==3&&bounded.RetainedBytes<=1000,"scoped history obeys entry and byte limits");
  for(int i=0;i<3;i++)bounded.Undo();check(bounded.Value[0]==17,"bounded scope undo order");
 }
}
