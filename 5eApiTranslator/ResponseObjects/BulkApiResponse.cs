using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _5eApiTranslator
{
    class BulkApiResponse
    {
        public int count { get; set; }
        public List <BaseApiClass> results { get; set; }
    }
}
