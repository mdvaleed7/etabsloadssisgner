using EtabsWindAutomation;

public sealed class cPlugin
{
    public void Main(ref ETABSv1.cSapModel SapModel, ref ETABSv1.cPluginCallback ISapPlugin)
    {
        var exitCode = 0;

        try
        {
            WindPluginRunner.Run(SapModel);
        }
        catch (Exception ex)
        {
            exitCode = 1;
            WindPluginRunner.TryWriteFailureLog(ex);
        }
        finally
        {
            ISapPlugin.Finish(exitCode);
        }
    }

    public void Info(ref string Text)
    {
        Text = "IS 875 wind load automation: load patterns, cases, combinations, story forces, and optional modifiers.";
    }
}

