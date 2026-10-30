using System;
using System.Linq;
using RimCampaign.Core;

static class SpectrumTests
{
    public static void Run(Action<bool,string> check)
    {
        var spec=new CommunicationSpectrumSpec{Enabled=true,SimulateSpeech=true,Color="#FFD447"};
        var restored=Json.Copy(new CommunicationSpec{Spectrum=spec});
        check(restored.Spectrum.BarCount==67&&restored.Spectrum.Color=="#FFD447"&&!restored.Errors().Any(),"spectrum settings survive campaign serialization");
        spec.BarCount=0;check(spec.Errors().Any(),"spectrum rejects invalid bar counts");spec.BarCount=67;
        check(CommunicationSpectrumSpec.TryColor("#FFD44780",out var rgba)&&rgba==0xFFD44780&&!CommunicationSpectrumSpec.TryColor("#ZZ0000",out _),"spectrum validates configurable color and alpha");
        var signal=new SpectrumSignal(67,42);
        for(int i=0;i<120;i++)signal.Update(i/60d,1/60d,true);
        check(signal.Levels.Skip(27).Take(13).Max()>signal.Levels[0]*3,"synthetic speech retains a prominent central peak");
        check(Enumerable.Range(1,65).Count(i=>signal.Levels[i]>signal.Levels[i-1]&&signal.Levels[i]>signal.Levels[i+1])>=4,"synthetic speech has several separated peaks");
        var same=new SpectrumSignal(67,42);var different=new SpectrumSignal(67,73);
        for(int i=0;i<120;i++){same.Update(i/60d,1/60d,true);different.Update(i/60d,1/60d,true);}
        check(same.Levels.SequenceEqual(signal.Levels)&&!different.Levels.SequenceEqual(signal.Levels),"seeds produce repeatable tests and distinct playback envelopes");
        var freshA=new SpectrumSignal(67);var freshB=new SpectrumSignal(67);freshA.Update(1,.1,true);freshB.Update(1,.1,true);
        check(!freshA.Levels.SequenceEqual(freshB.Levels),"each new playback randomizes its envelope without consuming game RNG");
        var before=signal.Levels.ToArray();signal.Update(99,0,true);
        check(signal.Levels.SequenceEqual(before),"zero elapsed time cannot advance spectrum smoothing");
        for(int i=0;i<120;i++)signal.Update(2+i/60d,1/60d,false);
        check(signal.Levels.All(v=>Math.Abs(v-.035f)<.001f),"silence decays to the baseline");
        var samples=new float[134];samples[8]=1;samples[50]=float.NaN;samples[51]=float.PositiveInfinity;
        for(int i=0;i<60;i++)signal.Update(4+i/60d,1/60d,false,samples);
        check(signal.Levels[4]>.99&&signal.Levels[33]<.04&&signal.Levels.All(v=>!float.IsNaN(v)&&v>=0&&v<=1),"external spectrum preserves off-center audio peaks and ignores nonfinite samples");
        var coarse=new SpectrumSignal(67);coarse.Update(0,.1,false,new[]{0f,1f,0f});
        check(coarse.Levels[33]>.5&&coarse.Levels[0]<.04,"short external sample arrays resample without out-of-range access");
    }
}
