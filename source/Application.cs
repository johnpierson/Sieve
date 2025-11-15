using Autodesk.Revit.DB.Events;
using Nice3point.Revit.Toolkit.External;
using Sieve.Commands;

namespace Sieve
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    [UsedImplicitly]
    public class Application : ExternalApplication
    {
        public override void OnStartup()
        {
            CreateRibbon();

            Application.ControlledApplication.ApplicationInitialized += ControlledApplicationOnApplicationInitialized;
           
        }

        private void ControlledApplicationOnApplicationInitialized(object? sender, ApplicationInitializedEventArgs e)
        {
            Application.ControlledApplication.DocumentSynchronizingWithCentral += OnDocSynchronizing;

            Application.ControlledApplication.DocumentSaving += OnDocSaving;
        }

        private void OnDocSaving(object? sender, DocumentSavingEventArgs e)
        {
            var version = Document.GetDocumentVersion(e.Document);

            var versionGuid = version.VersionGUID;

            var allChanges = e.Document.GetChangedElements(versionGuid);

            var ids = allChanges.GetCreatedElementIds();
        }


        private void OnDocSynchronizing(object sender, DocumentSynchronizingWithCentralEventArgs e)
        {
            var allChanges = e.Document.GetChangedElements(Guid.Empty);
        }

        private void CreateRibbon()
        {
            var panel = Application.CreatePanel("Commands", "Sieve");

            panel.AddPushButton<StartupCommand>("Execute")
                .SetImage("/Sieve;component/Resources/Icons/RibbonIcon16.png")
                .SetLargeImage("/Sieve;component/Resources/Icons/RibbonIcon32.png");
        }
    }
}