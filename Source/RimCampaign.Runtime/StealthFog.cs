using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace RimCampaign.Runtime
{
    public sealed partial class StealthMapComponent
    {
        static readonly Material MemoryShade=new Material(SolidColorMaterials.SimpleSolidColorMaterial(new Color(.25f,.27f,.29f,.55f))){renderQueue=3600};
        static readonly Material UnknownShade=new Material(SolidColorMaterials.SimpleSolidColorMaterial(Color.black)){renderQueue=3600};
        readonly List<Mesh> memoryMeshes=new List<Mesh>();
        readonly List<Mesh> unknownMeshes=new List<Mesh>();
        bool memoryShadeDirty=true;
        public override void MapRemoved(){ClearFogMeshes();base.MapRemoved();}
        void ClearFogMeshes(){foreach(var mesh in memoryMeshes)Object.Destroy(mesh);memoryMeshes.Clear();foreach(var mesh in unknownMeshes)Object.Destroy(mesh);unknownMeshes.Clear();}
        void DrawRememberedFog(){
            if(!Config.DynamicFog)return;
            if(memoryShadeDirty){RebuildMemoryShade();memoryShadeDirty=false;}
            foreach(var mesh in memoryMeshes)Graphics.DrawMesh(mesh,Matrix4x4.identity,MemoryShade,0);
            foreach(var mesh in unknownMeshes)Graphics.DrawMesh(mesh,Matrix4x4.identity,UnknownShade,0);
        }
        void RebuildMemoryShade(){
            ClearFogMeshes();
            BuildFogMesh(memoryMeshes,false);BuildFogMesh(unknownMeshes,true);
        }
        void BuildFogMesh(List<Mesh> destination,bool unknown){
            var vertices=new List<Vector3>();var indices=new List<int>();var uv=new List<Vector2>();
            float altitude=AltitudeLayer.FogOfWar.AltitudeFor()+.01f;
            void Finish(){
                if(vertices.Count==0)return;
                var mesh=new Mesh{name=unknown?"RimCampaign unexplored terrain":"RimCampaign remembered terrain"};
                mesh.vertices=vertices.ToArray();mesh.triangles=indices.ToArray();mesh.uv=uv.ToArray();mesh.RecalculateBounds();
                destination.Add(mesh);vertices.Clear();indices.Clear();uv.Clear();
            }
            bool Covered(int x,int z)=>unknown?!WasExplored(new IntVec3(x,0,z)):Remembered(new IntVec3(x,0,z));
            // Merge adjacent cells along each row; build only when visibility changes.
            for(int z=0;z<map.Size.z;z++)for(int x=0;x<map.Size.x;){
                if(!Covered(x,z)){x++;continue;}
                int start=x;while(x<map.Size.x&&Covered(x,z))x++;
                if(vertices.Count>=60000)Finish();int v=vertices.Count;
                vertices.Add(new Vector3(start,altitude,z));vertices.Add(new Vector3(start,altitude,z+1));
                vertices.Add(new Vector3(x,altitude,z+1));vertices.Add(new Vector3(x,altitude,z));
                uv.Add(new Vector2(0,0));uv.Add(new Vector2(0,1));uv.Add(new Vector2(1,1));uv.Add(new Vector2(1,0));
                indices.Add(v);indices.Add(v+1);indices.Add(v+2);indices.Add(v);indices.Add(v+2);indices.Add(v+3);
            }
            Finish();
        }
    }
}
