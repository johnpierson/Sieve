using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sieve.Classes
{
    internal class Global
    {
        internal static Dictionary<string, ViewSheet>
            CurrentSessionModifiedSheets = new Dictionary<string, ViewSheet>();


        internal static List<ViewSheet> FlaggedSheets = new List<ViewSheet>();

    }
}
