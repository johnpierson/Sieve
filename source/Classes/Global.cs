using c4r.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using View = Autodesk.Revit.DB.View;

namespace Sieve.Classes
{
    internal class Global
    {
        internal static Dictionary<string, View>
            CurrentSessionModifiedViews = new Dictionary<string, View>();


        internal static List<View> FlaggedViews = new List<View>();

        internal static ClippyWindow clippyWindow { get; set; }


    }
}
