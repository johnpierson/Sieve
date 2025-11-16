using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using PullRequestForRevit.Services;
using Nice3point.Revit.Toolkit.External;

namespace PullRequestForRevit.Commands;

/// <summary>
///     External command entry point - Show/hide WebView pane
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
public class StartupCommand : ExternalCommand
{
    public override void Execute()
    {
        try
        {
            Logger.Instance.LogInfo("StartupCommand executed");

            var panel = UiApplication.GetDockablePane(new DockablePaneId(PullRequestForRevit.Application.PullRequestForRevitDockablePaneId));

            if (panel is not null)
            {
                if (panel.IsShown())
                {
                    panel.Hide();
                    Logger.Instance.LogInfo("Dockable pane hidden");
                }
                else
                {
                    panel.Show();
                    Logger.Instance.LogInfo("Dockable pane shown");
                }
            }
            else
            {
                Logger.Instance.LogWarning("Dockable pane not found");
            }
        }
        catch (Exception ex)
        {
            Logger.Instance.LogError("Error in StartupCommand", ex);
            throw;
        }
    }
}

