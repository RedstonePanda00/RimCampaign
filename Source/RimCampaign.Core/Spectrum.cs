using System;
using System.Collections.Generic;
using System.Globalization;

namespace RimCampaign.Core
{
    public sealed class CommunicationSpectrumSpec
    {
        public bool Enabled { get; set; }
        public int BarCount { get; set; } = 67;
        public float Width { get; set; } = 340;
        public float Height { get; set; } = 56;
        public string Color { get; set; } = "#FFFFFF";
        // Text-only dialogue explicitly opts into a synthetic signal, not an audio FFT.
        public bool SimulateSpeech { get; set; }
        public IEnumerable<string> Errors()
        {
            if(BarCount<3||BarCount>256)yield return "Spectrum bar count must be between 3 and 256.";
            if(!(Width>=32&&Width<=1000&&Height>=8&&Height<=100))yield return "Invalid spectrum dimensions.";
            if(!TryColor(Color,out _))yield return "Spectrum color must be #RRGGBB or #RRGGBBAA.";
        }
        public static bool TryColor(string value,out uint rgba)
        {
            rgba=0;
            if(value==null||(value.Length!=7&&value.Length!=9)||value[0]!='#'||!uint.TryParse(value.Substring(1),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var parsed))return false;
            rgba=value.Length==7?(parsed<<8)|255:parsed;return true;
        }
    }

    // Reusable signal envelope. Call once per presentation update, never to advance story time.
    public sealed class SpectrumSignal
    {
        readonly float[] levels;
        readonly double[] centers=new double[5],widths=new double[5],heights=new double[5],phases=new double[5];
        public IReadOnlyList<float> Levels=>levels;
        public SpectrumSignal(int count,int? seed=null)
        {
            if(count<3||count>256)throw new ArgumentOutOfRangeException(nameof(count));
            levels=new float[count];for(int i=0;i<count;i++)levels[i]=.035f;
            var random=new Random(seed??Guid.NewGuid().GetHashCode());
            for(int p=0;p<5;p++){
                centers[p]=-.76+p*.38+(random.NextDouble()-.5)*.1;
                widths[p]=.065+random.NextDouble()*.035;
                heights[p]=p==2?.95:.35+random.NextDouble()*.4;
                phases[p]=random.NextDouble()*Math.PI*2;
            }
        }
        public void Update(double seconds,double delta,bool simulateSpeech,IReadOnlyList<float> samples=null)
        {
            if(double.IsNaN(seconds)||double.IsInfinity(seconds))seconds=0;
            if(double.IsNaN(delta)||double.IsInfinity(delta)||delta<=0)return;
            delta=Math.Min(delta,.1);
            for(int i=0;i<levels.Length;i++){
                double target=.035;
                if(samples!=null&&samples.Count>0){
                    // Bin maxima preserve narrow peaks when the input has more frequency bins.
                    int from=i*samples.Count/levels.Length,to=Math.Max(from+1,(i+1)*samples.Count/levels.Length);
                    for(int j=from;j<Math.Min(to,samples.Count);j++)if(!float.IsNaN(samples[j])&&!float.IsInfinity(samples[j]))target=Math.Max(target,Math.Min(1,samples[j]));
                }else if(simulateSpeech){
                    double x=(i-(levels.Length-1)*.5)/((levels.Length-1)*.5);
                    double peak=.035;
                    for(int p=0;p<centers.Length;p++){
                        double center=centers[p]+.025*Math.Sin(seconds*1.7+phases[p]);
                        double distance=(x-center)/widths[p];
                        double pulse=.6+.4*(.5+.5*Math.Sin(seconds*(3.1+p*.37)+phases[p]));
                        peak+=heights[p]*pulse*Math.Exp(-.5*distance*distance);
                    }
                    double detail=.8+.2*(.5+.5*Math.Sin(i*1.73+seconds*5.1+phases[0]));
                    target=Math.Max(.035,Math.Min(1,peak*detail));
                }
                double response=target>levels[i]?24:9;
                levels[i]+=(float)((target-levels[i])*(1-Math.Exp(-delta*response)));
            }
        }
    }
}
