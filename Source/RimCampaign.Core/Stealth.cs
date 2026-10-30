using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace RimCampaign.Core
{
    // Authoring data contains no game objects. A map opts in; ordinary maps stay untouched.
    public class StealthSpec
    {
        public bool Enabled { get; set; }
        public bool DynamicFog { get; set; } = true;
        public bool PartialCoverConcealsFromCalmGuards { get; set; }
        public string Actor { get; set; } = "hero";
        public float PlayerSight { get; set; } = 16;
        public bool PlayerCornerPeek { get; set; }
        public bool PlayerHearing { get; set; }
        public int UpdateTicks { get; set; } = 6;
        public bool AllowTakedown { get; set; } = true;
        public int TakedownTicks { get; set; } = 120;
        public float GunshotRadius { get; set; } = 22;
        public List<PatrolRoute> Routes { get; set; } = new List<PatrolRoute>();
        public List<GuardSpec> Guards { get; set; } = new List<GuardSpec>();
        public List<DeviceSpec> Devices { get; set; } = new List<DeviceSpec>();
        public List<PatrolConversation> Conversations { get; set; } = new List<PatrolConversation>();
        [OnDeserialized] void RestoreConversations(StreamingContext context){if(Conversations==null)Conversations=new List<PatrolConversation>();}
    }
    public class PatrolRoute : Named
    {
        public bool PingPong { get; set; }
        public List<PatrolPoint> Points { get; set; } = new List<PatrolPoint>();
    }
    public class PatrolPoint
    {
        public string Marker { get; set; } = "";
        public Cell Position { get; set; } = new Cell();
        public int Facing { get; set; }
        public int WaitTicks { get; set; } = 90;
        public bool Checkpoint { get; set; } = true;
        [OnDeserializing] void NewDefaults(StreamingContext context){Checkpoint=true;}
    }
    public class GuardSpec : Named
    {
        public string Pawn { get; set; } = "";
        public string Route { get; set; } = "";
        public float SightRange { get; set; } = 14;
        public float ViewAngle { get; set; } = 100;
        public float HearingMultiplier { get; set; } = 1;
        public int DetectionTicks { get; set; } = 90;
        public float FarDetectionMultiplier { get; set; } = .2f;
        public int FullSuspicionHoldTicks { get; set; } = 180;
        [OnDeserializing] void NewDefaults(StreamingContext context){FarDetectionMultiplier=.2f;FullSuspicionHoldTicks=180;}
        public int SearchTicks { get; set; } = 600;
        public float SearchRadius { get; set; } = 6;
        public string RadioChannel { get; set; } = "security";
        public float RadioRange { get; set; } = 30;
        public int RadioDelayTicks { get; set; } = 90;
        public string Uplink { get; set; } = "";
        public int ReportTicks { get; set; } = 240;
    }
    public class DeviceSpec : Named
    {
        public string CommandLabel { get; set; } = "";
        public string RequiredVariable { get; set; } = "";
        public string RequirementText { get; set; } = "";
        public bool OneShot { get; set; }
        public string Kind { get; set; } = "Camera";
        public string Host { get; set; } = "";
        public Cell Position { get; set; } = new Cell();
        public int Facing { get; set; }
        public float SightRange { get; set; } = 14;
        public float ViewAngle { get; set; } = 90;
        public float Radius { get; set; } = 12;
        public int UseTicks { get; set; } = 120;
        public int DetectionTicks { get; set; } = 180;
        public int ReportTicks { get; set; } = 300;
        public string Uplink { get; set; } = "";
        public string PasswordRule { get; set; } = "";
        public string Signal { get; set; } = "";
        public bool StartsCaptured { get; set; }
        public bool AutoReport { get; set; }
        public bool OutdoorsOnly { get; set; }
        public static string KindForDef(string name)=>name=="RC_Camera"?"Camera":name=="RC_Broadcast"?"Broadcast":name=="RC_Uplink"?"Uplink":name=="RC_Terminal"?"Password":name=="RC_SupplyCrate"?"SupplyCrate":null;
    }
    public class GuardMemory
    {
        public string Id { get; set; } = "";
        public string Mode { get; set; } = "Patrol";
        public string ScriptedAction { get; set; } = "";
        public double Suspicion { get; set; }
        public int FullSuspicionUntil { get; set; }
        public bool Alarmed { get; set; }
        public bool Identified { get; set; }
        public Cell LastKnown { get; set; }
        public int LastSeenTick { get; set; } = -1;
        public Cell Investigate { get; set; }
        public Cell SearchGoal { get; set; }
        public int SearchUntil { get; set; }
        public int RouteIndex { get; set; }
        public int RouteDirection { get; set; } = 1;
        public int WaitUntil { get; set; }
        public int RadioAt { get; set; } = -1;
        public int ReportProgress { get; set; }
        public bool Reported { get; set; }
        public List<string> BodiesSeen { get; set; } = new List<string>();
        public void Observe(Cell position, int tick, int delta, GuardSpec config, bool hostileAction = false, double distance = 0)
        {
            LastKnown = StealthMath.Copy(position); LastSeenTick = tick;
            double rate=1-(1-config.FarDetectionMultiplier)*Math.Min(1,Math.Max(0,distance)/Math.Max(1,config.SightRange));
            Suspicion = hostileAction || Identified ? 1 : Math.Min(1, Suspicion + (double)delta * rate / Math.Max(1, config.DetectionTicks));
            if(Suspicion>=1)FullSuspicionUntil=tick+config.FullSuspicionHoldTicks;
            Investigate = StealthMath.Copy(position); SearchGoal=null; SearchUntil = tick + config.SearchTicks;
            Mode = Suspicion >= 1 ? "Combat" : Suspicion >= .5 ? "Suspicious" : "Observing";
            if (Mode == "Combat") { Alarmed = Identified = true; if (RadioAt < 0) RadioAt = tick + config.RadioDelayTicks; }
        }
        public void LoseSight(int tick, int delta, GuardSpec config)
        {
            if (Mode == "Combat") Mode = "Search";
            if (Mode == "Observing") Mode = "Patrol";
            if (Mode == "Suspicious") { Mode = "Investigate"; SearchUntil = tick + config.SearchTicks; }
            if(tick>=FullSuspicionUntil)Suspicion = Math.Max(Alarmed ? .5 : 0, Suspicion - (double)delta / Math.Max(1, config.DetectionTicks * 3));
            if ((Mode == "Search" || Mode == "Investigate") && tick >= SearchUntil) { Mode = "Patrol"; Investigate = null; }
        }
        public bool Hear(Cell point, int tick, GuardSpec config, bool dangerous)
        {
            if (Mode == "Combat" || Mode == "Reporting" || (!dangerous && (Alarmed || Mode != "Patrol"))) return false;
            Investigate = StealthMath.Copy(point); SearchGoal=null; SearchUntil = tick + config.SearchTicks;
            Mode = dangerous ? "Search" : "Investigate";
            if (dangerous) Alarmed = true;
            Suspicion = Math.Max(Suspicion, dangerous ? .5 : .25);
            return true;
        }
        // A radio report is a timestamped observation, never a live target reference.
        public void Receive(GuardMemory sender, int tick, GuardSpec config)
        {
            if (!sender.Identified || sender.LastKnown == null || sender.LastSeenTick <= LastSeenTick) return;
            LastKnown = StealthMath.Copy(sender.LastKnown); LastSeenTick = sender.LastSeenTick;
            // A report suggests where to investigate. Each receiver must identify
            // the actor for itself before gaining permission to attack or report.
            Suspicion = Math.Max(Suspicion,.5);
            if (Mode != "Combat") { Investigate = StealthMath.Copy(LastKnown); SearchGoal=null; Mode = Identified ? "Search" : "Investigate"; SearchUntil = tick + config.SearchTicks; }
        }
        public void NextWaypoint(PatrolRoute route)
        {
            if (route.Points.Count <= 1) { RouteIndex = 0; return; }
            if (!route.PingPong) RouteIndex = (RouteIndex + 1) % route.Points.Count;
            else { if (RouteIndex + RouteDirection >= route.Points.Count || RouteIndex + RouteDirection < 0) RouteDirection = -RouteDirection; RouteIndex += RouteDirection; }
            WaitUntil = 0;
        }
    }
    public static class StealthMath
    {
        public static Cell Copy(Cell c) => c == null ? null : new Cell { X = c.X, Z = c.Z };
        public static double Distance(Cell a, Cell b) => Math.Sqrt((double)(a.X-b.X)*(a.X-b.X)+(double)(a.Z-b.Z)*(a.Z-b.Z));
        public static bool InTakedownReach(Cell from, Cell to)
        {
            int dx=Math.Abs(to.X-from.X),dz=Math.Abs(to.Z-from.Z);
            // Union of the four directional T footprints: eight neighbors and four axial cells at distance two.
            return dx<=1&&dz<=1&&(dx+dz)>0 || dx==2&&dz==0 || dx==0&&dz==2;
        }
        public static bool InCone(Cell from, Cell to, int facing, double range, double angle)
        {
            double dx=to.X-from.X,dz=to.Z-from.Z,dist=Math.Sqrt(dx*dx+dz*dz);
            if(dist>range)return false;
            if(dist<.001||angle>=360)return true;
            double dot=(facing==0?dz:facing==1?dx:facing==2?-dz:-dx)/dist;
            return dot>=Math.Cos(angle*Math.PI/360)-.00001;
        }
        public static bool PasswordMatches(Rule rule,string input)
        {
            if(input==null)return false;
            if(rule.PasswordTrimInput)input=input.Trim();
            // Numeric keypads accept full-width IME digits, without changing text passwords or leading zeroes.
            if(!string.IsNullOrEmpty(rule.Password)&&rule.Password.All(c=>c>='0'&&c<='9'))
                input=new string(input.Select(c=>c>='０'&&c<='９'?(char)('0'+c-'０'):c).ToArray());
            return string.Equals(rule.Password??"",input,rule.PasswordCaseSensitive?StringComparison.Ordinal:StringComparison.OrdinalIgnoreCase);
        }
        public static IEnumerable<Issue> Validate(MapSpec map)
        {
            var s=map.Stealth;if(s==null||!map.Components.Any(c=>(c.Type??"").Split(',')[0].Trim()=="RimCampaign.Runtime.StealthMapComponent"))yield break;
            Issue Error(string text)=>new Issue{Path=map.Id+"/Stealth",Message=text};
            bool Valid(double n,double min,double max)=>!double.IsNaN(n)&&!double.IsInfinity(n)&&n>=min&&n<=max;
            bool Inside(Cell c)=>c!=null&&c.X>=0&&c.Z>=0&&c.X<map.Width&&c.Z<map.Height;
            bool Pawn(string id)=>map.Roles.Any(r=>id==r||id=="role:"+r)||map.Objects.Any(o=>o.Id==id&&(o.Kind=="Pawn"||o.Kind=="PawnKind"));
            if(!Pawn(s.Actor))yield return Error("Select a stealth actor role or pawn placement.");
            if(!Valid(s.PlayerSight,1,100)||s.UpdateTicks<1||s.UpdateTicks>60||s.TakedownTicks<1||!Valid(s.GunshotRadius,0,200))yield return Error("Invalid stealth timing or ranges.");
            var names=s.Routes.Cast<Named>().Concat(s.Guards).Concat(s.Devices).ToList();
            if(names.Any(n=>string.IsNullOrWhiteSpace(n.Id))||names.GroupBy(n=>n.Id).Any(g=>g.Count()>1))yield return Error("Stealth IDs must be nonempty and unique.");
            foreach(var route in s.Routes)if(route.Points.Count==0||route.Points.Any(p=>!Inside(p.Position)||p.WaitTicks<0||p.Facing<0||p.Facing>3))yield return Error("Invalid patrol route: "+route.Name);
            if(s.Guards.GroupBy(g=>g.Pawn).Any(g=>g.Count()>1))yield return Error("A pawn may have only one guard controller.");
            foreach(var g in s.Guards){
                if(!Valid(g.FarDetectionMultiplier,.01,1)||g.FullSuspicionHoldTicks<0)yield return Error("Invalid suspicion timing: "+g.Name);
                if(!Pawn(g.Pawn)||g.Pawn==s.Actor)yield return Error("Invalid guard pawn: "+g.Name);
                if(g.Route!=""&&!s.Routes.Any(r=>r.Id==g.Route))yield return Error("Missing patrol route: "+g.Name);
                if(!Valid(g.SightRange,1,100)||!Valid(g.ViewAngle,1,360)||!Valid(g.HearingMultiplier,0,5)||!Valid(g.RadioRange,0,500)||!Valid(g.SearchRadius,0,100)||g.DetectionTicks<1||g.SearchTicks<1||g.ReportTicks<1||g.RadioDelayTicks<1)yield return Error("Invalid guard senses/timing: "+g.Name);
                if(g.Uplink!=""&&!s.Devices.Any(d=>d.Id==g.Uplink&&d.Kind=="Uplink"))yield return Error("Missing guard uplink: "+g.Name);
            }
            foreach(var d in s.Devices){
                if(d.Kind=="Camera"&&d.DetectionTicks<1)yield return Error("Camera detection time must be positive: "+d.Name);
                if(!new[]{"Camera","Broadcast","Uplink","Password","Interaction","SupplyCrate"}.Contains(d.Kind)||!Inside(d.Position)||d.UseTicks<1||d.ReportTicks<1||d.Facing<0||d.Facing>3||!Valid(d.SightRange,1,100)||!Valid(d.ViewAngle,1,360)||!Valid(d.Radius,0,200))yield return Error("Invalid device: "+d.Name);
                if(d.AutoReport&&(d.Kind!="Camera"||string.IsNullOrEmpty(d.Uplink)))yield return Error("Automatic camera reporting needs an uplink: "+d.Name);
                if(d.Host!=""&&!map.Objects.Any(o=>o.Id==d.Host&&o.Kind=="Thing"))yield return Error("Missing device host building: "+d.Name);
                if(d.Uplink!=""&&!s.Devices.Any(u=>u.Id==d.Uplink&&u.Kind=="Uplink"))yield return Error("Missing camera uplink: "+d.Name);
                if(d.Kind=="Password"&&!map.Rules.Any(r=>r.Id==d.PasswordRule&&r.Events.Any(e=>e.Kind=="Password")))yield return Error("Password device needs a Password trigger: "+d.Name);
            }
            foreach(var c in s.Conversations){
                if(c.First==c.Second||!s.Guards.Any(g=>g.Pawn==c.First)||!s.Guards.Any(g=>g.Pawn==c.Second)||!Valid(c.ParticipantDistance,.5,30)||c.Lines.Count==0)yield return Error("Invalid patrol conversation: "+c.Name);
                foreach(var line in c.Lines)if(line.Kind!="Communication"||line.Communication==null||line.Communication.Errors().Any()||!new[]{c.First,c.Second}.Contains(line.Communication.Speaker))yield return Error("Patrol conversation needs valid participant communication lines: "+c.Name);
            }
            if(s.Conversations.GroupBy(c=>c.Id).Any(g=>g.Count()>1))yield return Error("Patrol conversation IDs must be unique.");
        }
    }
}
