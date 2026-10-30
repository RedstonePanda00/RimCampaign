using System;
using System.Linq;

namespace RimCampaign.Core {
    // Shared by editor overlays and runtime detection. A Rule never spawns a Thing.
    public static class TriggerGeometry {
        public static bool Visible(Rule rule)=>rule.ShowOnMap||Spatial(rule);
        public static bool Spatial(Rule rule)=>rule.Events.Any(e=>e.Kind=="RangeEnter"||e.Kind=="RangeLeave");
        public static bool Contains(Rule rule,int centerX,int centerZ,int x,int z){
            double dx=x-centerX,dz=z-centerZ;
            return rule.RangeShape=="Rectangle"?Math.Abs(dx)<=rule.RangeWidth/2d&&Math.Abs(dz)<=rule.RangeHeight/2d:dx*dx+dz*dz<=rule.Radius*rule.Radius;
        }
        public static Rule Copy(Rule rule,int dx,int dz){
            var copy=Json.Copy(rule);copy.Id=Guid.NewGuid().ToString("N");copy.Name+=" 副本";copy.Position.X+=dx;copy.Position.Z+=dz;
            foreach(var a in Validation.Actions(copy.Actions)){a.Id=Guid.NewGuid().ToString("N");if(a.Target==rule.Id)a.Target=copy.Id;if(a.Origin==rule.Id)a.Origin=copy.Id;}
            return copy;
        }
    }
}
