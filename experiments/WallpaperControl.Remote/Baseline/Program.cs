namespace RemoteExperiment.Baseline;
static class Program
{
    [STAThread] static void Main() { ApplicationConfiguration.Initialize(); Application.Run(new Form { Text = "Remote experiment size baseline only" }); }
}
