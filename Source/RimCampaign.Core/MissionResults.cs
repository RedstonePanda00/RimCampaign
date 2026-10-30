using System;
using System.Collections.Generic;

namespace RimCampaign.Core
{
    public class ResultSpec
    {
        public string Title { get; set; } = "";
        public string Heading { get; set; } = "任务完成";
        public string Faction { get; set; } = "";
        public string Material { get; set; } = "";
        public string PresentationType { get; set; } = "";
        public bool StealthRating { get; set; }
        public int DetectionPenalty { get; set; } = 15;
        public int ReportPenalty { get; set; } = 10;
        public int KillPenalty { get; set; } = 8;
    }
    // Unique witnesses/victims: revisiting a cone, repeat callbacks and loading
    // the same save cannot farm takedowns or charge the same witness twice.
    public class MissionPerformance
    {
        public List<string> DetectedBy { get; set; } = new List<string>();
        public List<string> Reporters { get; set; } = new List<string>();
        public List<string> Killed { get; set; } = new List<string>();
        public List<string> Subdued { get; set; } = new List<string>();
        public void Record(string kind,string id)
        {
            var list=kind=="detected"?DetectedBy:kind=="report"?Reporters:kind=="kill"?Killed:kind=="subdue"?Subdued:null;
            if(list!=null&&!string.IsNullOrEmpty(id)&&!list.Contains(id))list.Add(id);
        }
        public int SurvivingSubdued=>Subdued.FindAll(id=>!Killed.Contains(id)).Count;
        public int Score(ResultSpec spec)=>Math.Max(0,100-DetectedBy.Count*Math.Max(0,spec.DetectionPenalty)-Reporters.Count*Math.Max(0,spec.ReportPenalty)-Killed.Count*Math.Max(0,spec.KillPenalty));
        public string Rank(ResultSpec spec){int score=Score(spec);return score==100?"S":score>=85?"A":score>=65?"B":score>=40?"C":"D";}
    }
    public class ResultRow
    {
        public string Label { get; set; } = "";
        public string Value { get; set; } = "";
    }
    public class MissionResult
    {
        public string Node { get; set; } = "";
        public string Title { get; set; } = "";
        public string Summary { get; set; } = "";
        public double Seconds { get; set; }
        public MissionPerformance Performance { get; set; } = new MissionPerformance();
        public List<ResultRow> Objectives { get; set; } = new List<ResultRow>();
        public List<ResultRow> Cargo { get; set; } = new List<ResultRow>();
    }
}
