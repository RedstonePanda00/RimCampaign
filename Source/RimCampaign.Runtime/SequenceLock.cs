using RimCampaign.Core;
using RimCampaign.SDK;
namespace RimCampaign.Runtime {
    [CampaignModule("rc.sequenceLock","Action","Ordered interaction: Target state key, Value index (1-based), Text sequence length. Steps on completion, Else on reset.")]
    public sealed class SequenceLock:IActionModule {
        public bool Execute(Act a,IHost host,string source,double delta){var c=(CampaignController)host;c.State.Variables.TryGetValue(a.Target,out var current);int length=int.Parse(a.Text);
            if(current>=length)return true;
            if(a.Value==current+1){c.State.Variables[a.Target]=a.Value;if(a.Value==length)c.QueueStory(a.Steps,source);}
            else{c.State.Variables[a.Target]=0;c.QueueStory(a.Else,source);}return true;}
    }
}
