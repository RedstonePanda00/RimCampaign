using System;
using System.Collections.Generic;

namespace RimCampaign.Core
{
    public static class SurfaceBrush
    {
        public static List<Cell> Cells(Cell start,Cell end,int width,int height,string shape){
            int x1=Math.Min(start.X,end.X),x2=Math.Max(start.X,end.X),z1=Math.Min(start.Z,end.Z),z2=Math.Max(start.Z,end.Z);
            double cx=(x1+x2)/2d,cz=(z1+z2)/2d,rx=(x2-x1+1)/2d,rz=(z2-z1+1)/2d;
            bool Inside(int x,int z)=>x>=x1&&x<=x2&&z>=z1&&z<=z2&&Math.Pow((x-cx)/rx,2)+Math.Pow((z-cz)/rz,2)<=1.000001;
            var result=new List<Cell>();
            for(int z=Math.Max(0,z1);z<=Math.Min(height-1,z2);z++)for(int x=Math.Max(0,x1);x<=Math.Min(width-1,x2);x++){
                bool include=shape=="HollowRectangle"?x==x1||x==x2||z==z1||z==z2:
                    shape=="Ellipse"?Inside(x,z):shape=="Ring"?Inside(x,z)&&(!Inside(x-1,z)||!Inside(x+1,z)||!Inside(x,z-1)||!Inside(x,z+1)):true;
                if(include)result.Add(new Cell{X=x,Z=z});
            }
            return result;
        }
    }
}
