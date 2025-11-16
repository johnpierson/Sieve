using System.Diagnostics.SymbolStore;
using Autodesk.Revit.DB.Events;
using c4r.UI;
using Nice3point.Revit.Toolkit.External;
using Sieve.Classes;
using Sieve.Commands;
using View = Autodesk.Revit.DB.View;

namespace Sieve
{
    /// <summary>
    /// Application entry point
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
            var viewFilter = new ElementClassFilter(typeof(View));

            //first add new views to dictionary 
            var newViews = e.GetAddedElementIds(viewFilter);

            foreach (var id in newViews)
            {
                if (currentDoc.GetElement(id) is View view)
                {
                    var sheetId = view.Id.Value;

                    string viewLookup = $"{docId}_{sheetId}";

                    Global.CurrentSessionModifiedViews.TryAdd(viewLookup, view);
                }
            }


            //now add modified views to dictionary
            var modifiedViews = e.GetModifiedElementIds(viewFilter);

            foreach (var id in modifiedViews)
            {
                if (currentDoc.GetElement(id) is View view)
                {
                    var sheetId = view.Id.Value;

                    string viewLookup = $"{docId}_{sheetId}";

                    Global.CurrentSessionModifiedViews.TryAdd(viewLookup, view);
                }
            }

            //if any deleted elements are sheets, remove from dictionary
            var deletedViews = e.GetDeletedElementIds().Select(s => s.Value).ToList();

            if (!deletedViews.Any()) return;
            foreach (var id in deletedViews)
            {
                string viewLookup = $"{docId}_{id}";
                if (Global.CurrentSessionModifiedViews.ContainsKey(viewLookup))
                {
                    Global.CurrentSessionModifiedViews.Remove(viewLookup);
                }
            }

        }

        private void OnDocSaving(object? sender, DocumentSavingEventArgs e)
        {
            var currentDocument = e.Document;

            var blockSave = CheckViewsEdited(currentDocument);

            if (blockSave)
            {
                //cancel the save for now
                //e.Cancel();
                if (Global.clippyWindow is null)
                {
                    Global.clippyWindow = new ClippyWindow();

                    //parent the clippy window so it will just run.
                    new System.Windows.Interop.WindowInteropHelper(Global.clippyWindow).Owner = UiApplication.MainWindowHandle;

                    Global.clippyWindow.Top = UiApplication.MainWindowExtents.Top;
                    Global.clippyWindow.Left = UiApplication.MainWindowExtents.Left;
                }

                Global.clippyWindow.BubbleText.Text = "test";
                Global.clippyWindow.Show();
            }

            //clear all the current edits
            Global.CurrentSessionModifiedViews = new Dictionary<string, View>();
            Global.FlaggedViews.Clear();
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


        private bool CheckViewsEdited(Document doc)
        {
            var docId = doc.CreationGUID.ToString();
            //block save
            if (!Global.CurrentSessionModifiedViews.Any()) return false;


            List<string> keysToCheck = new List<string>();

            foreach (var key in Global.CurrentSessionModifiedViews.Keys)
            {
                if (key.StartsWith(docId))
                {
                    keysToCheck.Add(key);
                }
            }

            foreach (var key in keysToCheck)
            {
                Global.CurrentSessionModifiedViews.TryGetValue(key, out var value);

                var regex = new System.Text.RegularExpressions.Regex(@"Copy \d+$");

                var viewName = value.get_Parameter(BuiltInParameter.VIEW_NAME).AsString();

                if (regex.IsMatch(viewName))
                {
                    Global.FlaggedViews.Add(value);
                }
            }

            return Global.FlaggedViews.Any();
        }

        internal void RegisterClippyWindow()
        {
            Global.clippyWindow = new ClippyWindow();

            //parent the clippy window so it will just run.
            new System.Windows.Interop.WindowInteropHelper(Global.clippyWindow).Owner = UiApplication.MainWindowHandle;

            Global.clippyWindow.Top = UiApplication.MainWindowExtents.Top;
            Global.clippyWindow.Left = UiApplication.MainWindowExtents.Left;
        }

    }
}