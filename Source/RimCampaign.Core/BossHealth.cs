using System;
using System.Collections.Generic;

namespace RimCampaign.Core
{
    public sealed class BossDamageResult
    {
        public double Health;
        public int Phase;
        public bool Locked;
        public List<int> Entered = new List<int>();
    }
    public static class BossHealth
    {
        public static BossDamageResult Apply(BossTemplate template,double health,int phase,bool locked,double damage) {
            var result=new BossDamageResult{Health=health,Phase=phase,Locked=locked};
            if(locked||health<=0||damage<=0||double.IsNaN(damage)||double.IsInfinity(damage))return result;
            double target=Math.Max(0,health-damage);
            for(int i=phase+1;i<template.Phases.Count;i++) {
                double boundary=template.MaxHealth*template.Phases[i].HealthFraction;
                if(target>boundary)break;
                result.Phase=i;result.Entered.Add(i);
                if(template.Phases[i].LockHealth){target=boundary;result.Locked=true;break;}
            }
            result.Health=target;return result;
        }
    }
}
