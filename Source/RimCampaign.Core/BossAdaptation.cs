using System;
namespace RimCampaign.Core
{
    public static class BossAdaptation
    {
        // Shifted logistic sigmoid: C1 at the identity boundary, asymptotic at ceiling.
        public static double Compress(double damage,double threshold=100,double ceiling=500)
        {
            if(!double.IsFinite(threshold)||!double.IsFinite(ceiling)||threshold<0||ceiling<=threshold)
                throw new ArgumentOutOfRangeException(nameof(ceiling),"Require 0 <= threshold < ceiling, both finite.");
            if(double.IsNaN(damage)||damage<=0)return 0;
            if(damage<=threshold)return damage;
            return threshold+(ceiling-threshold)*Math.Tanh((damage-threshold)/(ceiling-threshold));
        }
    }
}
