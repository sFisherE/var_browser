using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace var_browser
{
    public class DAZMorphMgr
    {
        public static DAZMorphMgr singleton = new DAZMorphMgr();


        public Dictionary<string, DAZMorphVertex[]> cache = new Dictionary<string, DAZMorphVertex[]>();


    }
}
