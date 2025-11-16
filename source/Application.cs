using System.Diagnostics.SymbolStore;
using Autodesk.Revit.DB.Events;
using Nice3point.Revit.Toolkit.External;
using Sieve.Classes;
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

            Application.ControlledApplication.DocumentChanged += ControlledApplicationOnDocumentChanged;
        }

        private void ControlledApplicationOnDocumentChanged(object? sender, DocumentChangedEventArgs e)
        {
            var currentDoc = e.GetDocument();
            var docId = currentDoc.CreationGUID;
            var sheetFilter = new ElementClassFilter(typeof(ViewSheet));

            //first add modified sheets to dictionary
            var modifiedSheets = e.GetModifiedElementIds(sheetFilter);

            foreach (var id in modifiedSheets)
            {
                var sheet = currentDoc.GetElement(id) as ViewSheet;
                if (sheet != null)
                {
                    var sheetId = sheet.Id.Value;

                    string sheetLookup = $"{docId}_{sheetId}";

                    Global.CurrentSessionModifiedSheets.TryAdd(sheetLookup, sheet);
                }
            }

            //if any deleted elements are sheets, remove from dictionary
            var deletedSheets = e.GetDeletedElementIds().Select(s => s.Value).ToList();

            if (!deletedSheets.Any()) return;
            foreach (var id in deletedSheets)
            {
                string sheetLookup = $"{docId}_{id}";
                if (Global.CurrentSessionModifiedSheets.ContainsKey(sheetLookup))
                {
                    Global.CurrentSessionModifiedSheets.Remove(sheetLookup);
                }
            }

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


        private bool CheckSheetsEdited(Document doc)
        {
            var docId = doc.CreationGUID.ToString();
            //block save
            if (!Global.CurrentSessionModifiedSheets.Any()) return false;


            List<string> keysToCheck = new List<string>();

            foreach (var key in Global.CurrentSessionModifiedSheets.Keys)
            {
                if (key.StartsWith(docId))
                {
                    keysToCheck.Add(key);
                }
            }

            foreach (var key in keysToCheck)
            {
                Global.CurrentSessionModifiedSheets.TryGetValue(key, out var value);

                if (value.get_Parameter(BuiltInParameter.SHEET_NAME).AsString().Contains(" Copy"))
                {
                    
                }
            }

        }
    }
}