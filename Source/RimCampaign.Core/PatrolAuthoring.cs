using System.Linq;
namespace RimCampaign.Core
{
    public static class PatrolAuthoring
    {
        public static void Synchronize(MapSpec map){
            var wanted=map.PatrolRoutes.Concat(map.Stealth.Routes).SelectMany(r=>r.Points).Select(p=>p.Marker).Where(id=>!string.IsNullOrEmpty(id)).ToHashSet();
            if(wanted.Count==0)return;
            var markers=map.Objects.Where(o=>wanted.Contains(o.Id)).GroupBy(o=>o.Id).ToDictionary(g=>g.Key,g=>g.First());
            foreach(var route in map.PatrolRoutes.Concat(map.Stealth.Routes)){
                route.Points.RemoveAll(p=>!string.IsNullOrEmpty(p.Marker)&&!markers.ContainsKey(p.Marker));
                foreach(var point in route.Points.Where(p=>!string.IsNullOrEmpty(p.Marker)))
                { var marker=markers[point.Marker];point.Position=new Cell{X=marker.Position.X,Z=marker.Position.Z};point.Facing=marker.Rotation; }
            }
        }
    }
}
