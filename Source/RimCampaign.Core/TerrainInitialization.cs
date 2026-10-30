using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace RimCampaign.Core
{
    public class TerrainInitialization
    {
        public DefRef Biome { get; set; } = new DefRef {Kind="BiomeDef",Name="TemperateForest"};
        public DefRef SecondaryBiome { get; set; }
        public string Seed { get; set; } = "12345";
        public float PatchSize { get; set; } = 18;
        public float SecondaryFraction { get; set; } = .5f;
        public float Temperature { get; set; } = 21;
        public float Vegetation { get; set; } = 1;
        public float Mountains { get; set; } = .22f;
        [OnDeserializing] void Defaults(StreamingContext context){Vegetation=1;Mountains=.22f;}
    }
    // Stable, spatially coherent noise, independent of Unity/Rand and process hash randomization.
    public static class TerrainNoise
    {
        public static int Seed(string text) { unchecked {int h=17;foreach(char c in text??"")h=h*31+c;return h;} }
        static double Hash(int x,int z,int seed) {unchecked {uint n=(uint)(x*374761393+z*668265263+seed);n=(n^(n>>13))*1274126177;return (n^(n>>16))/(double)uint.MaxValue;}}
        public static double Sample(double x,double z,int seed) {
            int a=(int)Math.Floor(x),b=(int)Math.Floor(z);double u=x-a,v=z-b;u=u*u*(3-2*u);v=v*v*(3-2*v);
            double first=Hash(a,b,seed)*(1-u)+Hash(a+1,b,seed)*u,second=Hash(a,b+1,seed)*(1-u)+Hash(a+1,b+1,seed)*u;
            return first*(1-v)+second*v;
        }
        public static bool Secondary(TerrainInitialization s,int x,int z,int width) =>
            s.SecondaryBiome!=null && (x+.5)/width + (Sample(x/(double)s.PatchSize,z/(double)s.PatchSize,Seed(s.Seed)+73)-.5)*.3 > 1-s.SecondaryFraction;
        public static IEnumerable<string> Errors(TerrainInitialization s) {
            if(s==null)yield break;
            if(s.Biome?.Kind!="BiomeDef"||string.IsNullOrWhiteSpace(s.Biome.Name))yield return "请选择主生态。";
            if(s.SecondaryBiome!=null&&(s.SecondaryBiome.Kind!="BiomeDef"||string.IsNullOrWhiteSpace(s.SecondaryBiome.Name)))yield return "混合生态需要有效的副生态。";
            if(float.IsNaN(s.PatchSize)||float.IsInfinity(s.PatchSize)||s.PatchSize<4||s.PatchSize>100)yield return "地形斑块尺寸必须为 4–100。";
            if(float.IsNaN(s.SecondaryFraction)||s.SecondaryFraction<.1f||s.SecondaryFraction>.9f)yield return "副生态比例必须为 0.1–0.9。";
            if(float.IsNaN(s.Temperature)||float.IsInfinity(s.Temperature)||s.Temperature< -100||s.Temperature>100)yield return "温度必须为 -100–100。";
            if(float.IsNaN(s.Vegetation)||float.IsInfinity(s.Vegetation)||s.Vegetation<0||s.Vegetation>2)yield return "植被密度必须为 0–2。";
            if(float.IsNaN(s.Mountains)||s.Mountains<0||s.Mountains>.7f)yield return "山体强度必须为 0–0.7。";
        }
    }
}
