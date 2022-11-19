using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _5eApiTranslator.Models
{
    class DamageComposite
    {
        public BaseApiClass damage_type { get; set; }
        public Dictionary<string,string> damage_at_slot_level { get; set; }
    }
}
