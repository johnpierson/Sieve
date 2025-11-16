using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sieve.Classes
{
    internal class Global
    {
        internal static Dictionary<string, View>
            CurrentSessionModifiedViews = new Dictionary<string, View>();


        internal static List<View> FlaggedViews = new List<View>();

    }
}
