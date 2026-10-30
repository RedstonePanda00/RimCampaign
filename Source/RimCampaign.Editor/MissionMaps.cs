using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimCampaign.Core;
using RimCampaign.Runtime;

namespace RimCampaign.Editor {
    public partial class Workspace {
        void InitializePocketMission(){
            Edit(p=>{
                Map.Base="PocketMission";Map.TerrainInitialization=null;
                Map.Objects.RemoveAll(o=>TerrainInitializer.Generated(o));
                if(!p.Campaigns.SelectMany(c=>c.Nodes).Any(n=>n.Map==Map.Id)){
                    var node=new Node{Name=Map.Name,Map=Map.Id};
                    p.Campaigns.Add(new Campaign{Name=Map.Name,Start=node.Id,Nodes=new List<Node>{node}});
                }
            });
            status="已初始化为袖珍地图：自由编辑完整任务；运行时生态由任务所在地格决定。";
        }
        internal MissionMapSpec CaptureMissionMap(){
            if(Map.Base!="PocketMission")throw new InvalidOperationException("请先在地图设置 → 生成中选择袖珍地图初始化。");
            var map=Json.Copy(Map);map.TerrainInitialization=null;
            map.Objects.RemoveAll(TerrainInitializer.Generated);
            var matching=Project.Campaigns.SelectMany(c=>c.Nodes).Where(n=>n.Map==Map.Id).ToList();
            if(matching.Count>1)throw new InvalidOperationException("当前地图有多个战役节点，请明确唯一任务节点。");
            var node=matching.Count==1?Json.Copy(matching[0]):new Node{Name=map.Name,Map=map.Id};
            node.Kind="Story";node.Results=null;node.RefillNeedsOnSuccess=false;
            var project=new Project{Name=map.Name,PackageId=Project.PackageId,Author=Project.Author,Maps=new List<MapSpec>{map},
                Campaigns=new List<Campaign>{new Campaign{Name=map.Name,Start=node.Id,Nodes=new List<Node>{node}}},
                Profiles=Json.Copy(Project.Profiles),Bosses=Json.Copy(Project.Bosses),Structures=Json.Copy(Project.Structures??new List<StructureSpec>()),
                ExtraReferences=Json.Copy(Project.ExtraReferences),ManualDependencies=Json.Copy(Project.ManualDependencies),ManualLoadAfter=Json.Copy(Project.ManualLoadAfter),CustomAssemblies=Json.Copy(Project.CustomAssemblies)};
            var mission=new MissionMapSpec{Id=map.Id,Name=map.Name,Project=project};MissionMaps.Validate(mission);return mission;
        }
        void ExportMissionMap(){
            var mission=CaptureMissionMap();
            Prompt("任务地图文件",Path.Combine(WorkRoot,"MissionMaps",MissionMaps.DefName(mission)+".rcmission.json"),file=>{
                MissionMaps.Export(Path.GetFullPath(file),mission);
                status="已导出任务地图：JSON 放入模组 MissionMaps，配套 XML 放入 Defs；任务可引用 MissionMapDef 创建据点并接收完成信号。";
            });
        }
    }
}
